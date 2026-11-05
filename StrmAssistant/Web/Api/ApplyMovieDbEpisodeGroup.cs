using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>把 TMDb 剧集组的顺序应用到指定剧集条目（重排季/集号）。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/MovieDb/ApplyEpisodeGroup", "POST", Summary = "Apply TMDb episode group to a series")]
    public class ApplyMovieDbEpisodeGroup : IReturnVoid
    {
        [ApiMember(Name = "SeriesId", Description = "Emby series item id", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string SeriesId { get; set; }

        [ApiMember(Name = "TmdbId", Description = "TMDb series id", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string TmdbId { get; set; }

        [ApiMember(Name = "GroupId", Description = "TMDb episode group id", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string GroupId { get; set; }
    }
}
