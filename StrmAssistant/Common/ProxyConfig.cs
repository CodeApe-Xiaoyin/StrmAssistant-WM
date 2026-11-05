using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 代理设置的唯一入口。
    ///   · 协议看地址前缀：http:// / https:// / socks5:// / socks4://，不写前缀按 http。
    ///   · 填了地址：插件自己的请求（刮削、豆瓣、片头库……）走代理；
    ///     再打开「Emby 自身请求也走代理」：Emby 的全部出站请求也走代理。
    ///   · 局域网地址（127.x / 10.x / 172.16–31.x / 192.168.x / localhost）始终直连，
    ///     否则发到局域网 MoviePilot 的通知也会被送进代理。
    /// 以前另有「协议」「作用范围」两个单选，和地址、开关重复，已去掉（旧配置启动时迁移）。
    /// </summary>
    public static class ProxyConfig
    {
        private static readonly string[] LanBypass =
        {
            "localhost",
            @"127\.\d+\.\d+\.\d+",
            @"10\.\d+\.\d+\.\d+",
            @"192\.168\.\d+\.\d+",
            @"172\.(1[6-9]|2\d|3[01])\.\d+\.\d+"
        };

        /// <summary>把用户填的地址规范成代理 Uri；不合法返回 false。</summary>
        public static bool TryParse(string raw, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var text = raw.Trim();
            if (!text.Contains("://")) text = "http://" + text;
            if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) || string.IsNullOrEmpty(parsed.Host)) return false;

            switch (parsed.Scheme.ToLowerInvariant())
            {
                case "http":
                case "https":
                case "socks4":
                case "socks4a":
                case "socks5":
                    uri = parsed;
                    return true;
                case "socks5h": // .NET 不认 socks5h，按 socks5 处理（域名同样交给代理解析）
                    uri = new UriBuilder(parsed) { Scheme = "socks5" }.Uri;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>当前配置的代理（没填或不合法时为 null）。</summary>
        public static Uri Current
        {
            get
            {
                var raw = Plugin.Instance?.CurrentOptions?.GeneralOptions?.ProxyUrl;
                return TryParse(raw, out var uri) ? uri : null;
            }
        }

        /// <summary>Emby 自身请求是否也要走代理。</summary>
        public static bool GlobalEnabled =>
            Plugin.Instance?.CurrentOptions?.GeneralOptions?.GlobalProxy == true && Current != null;

        public static WebProxy CreateWebProxy(Uri uri) =>
            new WebProxy(uri, true, LanBypass);

        /// <summary>
        /// 旧配置迁移：以前协议是单独选的（地址写 http://… 但选了 SOCKS5）。
        /// 现在以地址前缀为准，所以把旧的选择写进地址里。返回 true 表示改动了。
        /// </summary>
        public static bool MigrateLegacy(Options.GeneralOptions options)
        {
            if (options == null) return false;
            var changed = false;
            if (string.Equals(options.ProxyMode, "Socks5", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(options.ProxyUrl) &&
                !options.ProxyUrl.Trim().StartsWith("socks", StringComparison.OrdinalIgnoreCase))
            {
                var text = options.ProxyUrl.Trim();
                var idx = text.IndexOf("://", StringComparison.Ordinal);
                options.ProxyUrl = "socks5://" + (idx >= 0 ? text.Substring(idx + 3) : text);
                changed = true;
            }

            if (!string.Equals(options.ProxyMode, "Http", StringComparison.OrdinalIgnoreCase))
            {
                options.ProxyMode = "Http";
                changed = true;
            }

            return changed;
        }

        /// <summary>经代理访问 generate_204，返回一句状态文字。</summary>
        public static async Task<string> TestAsync(Uri proxy)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                using var handler = new HttpClientHandler { Proxy = new WebProxy(proxy), UseProxy = true };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
                using var resp = await client.GetAsync("https://www.google.com/generate_204").ConfigureAwait(false);
                watch.Stop();
                var code = (int)resp.StatusCode;
                return code == 204 || code == 200
                    ? "可用 · " + watch.ElapsedMilliseconds + " ms"
                    : "可连接，但返回 HTTP " + code;
            }
            catch (Exception e)
            {
                var inner = e.InnerException ?? e;
                return "不可用 · " + (e is TaskCanceledException ? "超时" : inner.Message);
            }
        }

        public static string Scheme(Uri uri) => uri.Scheme.ToUpperInvariant();
    }
}
