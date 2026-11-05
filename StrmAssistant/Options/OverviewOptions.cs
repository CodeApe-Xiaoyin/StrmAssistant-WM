using System;
using System.Diagnostics;
using System.Linq;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.List;
using MediaBrowser.Model.GenericEdit;
using MediaBrowser.Model.Tasks;
using StrmAssistant.Options.Dialogs;
using StrmAssistant.Options.UI;

namespace StrmAssistant.Options
{
    /// <summary>
    /// 插件主页（第一个标签）：运行状态、计划任务、维护工具、关于。
    /// 每次打开页面现算，不落盘；需要保存的设置（内存守护、任务参数）都在「配置」弹窗里。
    /// </summary>
    public class OverviewOptions : EditableOptionsBase
    {
        public override string EditorTitle => "神医助手";

        public override string EditorDescription => _notice ?? string.Empty;

        private string _notice;

        public GenericItemList StatusList { get; set; } = new GenericItemList();

        public GenericItemList TaskList { get; set; } = new GenericItemList();

        public GenericItemList ToolList { get; set; } = new GenericItemList();

        public GenericItemList AboutList { get; set; } = new GenericItemList();

        public override IEditObjectContainer CreateEditContainer() =>
            OptionGroups.Apply(base.CreateEditContainer(),
                new OptionGroup("运行状态", nameof(StatusList)),
                new OptionGroup("计划任务", nameof(TaskList)) { Description = "其余任务在 Emby「计划任务」页面。" },
                new OptionGroup("维护", nameof(ToolList)),
                new OptionGroup("关于", nameof(AboutList)));

        /// <summary>按当前状态生成主页内容。</summary>
        public static OverviewOptions Build()
        {
            var page = new OverviewOptions();
            page._notice = DashboardOptions.LastActionResult;
            DashboardOptions.LastActionResult = null;

            Safe(page.StatusList, "运行状态", () => FillStatus(page.StatusList));
            Safe(page.TaskList, "计划任务", () => FillTasks(page.TaskList));
            Safe(page.ToolList, "维护", () => FillTools(page.ToolList));
            Safe(page.AboutList, "关于", () => FillAbout(page.AboutList));
            return page;
        }

        private static void Safe(GenericItemList list, string what, Action fill)
        {
            try
            {
                fill();
            }
            catch (Exception e)
            {
                // 主页刷新失败绝不能让页面打不开
                list.Clear();
                list.Add(new GenericListItem
                {
                    PrimaryText = what,
                    SecondaryText = "读取失败：" + e.Message,
                    Status = ItemStatus.Warning,
                    IconMode = ItemListIconMode.NoIcons
                });
            }
        }

        private static GenericListItem Row(IconNames icon, string title, string text) => new GenericListItem
        {
            PrimaryText = title,
            SecondaryText = text,
            Icon = icon,
            IconMode = ItemListIconMode.SmallRegular
        };

        // ---------------- 运行状态 ----------------

        private static void FillStatus(GenericItemList list)
        {
            AddQueue(list, IconNames.movie_filter, Common.QueueNames.MediaInfo,
                () => Common.QueueManager.MediaInfoExtractItemQueue?.Count ?? 0);
            AddQueue(list, IconNames.sync, Common.QueueNames.EpisodeRefresh,
                () => Common.QueueManager.EpisodeRefreshItemQueue?.Count ?? 0);
            AddQueue(list, IconNames.graphic_eq, Common.QueueNames.Fingerprint,
                () => Common.QueueManager.FingerprintItemQueue?.Count ?? 0);

            var mb = Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;
            var memory = Plugin.Instance?.CurrentOptions?.DashboardOptions?.MemoryOptions;
            var limit = memory?.MemoryUsageLimitMB;
            var row = Row(IconNames.developer_board, "内存",
                mb + " MB" + (limit.HasValue
                    ? " / 上限 " + limit.Value + " MB" + (memory.RestartOnLimit ? "，超限重启" : "")
                    : "，未设上限"));
            if (limit.HasValue && mb > limit.Value) row.Status = ItemStatus.Warning;
            row.Button1 = OptionDialogs.ConfigButton("memory-guard");
            row.Button2 = new ButtonItem { Icon = IconNames.refresh, Data1 = "Refresh" }; // 只放图标：窄屏下两个带字按钮会把内容挤没
            list.Add(row);
        }

