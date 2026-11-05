using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>诊断（只读）：打印某个类型的运行时信息（程序集位置、ALC、指定方法名的方法列表）。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Dev/TypeProbe", "GET", Summary = "Inspect a runtime type")]
    public class DevTypeProbe : IReturn<object>
    {
        [ApiMember(Name = "Name", Description = "Full type name", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string Name { get; set; }

        [ApiMember(Name = "Method", Description = "Method name filter", IsRequired = false, DataType = "string", ParameterType = "query")]
        public string Method { get; set; }

        [ApiMember(Name = "Assembly", Description = "Assembly simple name (default: MediaBrowser.Controller)", IsRequired = false, DataType = "string", ParameterType = "query")]
        public string Assembly { get; set; }
    }
}
