using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>调试用：触发一次任务完成通知（走 Emby 通知 + 配置的 Webhook），用于验证联动。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Dev/NotifyTest", "POST", Summary = "Dev only: fire one task-completion notification")]
    public class DevNotifyTest : IReturn<object>
    {
        [ApiMember(Name = "Text", IsRequired = false, DataType = "string", ParameterType = "query")]
        public string Text { get; set; } = "联调测试";
    }
}
