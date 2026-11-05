using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using StrmAssistant.Common;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class DevNotifyTestService : IService
    {
        private readonly ILogger _logger;

        public DevNotifyTestService(ILogger logger) => _logger = logger;

        public object Post(DevNotifyTest request)
        {
            var options = Plugin.Instance?.GetPluginOptions();
            var url = options?.ExperienceEnhanceOptions?.NotificationWebhookUrl ?? string.Empty;
            var report = options?.GeneralOptions?.ReportTaskCompletion == true;
            var enhance = options?.ExperienceEnhanceOptions?.EnhanceNotification == true;

            TaskReporter.Report("联调测试", request.Text ?? "联调测试", _logger);

            return new { ok = true, reportTaskCompletion = report, enhanceNotification = enhance,
                         webhookConfigured = !string.IsNullOrWhiteSpace(url), webhookLength = url.Length };
        }
    }
}
