using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>诊断：直接调用 Emby 的可用插件更新接口，用来验证「抑制更新」挂钩是否生效。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/PluginUpdate/Probe", "GET", Summary = "Probe plugin update hook")]
    public class PluginUpdateProbeApi : IReturn<object>
    {
    }
}
