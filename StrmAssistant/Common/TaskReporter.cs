using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Logging;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 任务完成后在 Emby 活动日志里留一条记录（对应 PRO 的任务完成通知，此处用活动日志实现，
    /// 不依赖外部通知渠道，装完就能在仪表盘看到）。
    /// </summary>
    public static class TaskReporter
    {
        /// <summary>
        /// 通知 Webhook：按 Emby Webhooks 插件的格式发（multipart 表单里的 data 字段放 JSON），
        /// 这样 MoviePilot 的 /api/v1/webhook?token=…&amp;source=Emby 能直接解析，
        /// 再由它的「媒体服务器通知」插件转发到 Telegram 等渠道。
        ///
        /// 事件用 system.notificationtest：MoviePilot 只转发它认识的事件（入库 / 播放 / 登录 / 测试），
        /// 以前发的 system.update 会被直接丢弃，而且以前发的是裸 JSON，MoviePilot 只读表单，根本解析不到。
        /// 在 Telegram 里显示为「测试」，正文「用户：神医助手」「剧情：任务名：摘要」「时间」。
        /// </summary>
        internal static string BuildWebhookData(string taskName, string summary)
        {
            var text = (string.IsNullOrWhiteSpace(taskName) ? "" : taskName + "：") + (summary ?? string.Empty);
            var data = new System.Collections.Generic.Dictionary<string, object>
            {
                ["Event"] = "system.notificationtest",
                ["Title"] = "神医助手 · " + taskName,
                ["Description"] = text,
                ["Date"] = System.DateTime.UtcNow.ToString("o"),
                ["User"] = new System.Collections.Generic.Dictionary<string, object> { ["Name"] = "神医助手" },
                ["Server"] = new System.Collections.Generic.Dictionary<string, object> { ["Name"] = "Emby" },
                // MoviePilot 从 Item.Overview 取「剧情」那一行；item_isvirtual 让它不要把这条当成电影去找海报
                ["Item"] = new System.Collections.Generic.Dictionary<string, object> { ["Name"] = "神医助手", ["Overview"] = text },
                ["item_isvirtual"] = true,
                ["item_type"] = "NOTICE",
                ["item_name"] = "神医助手"
            };
            return System.Text.Json.JsonSerializer.Serialize(data,
                new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        /// <summary>发送一条通知到 Webhook，返回 HTTP 状态码（失败返回 -1）。</summary>
        internal static int PostWebhook(string url, string taskName, string summary, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(url)) return -1;

            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromSeconds(15) };
                using var form = new System.Net.Http.MultipartFormDataContent
                {
                    { new System.Net.Http.StringContent(BuildWebhookData(taskName, summary), System.Text.Encoding.UTF8), "data" }
                };
                var resp = http.PostAsync(url, form).GetAwaiter().GetResult();
                logger?.Info("TaskReporter - webhook " + (int)resp.StatusCode + " -> " + taskName);
                return (int)resp.StatusCode;
            }
            catch (System.Exception e)
            {
                logger?.Info("TaskReporter - webhook 失败: " + e.Message);
                return -1;
            }
        }

        private static void PostWebhook(string taskName, string summary, ILogger logger) =>
            PostWebhook(Plugin.Instance?.GetPluginOptions()?.ExperienceEnhanceOptions?.NotificationWebhookUrl,
                taskName, summary, logger);

        public static void Report(string taskName, string summary, ILogger logger = null)
        {
            var options = Plugin.Instance?.GetPluginOptions()?.GeneralOptions;
            if (options == null || !options.ReportTaskCompletion) return;

            try
            {
                // 「通知系统增强」：同时走 Emby 通知渠道
                if (Plugin.Instance?.GetPluginOptions()?.ExperienceEnhanceOptions?.EnhanceNotification == true)
                {
                    try
                    {
                        var manager = Plugin.NotificationManager;
                        if (manager == null)
                        {
                            logger?.Info("TaskReporter - notification manager unavailable");
                        }
                        else
                        {
                            var request = new Emby.Notifications.NotificationRequest
                            {
                                Title = "Strm Assistant · " + taskName,
                                Description = summary,
                                Date = System.DateTimeOffset.Now
                            };
                            EmbyCompat.SetIfExists(request, "Severity", "Info"); // 4.8 没有这个属性
                            manager.SendNotification(request);
                            logger?.Info("TaskReporter - notification dispatched: " + taskName);
                            PostWebhook(taskName, summary, logger);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        logger?.Info("TaskReporter - notification failed: " + ex.Message);
                    }
                }

                var activityManager = Plugin.ActivityManager;
                if (activityManager == null) return;

                activityManager.Create(new ActivityLogEntry
                {
                    Name = "Strm Assistant · " + taskName,
                    Overview = summary,
                    ShortOverview = summary,
                    Type = "TaskCompleted",
                    Severity = LogSeverity.Info
                });
            }
            catch (System.Exception e)
            {
                logger?.Info("TaskReporter - failed: " + e.Message);
            }
        }
    }
}
