using System;
using System.Linq;
using System.Reflection;
using MediaBrowser.Common.Net;
using StrmAssistant.Options;

namespace StrmAssistant.Common.Hooks
{
    /// <summary>
    /// 接管 Emby MovieDb 提供程序的请求（与 PRO 的做法一致）。
    ///
    /// PRO 不自己写抓取器，而是 patch `MovieDbProviderBase.GetMovieDbResponse`：
    /// 改写请求 URL（替代 API/图片地址、替代 key、语言），provider 照常返回结果。
    /// 这里做同样的事，好处是**普通刷新/首次扫描也能拿到中文元数据**，
    /// 而不是必须跑一遍「元数据增强」任务。
    /// </summary>
    public static class MovieDbProviderPatch
    {
        public static bool Installed { get; private set; }

        public static string TryInstall()
        {
            if (Installed) return "already";

            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "MovieDb");
                if (asm == null) return "没有加载 MovieDb 程序集（未安装该插件）";

                var type = asm.GetType("MovieDb.MovieDbProviderBase");
                if (type == null) return "找不到 MovieDbProviderBase";

                var prefix = typeof(MovieDbProviderPatch).GetMethod(nameof(RewriteRequest),
                    BindingFlags.NonPublic | BindingFlags.Static);
                var postfixLang = typeof(MovieDbProviderPatch).GetMethod(nameof(PreferChineseLanguages),
                    BindingFlags.NonPublic | BindingFlags.Static);

                var messages = new System.Collections.Generic.List<string>();

                var r1 = HarmonyHelper.PatchPrefix(type, "GetMovieDbResponse", prefix,
                    "StrmAssistant.MovieDb.Request", out var p1);
                messages.Add("GetMovieDbResponse:" + (p1 > 0 ? "ok" : r1));

                if (postfixLang != null)
                {
                    var r2 = HarmonyHelper.PatchPostfix(type, "GetMovieDbMetadataLanguages", postfixLang,
                        "StrmAssistant.MovieDb.Language", out var p2);
                    messages.Add("GetMovieDbMetadataLanguages:" + (p2 > 0 ? "ok" : r2));
                }

                // 结果后处理：PRO 的做法是 patch GenericMovieDbInfo<T>.ProcessMainInfo
                // （同步方法，拿得到 MetadataResult 与 CompleteMovieData，能在导入前改数据）
                var postProcess = typeof(MovieDbProviderPatch).GetMethod(nameof(ProcessMainInfoPostfix),
                    BindingFlags.NonPublic | BindingFlags.Static);

                var genericInfo = asm.GetType("MovieDb.GenericMovieDbInfo`1");
                if (genericInfo != null && postProcess != null)
                {
                    foreach (var entityTypeName in new[]
                             {
                                 "MediaBrowser.Controller.Entities.Movies.Movie",
                                 "MediaBrowser.Controller.Entities.TV.Series"
                             })
                    {
                        var entityType = Type.GetType(entityTypeName + ", MediaBrowser.Controller");
                        if (entityType == null) continue;

                        try
                        {
                            var closed = genericInfo.MakeGenericType(entityType);
                            var r = HarmonyHelper.PatchPostfix(closed, "ProcessMainInfo", postProcess,
                                "StrmAssistant.MovieDb.ProcessMainInfo." + entityType.Name, out var pn);
                            messages.Add("ProcessMainInfo<" + entityType.Name + ">:" + (pn > 0 ? "ok" : r));
                        }
                        catch (Exception ex)
                        {
                            messages.Add("ProcessMainInfo<" + entityType.Name + ">:" + ex.Message);
                        }
                    }
                }

