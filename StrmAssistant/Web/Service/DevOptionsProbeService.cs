using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class DevOptionsProbeService : IService
    {
        public object Get(DevOptionsProbe request)
        {
            var o = Plugin.Instance?.GetPluginOptions();
            return new
            {
                noBoxsets = o?.ExperienceEnhanceOptions?.NoBoxsetsAutoCreation,
                libraryScopePlayProgress = o?.ExperienceEnhanceOptions?.LibraryScopePlayProgress,
                webhook = !string.IsNullOrWhiteSpace(o?.ExperienceEnhanceOptions?.NotificationWebhookUrl),
                dryRun = o?.MetadataEnhanceOptions?.DryRun,
                exclusiveExtract = o?.MediaInfoExtractOptions?.ExclusiveExtract,
                reportTaskCompletion = o?.GeneralOptions?.ReportTaskCompletion,
                // 并发：设置值 / 当前生效的上限 / 此刻空闲的名额（用来验证改并发数、并行片头计算不会把全局改乱）
                masterConfigured = o?.GeneralOptions?.MaxConcurrentCount,
                masterLimit = Common.QueueManager.CurrentMasterLimit,
                masterAvailable = Common.QueueManager.MasterSemaphore?.CurrentCount,
                tier2Configured = o?.GeneralOptions?.Tier2MaxConcurrentCount,
                tier2Limit = Common.QueueManager.CurrentTier2Limit,
                tier2Available = Common.QueueManager.Tier2Semaphore?.CurrentCount
            };
        }
    }
}
