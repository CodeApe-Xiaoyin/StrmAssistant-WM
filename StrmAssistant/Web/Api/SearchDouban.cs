using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using StrmAssistant.Dto;

namespace StrmAssistant.Web.Api
{
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Douban/Search", "GET", Summary = "Search Douban")]
    public class SearchDouban : IReturn<DoubanSearchResponse>
    {
        [ApiMember(Name = "Q", Description = "Query", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string Q { get; set; }

        [ApiMember(Name = "Type", Description = "movie|tv", IsRequired = false, DataType = "string", ParameterType = "query")]
        public string Type { get; set; }
    }
}
