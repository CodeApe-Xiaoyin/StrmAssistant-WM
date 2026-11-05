using System.Collections.Generic;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/IntroDb/Chapters", "GET", Summary = "Read back chapters of an item")]
    public class GetEpisodeChapters : IReturn<List<ChapterInfo>>
    {
        [ApiMember(Name = "ItemId", Description = "Item id", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string ItemId { get; set; }
    }
}
