using System;
using System.Net.Http;
using MediaBrowser.Model.Services;
using StrmAssistant.Common;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class ProxyTestService : IService
    {
        public object Get(ProxyTestApi request)
        {
            var url = string.IsNullOrWhiteSpace(request.Url) ? "https://api.themoviedb.org/3/" : request.Url;
            var proxy = ScrapingHttpClient.ConfiguredProxy;

            try
            {
                var client = ScrapingHttpClient.Get(); // 共享实例，不能 dispose
                using var response = client.GetAsync(url).GetAwaiter().GetResult();

                return new
                {
                    proxy = string.IsNullOrWhiteSpace(proxy) ? "direct" : proxy,
                    url,
                    status = (int)response.StatusCode,
                    ok = response.IsSuccessStatusCode
                };
            }
            catch (Exception e)
            {
                return new
                {
                    proxy = string.IsNullOrWhiteSpace(proxy) ? "direct" : proxy,
                    url,
                    status = 0,
                    ok = false,
                    error = e.GetBaseException().Message
                };
            }
        }
    }
}
