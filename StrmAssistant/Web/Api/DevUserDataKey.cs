using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>诊断（只读）：调用 Video 的静态 GetUserDataKey 并返回结果，用于核对"按库分离"。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Dev/UserDataKey", "GET", Summary = "Inspect user data keys")]
    public class DevUserDataKey : IReturn<object>
    {
        [ApiMember(Name = "Ids", Description = "Comma separated item ids", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string Ids { get; set; }
    }
}
