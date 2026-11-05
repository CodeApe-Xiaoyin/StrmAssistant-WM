using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>TMDb 缓存统计（条目数、占用、命中/未命中）。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/CacheStats", "GET", Summary = "TMDB cache statistics")]
    public class CacheStatsApi : IReturn<object>
    {
    }
}