                Installed = true;
                return string.Join(" | ", messages);
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                return "失败: " + inner.GetType().Name + ": " + inner.Message;
            }
        }

        /// <summary>改写 provider 的请求：替代地址 / 替代 key / 强制中文。</summary>
        private static void RewriteRequest(HttpRequestOptions options)
        {
            try
            {
                if (options?.Url == null) return;

                var meta = Plugin.Instance?.GetPluginOptions()?.MetadataEnhanceOptions;
                if (meta == null) return;

                var before = options.Url;

                // 1) 替代 API/图片地址 + 替代 key（复用已有逻辑）
                options.Url = CommonUtility.ApplyAltMovieDbUrl(options.Url);

                // 2) 补 API key（留空时用系统默认，避免拼出空 api_key → 401）
                if (options.Url.IndexOf("api_key=", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var key = CommonUtility.ResolveMovieDbApiKey();
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        options.Url = System.Text.RegularExpressions.Regex.Replace(
                            options.Url, @"api_key=[^&]*", "api_key=" + key);
                    }
                }

                // 3) 中文优先：把 language=xx 改成 zh-CN
                if (meta.ChineseMovieDb && options.Url.IndexOf("language=", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    options.Url = System.Text.RegularExpressions.Regex.Replace(
                        options.Url, @"language=[^&]*", "language=zh-CN");
                }

                // 4) 缓存策略：与 PRO 一致（CacheMode=1，缓存 30 天）
                options.CacheMode = (CacheMode)1;
                options.CacheLength = TimeSpan.FromDays(30);

                if (!string.Equals(before, options.Url, StringComparison.Ordinal))
                {
                    Plugin.Instance?.Logger?.Info("MovieDbPatch - 请求已改写: " + Trim(options.Url));
                }
            }
            catch (Exception e)
            {
                Plugin.Instance?.Logger?.Info("MovieDbPatch - 改写失败: " + e.Message);
            }
        }

        /// <summary>语言偏好：中文优先。</summary>
        private static void PreferChineseLanguages(ref string[] __result)
        {
            try
            {
                var meta = Plugin.Instance?.GetPluginOptions()?.MetadataEnhanceOptions;
                if (meta?.ChineseMovieDb != true) return;
                if (__result == null) return;
                var preferred = new System.Collections.Generic.List<string> { "zh-CN" };

                var fallbacks = (meta.FallbackLanguages ?? string.Empty)
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length > 0);

                foreach (var f in fallbacks)
                {
                    if (!preferred.Any(p => string.Equals(p, f, StringComparison.OrdinalIgnoreCase))) preferred.Add(f);
                }

                __result = preferred.Concat(__result ?? Array.Empty<string>())
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch
            {
                // 语言偏好失败不影响抓取
            }
        }

        /// <summary>
        /// 对应 PRO 对 GenericMovieDbInfo&lt;T&gt;.ProcessMainInfo 的 patch：
        /// 在 provider 把数据导入条目之前，改条目与原始数据（tagline / overview）。
        /// </summary>
        private static void ProcessMainInfoPostfix(object __0, object __3)
        {
            try
            {
                var item = __0?.GetType().GetProperty("Item")?.GetValue(__0)
                           as MediaBrowser.Controller.Entities.BaseItem;

                // 「阻止合集自动创建」：TMDB 返回 belongs_to_collection 时，MovieDb 在这里给条目挂上合集，
                // Emby 随后据此自动建 BoxSet。这里把它摘掉。item 是本次刮削的新结果，不是库里的条目，
                // 所以只拦新增，已有的合集关系和手动新建合集都不受影响。
                if (item != null && Plugin.Instance?.CurrentOptions?.ExperienceEnhanceOptions?.NoBoxsetsAutoCreation == true &&
                    item.Collections != null && item.Collections.Length > 0)
                {
                    Plugin.Instance?.Logger?.Info("NoBoxsets - 已阻止自动创建合集: " +
                                                  string.Join(", ", item.Collections.Select(c => c?.Name)) +
                                                  "（条目 " + item.Name + "）");
                    item.Collections = item.Collections.Take(0).ToArray();
                }

                var meta = Plugin.Instance?.GetPluginOptions()?.MetadataEnhanceOptions;
                if (meta == null) return;

                var dataType = __3?.GetType();
                var changed = new System.Collections.Generic.List<string>();

                // 1) 清空 tagline
                if (meta.ClearTagline)
                {
                    if (item != null && !string.IsNullOrEmpty(item.Tagline))
                    {
                        item.Tagline = string.Empty;
                        changed.Add("item.tagline");
                    }

                    var dataTagline = dataType?.GetProperty("tagline")?.GetValue(__3) as string;
                    if (!string.IsNullOrEmpty(dataTagline))
                    {
                        dataType.GetProperty("tagline")?.SetValue(__3, string.Empty);
                        changed.Add("data.tagline");
                    }
                }

                // 2) 非中文简介屏蔽
                if (meta.BlockNonFallbackLanguage && item != null &&
                    !string.IsNullOrWhiteSpace(item.Overview) && !HasChinese(item.Overview))
                {
                    item.Overview = string.Empty;
                    dataType?.GetProperty("overview")?.SetValue(__3, string.Empty);
                    changed.Add("overview(非中文)");
                }

                // 3) 优先繁体
                if (meta.PreferTraditionalChinese && item != null && !string.IsNullOrWhiteSpace(item.Overview))
                {
                    var traditional = Common.ChineseTextHelper.ToTraditional(item.Overview);
                    if (!string.Equals(traditional, item.Overview, StringComparison.Ordinal))
                    {
                        item.Overview = traditional;
                        dataType?.GetProperty("overview")?.SetValue(__3, traditional);
                        changed.Add("overview(繁体)");
                    }
                }

                if (changed.Count > 0)
                {
                    Plugin.Instance?.Logger?.Info("MovieDbPatch - 结果后处理: " + (item?.Name ?? "?") +
                                                  " → " + string.Join(",", changed));
                }
            }
            catch (Exception e)
            {
                Plugin.Instance?.Logger?.Info("MovieDbPatch - 后处理失败: " + e.Message);
            }
        }

        /// <summary>备用：provider 返回结果后的后处理（当前不使用）。</summary>
        private static void PostProcessMetadata(object __result)
        {
            try
            {
                if (__result == null) return;

                var item = __result.GetType().GetProperty("Item")?.GetValue(__result)
                           as MediaBrowser.Controller.Entities.BaseItem;
                if (item == null) return;

                var meta = Plugin.Instance?.GetPluginOptions()?.MetadataEnhanceOptions;
                if (meta == null) return;

                var changed = new System.Collections.Generic.List<string>();

                if (meta.ClearTagline && !string.IsNullOrEmpty(item.Tagline))
                {
                    item.Tagline = string.Empty;
                    changed.Add("tagline");
                }

                if (meta.BlockNonFallbackLanguage &&
                    !string.IsNullOrWhiteSpace(item.Overview) && !HasChinese(item.Overview))
                {
                    item.Overview = string.Empty;
                    changed.Add("overview(非中文)");
                }

                if (meta.PreferTraditionalChinese && !string.IsNullOrWhiteSpace(item.Overview))
                {
                    var traditional = Common.ChineseTextHelper.ToTraditional(item.Overview);
                    if (!string.Equals(traditional, item.Overview, StringComparison.Ordinal))
                    {
                        item.Overview = traditional;
                        changed.Add("overview(繁体)");
                    }
                }

                if (changed.Count > 0)
                {
                    Plugin.Instance?.Logger?.Info("MovieDbPatch - 结果后处理: " + item.Name + " → " + string.Join(",", changed));
                }
            }
            catch (Exception e)
            {
                Plugin.Instance?.Logger?.Info("MovieDbPatch - 后处理失败: " + e.Message);
            }
        }

        private static bool HasChinese(string text)
        {
            foreach (var ch in text)
                if (ch >= 0x4E00 && ch <= 0x9FFF) return true;
            return false;
        }

        private static string Trim(string url) =>
            url.Length <= 140 ? url : url.Substring(0, 140) + "…";
    }
}
