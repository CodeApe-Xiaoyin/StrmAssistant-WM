using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>一次性健康检查：依赖、任务注册、缓存、内存。部署到新机器后先打这个。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Health", "GET", Summary = "Plugin health summary")]
    public class HealthApi : IReturn<object>
    {
    }
}
