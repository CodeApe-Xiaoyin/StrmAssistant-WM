using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using StrmAssistant.Dto;

namespace StrmAssistant.Web.Api
{
    /// <summary>
    /// 移植自 PRO 的 Web.Api 模式：ServiceStack 风格路由 + 插件内服务实现。
    /// 用于验证「自定义 MovieDb」是否真正作用于请求地址。
    /// </summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/MovieDb/EpisodeGroup", "GET", Summary = "Get TMDb episode group")]
    public class GetMovieDbEpisodeGroup : IReturn<MovieDbEpisodeGroupResponse>
    {
        [ApiMember(Name = "TmdbId", Description = "TMDb series id", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string TmdbId { get; set; }

        [ApiMember(Name = "GroupId", Description = "TMDb episode group id", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string GroupId { get; set; }
    }
}
