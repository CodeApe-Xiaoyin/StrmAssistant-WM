using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using StrmAssistant.Common;
using StrmAssistant.Properties;

namespace StrmAssistant.ScheduledTask
{
    /// <summary>内存看门狗：空闲时内存超限持续 15 分钟则重启进程（需容器/服务支持自动拉起）。</summary>
    public class MemoryWatchdogTask : IScheduledTask
    {
        private readonly ILogger _logger = Plugin.Instance.Logger;

        public string Category => Resources.ResourceManager.GetString("PluginOptions_EditorTitle_Strm_Assistant",
            Resources.Culture);

        public string Key => "MemoryWatchdogTask";

        public string Name => "内存看门狗";

        public string Description => "空闲时内存超限持续 15 分钟后重启进程";

        public bool IsHidden => true;

        public bool IsEnabled => true;

        public bool IsLogged => true;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            yield return new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromMinutes(15).Ticks
            };
        }

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var options = Plugin.Instance.GetPluginOptions().DashboardOptions?.MemoryOptions;
            var limit = options?.MemoryUsageLimitMB;

            if (!limit.HasValue || limit.Value <= 0) return Task.CompletedTask;

            var usage = MemoryWatchdog.CurrentUsageMb;
            var isIdle = !HasActivePlayback();
            var decision = MemoryWatchdog.Evaluate(usage, limit, isIdle, DateTimeOffset.UtcNow);

            _logger.Info($"MemoryWatchdog - usage: {usage}MB, limit: {limit}MB, idle: {isIdle}, decision: {decision}");

            if (decision == "exceeded-restart")
            {
                if (options.RestartOnLimit)
                {
                    _logger.Info("MemoryWatchdog - restarting server process");
                    Environment.Exit(0);
                }
                else
                {
                    _logger.Info("MemoryWatchdog - 已达重启条件，但「超限重启」未开启，仅记录");
                }
            }

            return Task.CompletedTask;
        }

        private static bool HasActivePlayback()
        {
            try
            {
                var sessionManager = Plugin.SessionManager;
                if (sessionManager == null) return false;

                return sessionManager.Sessions.Any(s => s.NowPlayingItem != null);
            }
            catch
            {
                return false;
            }
        }
    }
}
