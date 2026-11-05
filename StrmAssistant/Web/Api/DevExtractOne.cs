using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>调试用：对单个条目跑一次提取（含持久化），便于验证联动。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Dev/ExtractMediaInfo", "POST", Summary = "Dev only: extract media info for one item")]
    public class DevExtractOne : IReturn<object>
    {
        [ApiMember(Name = "ItemId", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string ItemId { get; set; }

        [ApiMember(Name = "MakeGbKSubtitle", IsRequired = false, DataType = "bool", ParameterType = "query")]
        public bool MakeGbKSubtitle { get; set; }

        [ApiMember(Name = "ForceSerialize", IsRequired = false, DataType = "bool", ParameterType = "query")]
        public bool ForceSerialize { get; set; }
    }
}
