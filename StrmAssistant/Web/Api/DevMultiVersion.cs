using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>调试用：查看一个条目的多版本情况。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Dev/MultiVersion", "GET", Summary = "Dev only: inspect alternate versions of an item")]
    public class DevMultiVersion : IReturn<object>
    {
        [ApiMember(Name = "ItemId", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string ItemId { get; set; }
    }
}
