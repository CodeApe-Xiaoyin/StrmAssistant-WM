using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>依赖自检：确认 TinyPinyin / ChineseConverter 在运行时可用。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/DependencyCheck", "GET", Summary = "Check managed dependencies")]
    public class DependencyCheckApi : IReturn<object>
    {
    }
}
