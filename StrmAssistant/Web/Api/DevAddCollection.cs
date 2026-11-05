using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>调试用（只读之外的单一动作）：调用 Emby 的 AddCollection，用于验证"阻止合集自动创建"。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Dev/AddCollection", "POST", Summary = "Dev only: call CollectionManager.AddCollection")]
    public class DevAddCollection : IReturnVoid
    {
        [ApiMember(Name = "ItemId", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string ItemId { get; set; }

        [ApiMember(Name = "Name", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string Name { get; set; }
    }
}
