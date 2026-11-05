using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace StrmAssistant.EntryPoints
{
    /// <summary>
    /// 按选项同步任务触发器。
    /// 不在 GetDefaultTriggers 里读选项：那时插件可能还没初始化完，读不到配置，
    /// 会导致「开关打开了但任务永远不触发」。
    /// </summary>
    public class TaskTriggerSyncEntryPoint : IServerEntryPoint
    {
        private readonly ILogger _logger;

        public TaskTriggerSyncEntryPoint(ILogger logger)
        {
            _logger = logger;
        }

        public void Run()
        {
            SyncNow(_logger);
        }

        /// <summary>
        /// 启动时和每次保存设置时调用（以前只在启动时同步，打开开关后不重启就永远不触发）。
        ///
        /// 只增删插件自己的那一个触发器：开关打开时没有就加上，关闭时只删掉和它一模一样的那条，
        /// 用户自己加的触发器一律不动。以前按「数量不一致就整体覆盖」，
        /// 用户删掉或多加的触发器每次重启都会被改回来。
        /// 豆瓣评分 / 内存看门狗这类固定的默认触发器交给各任务的 GetDefaultTriggers（Emby 只在第一次用它）。
        /// </summary>
        public static void SyncNow(ILogger logger)
        {
            try
            {
                var taskManager = Plugin.TaskManager;
                var options = Plugin.Instance?.GetPluginOptions();
                if (taskManager == null || options == null) return;

                var desired = new Dictionary<string, (bool enabled, TaskTriggerInfo trigger)>
                {
                    ["MetadataEnhanceTask"] = (options.MetadataEnhanceOptions?.EnableScheduledMetadataEnhance == true, Daily(3)),
                    ["IntroDbMarkerTask"] = (options.IntroSkipOptions?.EnableAutomaticIntroScraping == true,
                        Interval(TimeSpan.FromHours(6)))
                };

                foreach (var worker in taskManager.ScheduledTasks)
                {
                    var key = worker.ScheduledTask?.Key;
                    if (key == null || !desired.TryGetValue(key, out var want)) continue;
                    // 其他插件（如 MediaInfoKeeper）可能有同 Key 的任务，不能动它们的触发器
                    if (worker.ScheduledTask.GetType().Assembly != typeof(TaskTriggerSyncEntryPoint).Assembly) continue;

                    var current = (worker.Triggers ?? Array.Empty<TaskTriggerInfo>()).ToList();
                    var ours = current.Where(t => Same(t, want.trigger)).ToList();

                    if (want.enabled && ours.Count == 0)
                        current.Add(want.trigger);
                    else if (!want.enabled && ours.Count > 0)
                        current.RemoveAll(t => Same(t, want.trigger));
                    else
                        continue;

                    worker.Triggers = current.ToArray();
                    worker.ReloadTriggerEvents();
                    logger?.Info($"TaskTriggerSync - {key}: {(want.enabled ? "加上" : "去掉")}插件的定时触发器，现在共 {current.Count} 个");
                }
            }
            catch (Exception e)
            {
                logger?.Info("TaskTriggerSync - failed: " + e.Message);
            }
        }

        private static bool Same(TaskTriggerInfo a, TaskTriggerInfo b) =>
            a != null && b != null && a.Type == b.Type && a.TimeOfDayTicks == b.TimeOfDayTicks &&
            a.IntervalTicks == b.IntervalTicks && a.DayOfWeek == b.DayOfWeek;

        private static TaskTriggerInfo Daily(int hour) => new TaskTriggerInfo
        {
            Type = TaskTriggerInfo.TriggerDaily,
            TimeOfDayTicks = TimeSpan.FromHours(hour).Ticks
        };

        private static TaskTriggerInfo Interval(TimeSpan span) => new TaskTriggerInfo
        {
            Type = TaskTriggerInfo.TriggerInterval,
            IntervalTicks = span.Ticks
        };

        public void Dispose()
        {
        }
    }
}
