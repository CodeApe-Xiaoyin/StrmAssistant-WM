using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using StrmAssistant.Dto;

namespace StrmAssistant.Web.Api
{
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/IntroDb/Marker", "GET", Summary = "Get intro marker from external providers")]
    public class GetIntroDbMarker : IReturn<IntroMarker>
    {
        [ApiMember(Name = "ImdbId", Description = "IMDb id", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string ImdbId { get; set; }

        [ApiMember(Name = "Season", Description = "Season number", IsRequired = true, DataType = "int", ParameterType = "query")]
        public int Season { get; set; }

        [ApiMember(Name = "Episode", Description = "Episode number", IsRequired = true, DataType = "int", ParameterType = "query")]
        public int Episode { get; set; }
    }
}