        private static void AddQueue(GenericItemList list, IconNames icon, string name, Func<int> waiting)
        {
            var queued = 0;
            try { queued = waiting(); } catch { /* 队列还没建立 */ }

            var batch = Common.QueueProgress.Get(name);
            var row = Row(icon, name, "空闲");
            if (batch != null && batch.Running)
            {
                row.SecondaryText = batch.Done + " / " + batch.Total + (queued > 0 ? "，另有 " + queued + " 条排队" : "");
                row.HasPercentage = true;
                row.PercentComplete = batch.Total > 0 ? (int)(batch.Done * 100L / batch.Total) : 0;
                row.Status = ItemStatus.InProgress;
            }
            else if (queued > 0)
            {
                row.SecondaryText = queued + " 条排队中";
                row.Status = ItemStatus.InProgress;
            }
            else if (batch != null && batch.Total > 0)
            {
                row.SecondaryText = "空闲 · 上一批 " + batch.Done + " / " + batch.Total +
                                    (batch.FinishedUtc.HasValue ? "，" + batch.FinishedUtc.Value.ToLocalTime().ToString("MM-dd HH:mm") + " 完成" : "");
            }

            list.Add(row);
        }

        // ---------------- 计划任务 ----------------

        private static readonly (string key, IconNames icon, string dialog)[] Tasks =
        {
            ("MediaInfoExtractTask", IconNames.movie_filter, null),
            ("IntroFingerprintExtractTask", IconNames.graphic_eq, null),
            ("EpisodeRefreshTask", IconNames.sync, "task-episode-refresh"),
            ("MetadataEnhanceTask", IconNames.auto_awesome, "task-metadata-enhance"),
            ("DoubanRatingTask", IconNames.star, "task-douban-rating"),
            ("RefreshPersonTask", IconNames.person, null),
            ("IntroDbMarkerTask", IconNames.fingerprint, "introdb-api"),
            ("MergeMultiVersionTask", IconNames.call_merge, null),
            ("MediaInfoPersistTask", IconNames.backup, null),
            ("UpdatePluginTask", IconNames.system_update, "task-update-plugin"),
        };

        private static void FillTasks(GenericItemList list)
        {
            var workers = Plugin.TaskManager?.ScheduledTasks?.ToList();
            if (workers == null) return;

            foreach (var (key, icon, dialog) in Tasks)
            {
                var worker = workers.FirstOrDefault(w => OptionDialogs.IsOurTask(w, key));
                if (worker == null) continue;

                var row = Row(icon, worker.Name, TaskStatusText(worker));
                if (worker.State == TaskState.Running)
                {
                    row.Status = ItemStatus.InProgress;
                    row.HasPercentage = true;
                    row.PercentComplete = (int)Math.Round(worker.CurrentProgress ?? 0);
                }
                else if (worker.LastExecutionResult?.Status == TaskCompletionStatus.Failed)
                {
                    row.Status = ItemStatus.Failed;
                }

                if (dialog != null) row.Button1 = OptionDialogs.ConfigButton(dialog);
                var run = OptionDialogs.RunButton(key);
                run.IsEnabled = worker.State != TaskState.Running;
                row.Button2 = run;
                list.Add(row);
            }
        }

        internal static string TaskStatusText(IScheduledTaskWorker worker)
        {
            if (worker.State == TaskState.Running)
                return "运行中 " + Math.Round(worker.CurrentProgress ?? 0) + "%";
            if (worker.State == TaskState.Cancelling) return "正在取消";

            var last = worker.LastExecutionResult;
            if (last == null) return "尚未运行";

            var when = last.EndTimeUtc.ToLocalTime().ToString("MM-dd HH:mm");
            var took = last.EndTimeUtc - last.StartTimeUtc;
            var duration = took.TotalMinutes >= 1 ? Math.Round(took.TotalMinutes) + " 分钟" : Math.Max(1, Math.Round(took.TotalSeconds)) + " 秒";
            switch (last.Status)
            {
                case TaskCompletionStatus.Completed:
                    return "上次 " + when + " 完成，用时 " + duration;
                case TaskCompletionStatus.Failed:
                    return "上次 " + when + " 失败" + (string.IsNullOrWhiteSpace(last.ErrorMessage) ? "" : "：" + last.ErrorMessage);
                case TaskCompletionStatus.Cancelled:
                    return "上次 " + when + " 已取消";
                default:
                    return "上次 " + when + " 中断";
            }
        }

