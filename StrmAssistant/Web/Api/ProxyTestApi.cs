using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>诊断：用当前代理配置请求指定 URL，返回结果便于排查网络问题。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/ProxyTest", "GET", Summary = "Test scraping http client")]
    public class ProxyTestApi : IReturn<object>
    {
        [ApiMember(Name = "Url", Description = "URL to request", IsRequired = false, DataType = "string", ParameterType = "query")]
        public string Url { get; set; }
    }
}
