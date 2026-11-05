using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>调试用：读插件运行时的选项值，验证 UI 修改是否即时生效。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Dev/Options", "GET", Summary = "Dev only: read live plugin option values")]
    public class DevOptionsProbe : IReturn<object>
    {
    }
}
