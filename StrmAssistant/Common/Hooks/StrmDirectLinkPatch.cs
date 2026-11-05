using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;

namespace StrmAssistant.Common.Hooks
{
    /// <summary>
    /// 「Emby 直连」：客户端直接播放（static=true，不转码）远端 http .strm 时，返回 302 让客户端去拉直链，
    /// 不再由 Emby 下载再转发。
    ///
    /// 挂在 Emby 的流媒体服务上（VideoService.Get(GetVideoStream) / AudioService.Get(GetProgressiveAudioStream)），
    /// 而不是更早的 HTTP 入口：这里已经过了 Emby 自己的请求处理，返回值用 Emby 的 GetRedirectResult 生成。
    /// 需要转码（static 不是 true）、本地文件、strm 里不是 http 地址、客户端在黑名单里——都交回 Emby 照常处理。
    /// </summary>
    public static class StrmDirectLinkPatch
    {
        public static bool Installed { get; private set; }

        private static readonly ConcurrentDictionary<string, (string url, DateTime at)> Resolved =
            new ConcurrentDictionary<string, (string, DateTime)>();

        private static readonly HttpClient NoRedirectClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false
        }) { Timeout = TimeSpan.FromSeconds(8) };

        public static string TryInstall()
        {
            if (Installed) return "already";

            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                              .FirstOrDefault(a => a.GetName().Name == "Emby.Server.MediaEncoding") ??
                          Assembly.Load("Emby.Server.MediaEncoding");

                var targets = new[]
                {
                    ("Emby.Server.MediaEncoding.Api.Progressive.VideoService", "GetVideoStream", nameof(VideoPrefix)),
                    ("Emby.Server.MediaEncoding.Api.Progressive.AudioService", "GetProgressiveAudioStream", nameof(AudioPrefix))
                };

                var harmony = new HarmonyLib.Harmony("StrmAssistant.StrmDirectLink");
                var results = new System.Collections.Generic.List<string>();

                foreach (var (typeName, requestType, prefixName) in targets)
                {
                    var type = asm.GetType(typeName);
                    var method = type?.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "Get" && m.GetParameters().Length == 1 &&
                                             m.GetParameters()[0].ParameterType.Name == requestType);
                    if (method == null)
                    {
                        results.Add(requestType + ":none");
                        continue;
                    }

                    harmony.Patch(method, prefix: new HarmonyLib.HarmonyMethod(
                        typeof(StrmDirectLinkPatch).GetMethod(prefixName, BindingFlags.NonPublic | BindingFlags.Static)));
                    results.Add(requestType + ":ok");
                    Installed = true;
                }

                return string.Join(" | ", results);
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                return "失败: " + inner.GetType().Name + ": " + inner.Message;
            }
        }

        private static bool VideoPrefix(object __instance, object[] __args, ref Task<object> __result) =>
            Handle(__instance, __args, ref __result, false);

        private static bool AudioPrefix(object __instance, object[] __args, ref Task<object> __result) =>
            Handle(__instance, __args, ref __result, true);

        private static bool Handle(object service, object[] args, ref Task<object> result, bool audio)
        {
            try
            {
                var options = Plugin.Instance?.CurrentOptions?.ExperienceEnhanceOptions?.DirectLinkOptions;
                if (options == null) return true;
                if (audio ? !options.StrmDirectAudio : !options.StrmDirectVideo) return true;

                var request = args?.FirstOrDefault();
                if (request == null) return true;

                // 只处理直接播放（不转码）。网页播放器用的是 /videos/{id}/original.mp4：
                // Emby 要到 ProcessRequest 里才把它改成 Static=true，而这里在它之前执行，所以文件名也要认。
                var isStatic = Prop(request, "Static") is bool st && st;
                var streamFileName = Prop(request, "StreamFileName") as string;
                if (!isStatic && !string.IsNullOrEmpty(streamFileName) &&
                    string.Equals(Path.GetFileNameWithoutExtension(streamFileName), "original", StringComparison.OrdinalIgnoreCase))
                    isStatic = true;
                if (!isStatic) return true;

                var item = ResolveItem(Prop(request, "MediaSourceId") as string, Prop(request, "Id"));
                if (item == null) return true;
                if (audio ? !(item is Audio) : !(item is Video)) return true;

                var url = ReadHttpStrmTarget(item.Path);
                if (url == null) return true;

                var httpRequest = Prop(service, "Request");
                var client = ClientName(httpRequest);
                var userAgent = Prop(httpRequest, "UserAgent") as string ?? string.Empty;
                if (IsBlacklisted(options.StrmDirectClientBlacklist, client, userAgent))
                {
                    Plugin.Instance?.Logger?.Debug("StrmDirectLink - 客户端在黑名单，不直连: " + client);
                    return true;
                }

                if (options.StrmDirectFollowRedirect) url = FollowRedirects(url, userAgent);

                var factory = Prop(service, "ResultFactory");
                var redirect = factory?.GetType().GetMethod("GetRedirectResult", new[] { typeof(string) })
                    ?.Invoke(factory, new object[] { url });
                if (redirect == null) return true;

                Plugin.Instance?.Logger?.Info("StrmDirectLink - 302 直连 " + (audio ? "音乐" : "视频") + "「" + item.Name +
                                              "」→ " + SafeHost(url) + "（客户端 " + (client ?? "?") + "）");
                result = Task.FromResult(redirect);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Instance?.Logger?.Info("StrmDirectLink - 处理失败，交回 Emby: " + e.Message);
                return true;
            }
        }

        private static BaseItem ResolveItem(string mediaSourceId, object id)
        {
            var lm = Plugin.LibraryManager;
            if (lm == null) return null;

            // 多版本时 MediaSourceId 指向具体那个版本
            if (!string.IsNullOrWhiteSpace(mediaSourceId) && Guid.TryParse(mediaSourceId, out var sourceGuid))
            {
                var bySource = lm.GetItemById(sourceGuid);
                if (bySource != null) return bySource;
            }

            if (id is Guid g) return lm.GetItemById(g);
            var s = id?.ToString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (long.TryParse(s, out var internalId)) return lm.GetItemById(internalId);
            return Guid.TryParse(s, out var guid) ? lm.GetItemById(guid) : null;
        }

        /// <summary>.strm 文件里的第一行 http(s) 地址；本地路径、不是 strm 都返回 null。</summary>
        internal static string ReadHttpStrmTarget(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase)) return null;
            if (!File.Exists(path)) return null;

            var line = File.ReadLines(path).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
            if (line == null) return null;
            return line.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   line.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? line
                : null;
        }

        /// <summary>
        /// 跟踪 302 拿到最终地址（最多 5 跳）。带上客户端的 User-Agent：有的网盘直链和请求方 UA 绑定。
        /// 同一地址 + UA 的结果缓存 60 秒；跟踪失败就用原地址。
        /// </summary>
        private static string FollowRedirects(string url, string userAgent)
        {
            var key = url + "\n" + userAgent;
            if (Resolved.TryGetValue(key, out var hit) && (DateTime.UtcNow - hit.at).TotalSeconds < 60) return hit.url;

            var current = url;
            try
            {
                for (var i = 0; i < 5; i++)
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, current);
                    req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                    if (!string.IsNullOrWhiteSpace(userAgent)) req.Headers.TryAddWithoutValidation("User-Agent", userAgent);

                    using var resp = NoRedirectClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead)
                        .GetAwaiter().GetResult();
                    var code = (int)resp.StatusCode;
                    if (code < 300 || code >= 400 || resp.Headers.Location == null) break;

                    current = resp.Headers.Location.IsAbsoluteUri
                        ? resp.Headers.Location.ToString()
                        : new Uri(new Uri(current), resp.Headers.Location).ToString();
                }
            }
            catch (Exception e)
            {
                Plugin.Instance?.Logger?.Info("StrmDirectLink - 跟踪 302 失败，用原地址: " + e.Message);
                current = url;
            }

            if (Resolved.Count > 500) Resolved.Clear();
            Resolved[key] = (current, DateTime.UtcNow);
            return current;
        }

        /// <summary>客户端名称：X-Emby-Client 头 / 查询参数，或 Authorization 头里的 Client="…"。</summary>
        private static string ClientName(object httpRequest)
        {
            if (httpRequest == null) return null;
            var fromHeader = Header(httpRequest, "X-Emby-Client") ?? Query(httpRequest, "X-Emby-Client");
            if (!string.IsNullOrWhiteSpace(fromHeader)) return fromHeader;

            var auth = Header(httpRequest, "X-Emby-Authorization") ?? Header(httpRequest, "Authorization");
            if (string.IsNullOrEmpty(auth)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(auth, "Client=\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        internal static bool IsBlacklisted(string blacklist, string client, string userAgent)
        {
            if (string.IsNullOrWhiteSpace(blacklist)) return false;
            var words = blacklist.Split(new[] { ',', '，', ';', '；', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim()).Where(w => w.Length > 0);
            return words.Any(w => (client ?? string.Empty).IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  (userAgent ?? string.Empty).IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string SafeHost(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Scheme + "://" + u.Authority + "/…" : "?";

        private static object Prop(object o, string name)
        {
            if (o == null) return null;
            for (var t = o.GetType(); t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (p != null) return p.GetValue(o);
            }
            return null;
        }

        private static string Header(object httpRequest, string name) => Lookup(Prop(httpRequest, "Headers"), name);

        private static string Query(object httpRequest, string name) => Lookup(Prop(httpRequest, "QueryString"), name);

        private static string Lookup(object collection, string name)
        {
            if (collection == null) return null;
            try
            {
                // NameValueCollection / IHeaderDictionary 等都有 string 索引器
                var indexer = collection.GetType().GetProperties()
                    .FirstOrDefault(p => p.GetIndexParameters().Length == 1 &&
                                         p.GetIndexParameters()[0].ParameterType == typeof(string));
                var value = indexer?.GetValue(collection, new object[] { name });
                var text = value?.ToString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch
            {
                return null;
            }
        }
    }
}
