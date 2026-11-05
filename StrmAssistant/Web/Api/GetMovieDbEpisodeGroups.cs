using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using StrmAssistant.Dto;

namespace StrmAssistant.Web.Api
{
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/MovieDb/EpisodeGroups", "GET", Summary = "List TMDb episode groups of a series")]
    public class GetMovieDbEpisodeGroups : IReturn<MovieDbEpisodeGroupList>
    {
        [ApiMember(Name = "TmdbId", Description = "TMDb series id", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string TmdbId { get; set; }
    }
}
