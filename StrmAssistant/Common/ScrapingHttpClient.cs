using System;
using System.Net;
using System.Net.Http;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 刮削用的 HTTP 客户端。配置了代理就走代理，否则直连。
    /// 对应 PRO 的 NetworkOptions 思路，但只作用于本插件自己发出的请求（不接管 Emby 全局流量）。
    /// </summary>
    public static class ScrapingHttpClient
    {
        private static HttpClient _cached;
        private static string _cachedProxy;

        public static string ConfiguredProxy =>
            Plugin.Instance?.GetPluginOptions()?.GeneralOptions?.ProxyUrl ?? string.Empty;

        public static bool ProxyEnabled => !string.IsNullOrWhiteSpace(ConfiguredProxy);

        public static HttpClient Get()
        {
            var proxy = ConfiguredProxy;

            if (_cached != null && string.Equals(_cachedProxy, proxy, StringComparison.Ordinal))
            {
                return _cached;
            }

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            if (ProxyConfig.TryParse(proxy, out var proxyUri))
            {
                handler.Proxy = ProxyConfig.CreateWebProxy(proxyUri);
                handler.UseProxy = true;
            }

            _cached = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            _cachedProxy = proxy;
            return _cached;
        }
    }
}
