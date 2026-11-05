using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>调试用：查询某条目的媒体信息 JSON 是否已持久化（验证持久化开关联动）。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Dev/MediaInfoJson", "GET", Summary = "Dev only: check persisted mediainfo json")]
    [Route("/StrmAssistant/Dev/MediaInfoJson", "DELETE", Summary = "Dev only: delete persisted mediainfo json")]
    public class DevMediaInfoJson : IReturn<object>
    {
        [ApiMember(Name = "ItemId", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string ItemId { get; set; }
    }
}
