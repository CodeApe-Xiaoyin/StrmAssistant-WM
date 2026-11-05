using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>查看全局代理当前是否生效（只读）。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Proxy/Status", "GET", Summary = "Global proxy status")]
    public class GlobalProxyStatusApi : IReturn<object>
    {
    }
}
