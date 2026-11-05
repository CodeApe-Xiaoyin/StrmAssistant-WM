using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>调试用：对单个条目请求一次 MDBList 评分，返回解析结果（不写库）。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Dev/MdbList", "POST", Summary = "Dev only: query MDBList rating for one item")]
    public class DevMdbList : IReturn<object>
    {
        [ApiMember(Name = "ItemId", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string ItemId { get; set; }
    }
}
