using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>调试用：临时改写 MDBList 的基地址（接桩服务验证请求格式与解析）。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Dev/MdbListBaseUrl", "POST", Summary = "Dev only: override MDBList base url")]
    public class DevMdbListBaseUrl : IReturn<object>
    {
        [ApiMember(Name = "Url", IsRequired = false, DataType = "string", ParameterType = "query")]
        public string Url { get; set; }
    }
}
