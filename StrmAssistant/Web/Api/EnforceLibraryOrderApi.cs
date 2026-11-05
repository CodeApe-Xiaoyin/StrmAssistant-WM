using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>按第一个管理员的顺序强制统一所有用户的媒体库顺序（对应 PRO 的「强制媒体库顺序」）。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/User/EnforceLibraryOrder", "POST", Summary = "Enforce library order of the first admin to all users")]
    public class EnforceLibraryOrderApi : IReturn<object>
    {
    }
}
