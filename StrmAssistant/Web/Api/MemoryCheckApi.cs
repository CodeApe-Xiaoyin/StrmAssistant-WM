using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>内存看门狗决策自检（不会真的重启进程）。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/MemoryCheck", "GET", Summary = "Evaluate memory watchdog decision")]
    public class MemoryCheckApi : IReturn<object>
    {
        [ApiMember(Name = "UsageMb", Description = "Simulated usage in MB", IsRequired = false, DataType = "int", ParameterType = "query")]
        public int? UsageMb { get; set; }

        [ApiMember(Name = "LimitMb", Description = "Limit in MB (blank uses configured value)", IsRequired = false, DataType = "int", ParameterType = "query")]
        public int? LimitMb { get; set; }

        [ApiMember(Name = "Idle", Description = "Whether server is idle", IsRequired = false, DataType = "bool", ParameterType = "query")]
        public bool? Idle { get; set; }

        [ApiMember(Name = "OffsetMinutes", Description = "Simulated minutes later", IsRequired = false, DataType = "int", ParameterType = "query")]
        public int? OffsetMinutes { get; set; }

        [ApiMember(Name = "Reset", Description = "Reset watchdog state", IsRequired = false, DataType = "bool", ParameterType = "query")]
        public bool? Reset { get; set; }
    }
}
