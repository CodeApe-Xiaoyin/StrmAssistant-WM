using System;
using MediaBrowser.Model.Services;
using StrmAssistant.Common;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class MemoryCheckService : IService
    {
        public object Get(MemoryCheckApi request)
        {
            if (request.Reset == true) MemoryWatchdog.Reset();

            var configured = Plugin.Instance.GetPluginOptions().DashboardOptions?.MemoryOptions?.MemoryUsageLimitMB;
            var limit = request.LimitMb ?? configured;
            var usage = request.UsageMb ?? MemoryWatchdog.CurrentUsageMb;
            var idle = request.Idle ?? true;
            var now = DateTimeOffset.UtcNow.AddMinutes(request.OffsetMinutes ?? 0);

            var decision = MemoryWatchdog.Evaluate(usage, limit, idle, now);

            return new
            {
                usageMb = usage,
                limitMb = limit,
                idle,
                offsetMinutes = request.OffsetMinutes ?? 0,
                decision
            };
        }
    }
}