        // ---------------- 维护 ----------------

        private static void FillTools(GenericItemList list)
        {
            var search = Row(IconNames.manage_search, "拼音 / 模糊搜索索引",
                Common.SearchIndex.Ready ? Common.SearchIndex.Count.ToString("N0") + " 个条目" : "未建立，首次搜索时自动建立");
            search.Button1 = new ButtonItem("重建")
            {
                Icon = IconNames.build,
                Data1 = "RebuildSearchIndex",
                ConfirmationPrompt = "重新读取全部条目建立拼音索引（几秒钟），确定吗？"
            };
            list.Add(search);

            var webhook = Plugin.Instance?.CurrentOptions?.ExperienceEnhanceOptions?.NotificationWebhookUrl;
            var notify = Row(IconNames.notifications_active, "通知测试",
                DashboardOptions.LastNotifyResult ??
                (string.IsNullOrWhiteSpace(webhook) ? "未填写 Webhook 地址（体验增强 → 通知）" : "发一条测试消息到 Webhook"));
            notify.Button1 = new ButtonItem("发送")
            {
                Icon = IconNames.send,
                Data1 = "SendTestNotification",
                IsEnabled = !string.IsNullOrWhiteSpace(webhook)
            };
            list.Add(notify);

            var cache = Row(IconNames.folder, "本地元数据缓存", "未启用");
            var settings = DashboardOptions.CacheSettingsProvider?.Invoke();
            if (settings != null && settings.Item1 && !string.IsNullOrWhiteSpace(settings.Item2))
            {
                if (!System.IO.Directory.Exists(settings.Item2))
                {
                    cache.SecondaryText = "目录不存在：" + settings.Item2;
                    cache.Status = ItemStatus.Warning;
                }
                else
                {
                    var files = System.IO.Directory.GetFiles(settings.Item2, "*", System.IO.SearchOption.AllDirectories);
                    var bytes = files.Sum(f => { try { return new System.IO.FileInfo(f).Length; } catch { return 0L; } });
                    cache.SecondaryText = files.Length.ToString("N0") + " 个文件，" + FormatSize(bytes);
                }

                cache.Button1 = new ButtonItem("清空")
                {
                    Icon = IconNames.delete_sweep,
                    Data1 = "ClearMetadataCache",
                    ConfirmationPrompt = "清空本地元数据缓存？（不影响媒体库）"
                };
            }
            list.Add(cache);

            var stats = Common.TmdbCache.GetStats();
            var type = stats.GetType();
            int Read(string name) => Convert.ToInt32(type.GetProperty(name)?.GetValue(stats) ?? 0);
            var entries = Read("entries");
            var hits = Read("hits");
            var misses = Read("misses");
            var tmdb = Row(IconNames.storage, "TMDB 响应缓存",
                entries.ToString("N0") + " 条" + (hits + misses > 0 ? "，命中率 " + hits * 100 / (hits + misses) + "%" : ""));
            tmdb.Button1 = new ButtonItem("清空")
            {
                Icon = IconNames.delete_sweep,
                Data1 = "ClearTmdbCache",
                ConfirmationPrompt = "清空 TMDB 响应缓存？"
            };
            list.Add(tmdb);
        }

        private static string FormatSize(long bytes) =>
            bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("F1") + " GB"
            : bytes >= 1L << 20 ? (bytes / (double)(1L << 20)).ToString("F1") + " MB"
            : (bytes / 1024d).ToString("F0") + " KB";

        // ---------------- 关于 ----------------

        private static void FillAbout(GenericItemList list)
        {
            list.Add(Row(IconNames.info, "版本", (Plugin.GetVersionHash() ?? "").Replace("+n/a", "")));
            var repo = Row(IconNames.code, "项目主页（上游 StrmAssistant）", "github.com/sjtuross/StrmAssistant");
            repo.HyperLink = "https://github.com/sjtuross/StrmAssistant";
            list.Add(repo);
        }
    }
}
