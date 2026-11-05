using System.Net.Http;
using MediaBrowser.Model.Services;
using StrmAssistant.Common;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class GlobalProxyStatusService : IService
    {
        public object Get(GlobalProxyStatusApi request)
        {
            var options = Plugin.Instance?.GetPluginOptions()?.GeneralOptions;

            return new
            {
                optionEnabled = options?.GlobalProxy == true,
                proxyUrl = options?.ProxyUrl ?? string.Empty,
                applied = GlobalProxyService.AppliedProxy,
                defaultProxy = HttpClient.DefaultProxy?.ToString() ?? "none"
            };
        }
    }
}
