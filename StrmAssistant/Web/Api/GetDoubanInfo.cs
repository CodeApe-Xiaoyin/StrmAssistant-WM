using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using StrmAssistant.Dto;

namespace StrmAssistant.Web.Api
{
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Douban/Info", "GET", Summary = "Get Douban info")]
    public class GetDoubanInfo : IReturn<DoubanInfo>
    {
        [ApiMember(Name = "Id", Description = "Douban id", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string Id { get; set; }

        [ApiMember(Name = "Type", Description = "movie|tv", IsRequired = false, DataType = "string", ParameterType = "query")]
        public string Type { get; set; }
    }
}
