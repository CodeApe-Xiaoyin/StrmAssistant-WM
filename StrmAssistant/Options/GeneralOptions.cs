using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Properties;
using System.Collections.Generic;
using System.ComponentModel;
using MediaBrowser.Model.GenericEdit;

namespace StrmAssistant.Options
{
    public class GeneralOptions : EditableOptionsBase
    {
        [DisplayNameL("GeneralOptions_EditorTitle_General_Options", typeof(Resources))]
        public override string EditorTitle => "常规设置";

        [DisplayName("启用追更模式")]
        [Required]
        public bool CatchupMode { get; set; } = false;

        public enum CatchupTask
        {
            [DescriptionL("CatchupTask_MediaInfo_MediaInfo", typeof(Resources))]
            MediaInfo,
            [DescriptionL("CatchupTask_Fingerprint_Fingerprint", typeof(Resources))]
            Fingerprint,
            [DescriptionL("CatchupTask_IntroSkip_IntroSkip", typeof(Resources))]
            IntroSkip,
            [DescriptionL("CatchupTask_EpisodeRefresh_EpisodeRefresh", typeof(Resources))]
            EpisodeRefresh
        }
        
        [Browsable(false)]
        public IEnumerable<EditorSelectOption> CatchupTaskList
        {
            // 列表来自配置文件（老配置里是英文名），取用时按 Value 补中文
            get { Common.OptionLabels.RepairList(_catchupTaskList); return _catchupTaskList; }
            set { _catchupTaskList = value; }
        }
        private IEnumerable<EditorSelectOption> _catchupTaskList;

        [DisplayName("追更时处理")]
        [EditMultilSelect]
        [SelectItemsSource(nameof(CatchupTaskList))]
        [VisibleCondition(nameof(CatchupMode), SimpleCondition.IsTrue)]
        public string CatchupTaskScope { get; set; } = CatchupTask.MediaInfo.ToString();

        [DisplayName("同时处理条目数")]
        [Description("提取媒体信息、片头声纹等后台任务的并发数。NAS 建议 1–2。")]
        [Required, MinValue(1), MaxValue(20)]
        public int MaxConcurrentCount { get; set; } = 1;

        [DisplayName("每条间隔（秒）")]
        [Description("单线程时每处理一条后等待，减轻网盘压力。0 为不等待。")]
        [VisibleCondition(nameof(MaxConcurrentCount), ValueCondition.IsEqual, 1)]
        [Required, MinValue(0), MaxValue(60)]
        public int CooldownDurationSeconds { get; set; } = 0;

        [DisplayName("轻量任务并发数")]
        [Description("刷新元数据、字幕等本地任务。")]
        [Required, MinValue(1), MaxValue(20)]
        public int Tier2MaxConcurrentCount { get; set; } = 1;

        [DisplayName("任务完成写入活动日志")]
        public bool ReportTaskCompletion { get; set; } = false;

        [DisplayName("追更入队最小间隔（秒）")]
        [Description("两次追更入队之间至少间隔，0 为不限制。")]
        [VisibleCondition(nameof(CatchupMode), SimpleCondition.IsTrue)]
        [MinValue(0), MaxValue(3600)]
        public int CatchupMinimumDelaySeconds { get; set; } = 0;

        [DisplayName("代理地址")]
        [Description("http://、https:// 或 socks5://，按地址自动识别协议；不写协议按 http 处理。留空直连。默认只用于插件自己的请求（刮削等）。")]
        public string ProxyUrl { get; set; } = string.Empty;

        [DisplayName("Emby 自身请求也走代理")]
        [Description("打开后 Emby 的全部出站请求都走上面的代理；重启 Emby 后完全生效。")]
        [VisibleCondition(nameof(ProxyUrl), SimpleCondition.IsNotNullOrEmpty)]
        public bool GlobalProxy { get; set; } = false;

        /// <summary>最近一次代理检测结果（GlobalProxyService 写入），界面用下面的 ProxyStatus 显示。</summary>
        [Browsable(false)]
        public string ProxyServerStatus { get; set; } = string.Empty;

        [VisibleCondition(nameof(ProxyUrl), SimpleCondition.IsNotNullOrEmpty)]
        public Emby.Web.GenericEdit.Elements.StatusItem ProxyStatus
        {
            get
            {
                var text = string.IsNullOrWhiteSpace(ProxyServerStatus) ? "保存后检测" : ProxyServerStatus;
                var status = text.Contains("不可用") || text.Contains("无法识别") ? Emby.Web.GenericEdit.Elements.ItemStatus.Failed
                    : text.Contains("检测中") ? Emby.Web.GenericEdit.Elements.ItemStatus.InProgress
                    : text.Contains("可用") ? Emby.Web.GenericEdit.Elements.ItemStatus.Succeeded
                    : Emby.Web.GenericEdit.Elements.ItemStatus.Unavailable;
                return new Emby.Web.GenericEdit.Elements.StatusItem("代理状态", text, status);
            }
            set { }
        }

        // ---- 以下不在界面上显示 ----

        public enum ProxyModeOption { Http, Socks5 }
        public enum ProxyScopeOption { Global, PluginOnly }

        /// <summary>旧版的协议单选。现在按地址前缀自动识别，只为读取旧配置保留（启动时迁移进地址）。</summary>
        [Browsable(false)]
        public string ProxyMode { get; set; } = ProxyModeOption.Http.ToString();

        /// <summary>旧版的作用范围单选。现在由「Emby 自身请求也走代理」开关决定，只为兼容旧配置保留。</summary>
        [Browsable(false)]
        public string ProxyScope { get; set; } = ProxyScopeOption.PluginOnly.ToString();

        [Browsable(false)]
        public bool EnableAutomaticDecompression { get; set; } = false;

        [Browsable(false)]
        public string LocalDiscoveryUrl { get; set; } = string.Empty;

        [Browsable(false)]
        public bool ShowProxyServerStatus { get; set; } = true;

        [Browsable(false)]
        public bool LastProxyCheckSucceeded { get; set; } = false;

        public override IEditObjectContainer CreateEditContainer() =>
            UI.OptionGroups.Apply(base.CreateEditContainer(),
                new UI.OptionGroup("后台任务", nameof(MaxConcurrentCount), nameof(CooldownDurationSeconds),
                    nameof(Tier2MaxConcurrentCount), nameof(ReportTaskCompletion)),
                new UI.OptionGroup("追更模式", nameof(CatchupMode), nameof(CatchupTaskScope),
                    nameof(CatchupMinimumDelaySeconds))
                {
                    Description = "收藏的剧集有新内容入库时，自动提取媒体信息 / 片头等（仅 strm）。"
                },
                new UI.OptionGroup("网络代理", nameof(ProxyUrl), nameof(GlobalProxy), nameof(ProxyStatus)));
    }
}
