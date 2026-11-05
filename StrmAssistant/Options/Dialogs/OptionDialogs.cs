using System;
using System.Collections.Generic;
using System.Linq;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.List;
using MediaBrowser.Model.Plugins.UI.Views;
using MediaBrowser.Model.Plugins.UI.Views.Enums;
using MediaBrowser.Model.Tasks;
using StrmAssistant.Options.UI;

namespace StrmAssistant.Options.Dialogs
{
    /// <summary>
    /// 「配置」弹窗与「执行」按钮的登记处。
    ///   · 弹窗：页面上只留一行 + 「配置」按钮，细项放进弹窗；弹窗里的属性与真正保存的设置同名，打开时复制进来，保存时复制回去。
    ///     对应的设置属性在原页面上标成 [Browsable(false)]，页面保存时不会被旧值覆盖（见 Plugin.MergeInto）。
    ///   · 执行：按计划任务的 Key 立即排队运行。
    /// </summary>
    public static class OptionDialogs
    {
        public const string DialogPrefix = "dlg:";
        public const string TaskPrefix = "task:";

        private sealed class Spec
        {
            public string Title;
            public Type Type;
            public Func<PluginOptions, object> Target;
            public DialogSize Size = DialogSize.Medium;
        }

        private static readonly Dictionary<string, Spec> Specs = new Dictionary<string, Spec>(StringComparer.OrdinalIgnoreCase)
        {
            ["mediainfo-advanced"] = new Spec { Title = "提取高级设置", Type = typeof(MediaInfoAdvancedDialog), Target = o => o.MediaInfoExtractOptions, Size = DialogSize.MediumTall },
            ["introskip-rules"] = new Spec { Title = "片头跳过规则", Type = typeof(IntroSkipRulesDialog), Target = o => o.IntroSkipOptions, Size = DialogSize.MediumTall },
            ["introdb-api"] = new Spec { Title = "片头库接口", Type = typeof(IntroDbApiDialog), Target = o => o.IntroSkipOptions, Size = DialogSize.Small },
            ["task-episode-refresh"] = new Spec { Title = "刷新剧集元数据", Type = typeof(EpisodeRefreshDialog), Target = o => o.MetadataEnhanceOptions, Size = DialogSize.Small },
            ["task-metadata-enhance"] = new Spec { Title = "元数据增强", Type = typeof(MetadataEnhanceTaskDialog), Target = o => o.MetadataEnhanceOptions, Size = DialogSize.Small },
            ["task-douban-rating"] = new Spec { Title = "豆瓣评分更新", Type = typeof(DoubanRatingDialog), Target = o => o.MetadataEnhanceOptions, Size = DialogSize.Small },
            ["task-update-plugin"] = new Spec { Title = "更新插件", Type = typeof(UpdatePluginDialog), Target = o => o.AboutOptions, Size = DialogSize.Small },
            ["memory-guard"] = new Spec { Title = "内存守护", Type = typeof(MemoryGuardDialog), Target = o => o.DashboardOptions?.MemoryOptions, Size = DialogSize.Small },
        };

        /// <summary>「配置」按钮。</summary>
        public static ButtonItem ConfigButton(string key) => new ButtonItem("配置")
        {
            Icon = IconNames.tune,
            Data1 = DialogPrefix + key
        };

        /// <summary>「执行」按钮。</summary>
        public static ButtonItem RunButton(string taskKey) => new ButtonItem("执行")
        {
            Icon = IconNames.play_arrow,
            Data1 = TaskPrefix + taskKey
        };

        /// <summary>一行「名称 ……［配置］」，用在设置页里代替一堆平铺的细项。</summary>
        public static GenericItemList ConfigRow(string title, string summary, string key, IconNames icon)
        {
            return new GenericItemList
            {
                new GenericListItem
                {
                    PrimaryText = title,
                    SecondaryText = summary,
                    Icon = icon,
                    IconMode = ItemListIconMode.SmallRegular,
                    Button1 = ConfigButton(key)
                }
            };
        }

        /// <summary>按钮带过来的命令：Emby 有时放在 commandId，有时放在 data。</summary>
        public static string PickCommand(string commandId, string data)
        {
            if (IsOurs(commandId)) return commandId;
            if (IsOurs(data)) return data;
            return null;
        }

        private static bool IsOurs(string s) =>
            !string.IsNullOrEmpty(s) &&
            (s.StartsWith(DialogPrefix, StringComparison.Ordinal) || s.StartsWith(TaskPrefix, StringComparison.Ordinal));

        public static bool IsDialog(string command) =>
            command != null && command.StartsWith(DialogPrefix, StringComparison.Ordinal);

        public static bool IsTask(string command) =>
            command != null && command.StartsWith(TaskPrefix, StringComparison.Ordinal);

        public static IPluginUIView Open(string pluginId, string command)
        {
            var key = command.Substring(DialogPrefix.Length);
            if (!Specs.TryGetValue(key, out var spec)) return null;

            var plugin = Plugin.Instance;
            var options = plugin?.GetPluginOptions();
            var target = options == null ? null : spec.Target(options);
            if (target == null) return null;

            var dialog = (EditableOptionsBase)Activator.CreateInstance(spec.Type);
            OptionCopy.CopyAll(target, dialog);

            return new OptionDialogView(pluginId, dialog, spec.Title, spec.Size, edited =>
            {
                var live = spec.Target(plugin.CurrentOptions);
                OptionCopy.CopyEditable(edited, live);
                plugin.SaveOptionsFromUi();
                plugin.Logger?.Info("OptionDialog - 已保存「" + spec.Title + "」");
            });
        }

        /// <summary>立即运行计划任务（按 Key）。返回提示文字。</summary>
        public static string RunTask(string command)
        {
            var key = command.Substring(TaskPrefix.Length);
            var worker = Plugin.TaskManager?.ScheduledTasks?.FirstOrDefault(t => t.ScheduledTask?.Key == key);
            if (worker == null) return "找不到任务 " + key;
            if (worker.State == TaskState.Running) return "「" + worker.Name + "」正在运行";

            Plugin.TaskManager.QueueScheduledTask(worker.ScheduledTask, new TaskOptions());
            Plugin.Instance?.Logger?.Info("Dashboard - 手动运行任务「" + worker.Name + "」");
            return "已开始运行「" + worker.Name + "」";
        }
    }
}
