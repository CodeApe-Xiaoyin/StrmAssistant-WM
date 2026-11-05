using System.Collections.Generic;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using StrmAssistant.Dto;

namespace StrmAssistant.Web.Api
{
    /// <summary>
    /// 剧集组映射：把 TMDb 剧集组展开成「季/集 → 播放顺序」的映射表，
    /// 对应 PRO「剧集组支持」里用于重排集号的那一步。
    /// </summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/MovieDb/EpisodeGroupMap", "GET", Summary = "Get episode order map of a TMDb episode group")]
    public class GetMovieDbEpisodeGroupMap : IReturn<List<MovieDbEpisodeMapping>>
    {
        [ApiMember(Name = "TmdbId", Description = "TMDb series id", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string TmdbId { get; set; }

        [ApiMember(Name = "GroupId", Description = "TMDb episode group id", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string GroupId { get; set; }
    }
}
