using System.ComponentModel;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements.List;
using MediaBrowser.Model.Attributes;

namespace StrmAssistant.Options
{
    /// <summary>
    /// 仪表盘里需要保存的设置（目前只有内存守护）。页面内容由 <see cref="OverviewOptions.Build"/> 每次现算，不落盘。
    /// </summary>
    public class DashboardOptions : EditableOptionsBase
    {
        /// <summary>由 Plugin 在选项构建完成后注入，返回 (是否启用缓存, 缓存根目录)。</summary>
        internal static System.Func<System.Tuple<bool, string>> CacheSettingsProvider;

        /// <summary>最近一次「通知测试」的结果（只在内存里）。</summary>
        internal static string LastNotifyResult;

        /// <summary>最近一次按钮操作的提示（显示在页面标题下，显示一次后清空）。</summary>
        internal static string LastActionResult;

        public override string EditorTitle => "仪表盘";

        [DisplayName("内存守护")]
        public MemoryOptions MemoryOptions { get; set; } = new MemoryOptions();

        // 旧版把列表内容也存进了配置文件，保留属性只为读旧配置不报错
        [Browsable(false)]
        public GenericItemList QueueItemList { get; set; } = new GenericItemList();

        [Browsable(false)]
        public GenericItemList SystemInfoItemList { get; set; } = new GenericItemList();

        /// <summary>旧接口：以前的仪表盘对象会在这里重算一遍，现在页面每次打开都现算，什么也不用做。</summary>
        internal static void RefreshIfExists()
        {
        }

        internal void Refresh()
        {
        }
    }

    public class MemoryOptions : EditableOptionsBase
    {
        public override string EditorTitle => string.Empty;

        [DisplayName("内存上限（MB）")]
        [Description("空闲时内存超过上限持续 15 分钟则重启服务器，留空表示不启用。")]
        [MinValue(500)]
        public int? MemoryUsageLimitMB { get; set; }

        [DisplayName("超限时重启")]
        [Description("内存超限且空闲满 15 分钟后重启 Emby 进程；需要容器或服务设置为自动拉起。")]
        public bool RestartOnLimit { get; set; } = false;

        [Browsable(false)]
        public int CurrentUsageMb { get; set; }
    }
}
