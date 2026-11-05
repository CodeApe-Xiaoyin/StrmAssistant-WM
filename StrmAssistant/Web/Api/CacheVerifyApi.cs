using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>对账：本地元数据缓存与媒体库的差异（只读，不修改任何东西）。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Cache/Verify", "GET", Summary = "Verify metadata cache against library")]
    public class CacheVerifyApi : IReturn<object>
    {
    }
}
