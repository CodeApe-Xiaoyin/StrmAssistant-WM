using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>对一个字幕文件手动执行优化（用于验证与补救）。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Subtitle/Optimize", "POST", Summary = "Optimize a subtitle file")]
    public class SubtitleOptimizeApi : IReturn<object>
    {
        [ApiMember(Name = "Path", Description = "Absolute subtitle path", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string Path { get; set; }
    }
}
