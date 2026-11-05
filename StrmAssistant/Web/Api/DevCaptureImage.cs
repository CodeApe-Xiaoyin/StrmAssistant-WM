using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>调试用：对单个条目执行截图（可验证后处理与自定义 ffmpeg 路径）。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Dev/CaptureImage", "POST", Summary = "Dev only: capture image for one item")]
    public class DevCaptureImage : IReturn<object>
    {
        [ApiMember(Name = "ItemId", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string ItemId { get; set; }

        [ApiMember(Name = "Position", IsRequired = false, DataType = "int", ParameterType = "query")]
        public int Position { get; set; } = 10;
    }
}
