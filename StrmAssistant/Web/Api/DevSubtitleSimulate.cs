using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>调试用：模拟"字幕下载完成"后的处理（扫描条目同目录字幕并优化）。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Dev/SubtitleSimulate", "POST", Summary = "Simulate subtitle-downloaded handling")]
    public class DevSubtitleSimulate : IReturn<object>
    {
        [ApiMember(Name = "ItemId", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string ItemId { get; set; }
    }
}
