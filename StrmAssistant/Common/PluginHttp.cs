using System;
using System.Net;
using System.Net.Http;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 插件自己发出的 HTTP 请求。
    /// 填了代理地址时这里创建的客户端走代理；Emby 进程的全局代理另由 GlobalProxyService 管。
    /// </summary>
    public static class PluginHttp
    {
        public static HttpClient Create(TimeSpan? timeout = null)
        {
            var handler = new HttpClientHandler();

            try
            {
                // 填了代理地址，插件自己的请求就走代理（局域网地址直连）
                var proxy = ProxyConfig.Current;
                if (proxy != null)
                {
                    handler.Proxy = ProxyConfig.CreateWebProxy(proxy);
                    handler.UseProxy = true;
                }
            }
            catch
            {
                // 代理配置有问题就当直连
            }

            var client = new HttpClient(handler);
            if (timeout.HasValue) client.Timeout = timeout.Value;
            return client;
        }
    }
}
