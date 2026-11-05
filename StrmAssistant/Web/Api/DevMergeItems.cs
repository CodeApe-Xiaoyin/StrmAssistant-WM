using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>
    /// 仅供联调：把两个条目合并成一个多版本条目，用来验证依赖多版本的功能。
    /// 只在 DebugMode 打开时可用。
    /// </summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Dev/MergeItems", "POST", Summary = "Dev only: merge two items into one multi-version item")]
    public class DevMergeItems : IReturnVoid
    {
        [ApiMember(Name = "Ids", Description = "Comma separated item ids", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string Ids { get; set; }
    }
}
