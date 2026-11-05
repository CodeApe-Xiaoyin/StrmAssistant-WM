using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 把代理应用到 Emby 进程（「Emby 自身请求也走代理」打开时），并在地址变化后检测一次连通性。
    /// 设置的是 <see cref="HttpClient.DefaultProxy"/>：对新建的连接生效，已有长连接要重启 Emby 才会换。
    /// Emby 自己创建的 Handler 另由 GlobalProxyPatches 注入。
    /// </summary>
    public static class GlobalProxyService
    {
        private static string _applied = string.Empty;
        private static string _tested;
        private static System.Net.IWebProxy _original;

        public static string AppliedProxy => _applied;

        public static void Apply()
        {
            var options = Plugin.Instance?.GetPluginOptions()?.GeneralOptions;
            if (options == null) return;

            if (ProxyConfig.MigrateLegacy(options))
            {
                Plugin.Instance?.Logger?.Info("GlobalProxy - 旧的「代理协议」设置已写入地址: " + options.ProxyUrl);
                try { Plugin.Instance?.SavePluginOptionsSuppress(); } catch { }
            }

            var hasProxy = ProxyConfig.TryParse(options.ProxyUrl, out var uri);
            var wantGlobal = options.GlobalProxy && hasProxy;

            if (!wantGlobal)
            {
                if (_applied.Length > 0)
                {
                    HttpClient.DefaultProxy = _original ?? new System.Net.WebProxy(); // 还原成接管前的（环境变量 / 直连）
                    Plugin.Instance?.Logger?.Info("GlobalProxy - Emby 自身请求恢复直连（原代理 " + _applied + "）");
                    _applied = string.Empty;
                }
            }
            else if (!string.Equals(_applied, uri.ToString(), StringComparison.Ordinal))
            {
                if (_applied.Length == 0) _original = HttpClient.DefaultProxy;
                HttpClient.DefaultProxy = ProxyConfig.CreateWebProxy(uri);
                _applied = uri.ToString();
                Plugin.Instance?.Logger?.Info("GlobalProxy - Emby 全部出站请求改走代理: " + uri);
            }

            if (string.IsNullOrWhiteSpace(options.ProxyUrl))
            {
                options.ProxyServerStatus = string.Empty;
                _tested = null;
                return;
            }

            if (!hasProxy)
            {
                options.ProxyServerStatus = "地址无法识别，请写成 http://IP:端口 或 socks5://IP:端口";
                _tested = null;
                return;
            }

            var scope = wantGlobal ? "插件 + Emby 全部请求" : "仅插件请求";
            var key = uri + "|" + scope;
            if (key == _tested) return;
            _tested = key;

            options.ProxyServerStatus = ProxyConfig.Scheme(uri) + " · " + scope + " · 检测中…";
            var testing = uri;
            Task.Run(async () =>
            {
                var result = await ProxyConfig.TestAsync(testing).ConfigureAwait(false);
                var current = Plugin.Instance?.GetPluginOptions()?.GeneralOptions;
                if (current == null || _tested != key) return;
                current.ProxyServerStatus = ProxyConfig.Scheme(testing) + " · " + scope + " · " + result;
                Plugin.Instance?.Logger?.Info("GlobalProxy - 代理检测: " + current.ProxyServerStatus);
            });
        }
    }
}
