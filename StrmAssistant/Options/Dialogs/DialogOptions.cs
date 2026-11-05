using System.Collections.Generic;
using System.ComponentModel;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using MediaBrowser.Model.Attributes;

// 弹窗里的设置项。属性名必须与真正保存的设置（MediaInfoExtractOptions 等）里的同名属性一致，
// 打开弹窗时按名字复制进来，保存时按名字复制回去。[Browsable(false)] 的是下拉框的选项来源，只读不回写。
namespace StrmAssistant.Options.Dialogs
{
    public class MediaInfoAdvancedDialog : EditableOptionsBase
    {
        public override string EditorTitle => string.Empty;

        [Browsable(false)]
        public List<EditorSelectOption> ExclusiveControlList { get; set; }

        [DisplayName("独占提取控制")]
        [Description("开启「独占提取」后生效。")]
        [EditMultilSelect]
        [SelectItemsSource(nameof(ExclusiveControlList))]
        public string ExclusiveControlFeatures { get; set; } = string.Empty;

        [DisplayName("单个条目超时（毫秒）")]
        [Description("默认 25000。")]
        [MinValue(1000), MaxValue(600000)]
        public int MediaInfoExtractionTimeoutMs { get; set; } = 25000;

        [DisplayName("跳过的容器格式")]
        [Description("逗号分隔，例如 iso,m2ts。")]
        public string MediaInfoExcludeMediaContainers { get; set; } = string.Empty;

        [DisplayName("跳过的关键字")]
        [Description("路径包含这些词的条目不提取，逗号分隔。")]
        public string MediaInfoExcludeKeywords { get; set; } = string.Empty;

        [DisplayName("JSON 不存内嵌图片和章节")]
        [Description("减小 JSON 体积。默认开启。")]
        public bool ClearEmbeddedMetadata { get; set; } = true;

        [DisplayName("自定义 ffprobe 路径")]
        [Description("留空使用 Emby 自带。")]
        public string CustomProbePath { get; set; } = string.Empty;

        [DisplayName("自定义 ffmpeg 路径")]
        [Description("截图时使用，留空使用 Emby 自带。")]
        public string CustomEncoderPath { get; set; } = string.Empty;
    }

    public class IntroSkipRulesDialog : EditableOptionsBase
    {
        public override string EditorTitle => string.Empty;

        [DisplayName("片头最长（秒）")]
        [MinValue(10), MaxValue(600)]
        public int MaxIntroDurationSeconds { get; set; } = 150;

        [DisplayName("片尾最长（秒）")]
        [MinValue(10), MaxValue(600)]
        public int MaxCreditsDurationSeconds { get; set; } = 360;

        [DisplayName("片头前剧情至少（秒）")]
        [MinValue(30), MaxValue(120)]
        public int MinOpeningPlotDurationSeconds { get; set; } = 60;

        [Browsable(false)]
        public IEnumerable<EditorSelectOption> LibraryList { get; set; }

        [DisplayName("媒体库")]
        [Description("只列出剧集库，留空为全部。")]
        [EditMultilSelect]
        [SelectItemsSource(nameof(LibraryList))]
        public string LibraryScope { get; set; } = string.Empty;

        [Browsable(false)]
        public IEnumerable<EditorSelectOption> UserList { get; set; }

        [DisplayName("用户")]
        [Description("留空为全部用户。")]
        [EditMultilSelect]
        [SelectItemsSource(nameof(UserList))]
        public string UserScope { get; set; } = string.Empty;

        [DisplayName("客户端")]
        [Description("逗号分隔，默认 Emby,Infuse,SenPlayer。")]
        public string ClientScope { get; set; } = "Emby,Infuse,SenPlayer";

        [Browsable(false)]
        public List<EditorSelectOption> IntroSkipPreferenceList { get; set; }

        [DisplayName("标记处理")]
        [EditMultilSelect]
        [SelectItemsSource(nameof(IntroSkipPreferenceList))]
        public string IntroSkipPreferences { get; set; } = string.Empty;
    }

    public class IntroDbApiDialog : EditableOptionsBase
    {
        public override string EditorTitle => string.Empty;

        [DisplayName("片头库顺序")]
        [Description("按顺序尝试，逗号分隔。默认 IntroDb,Tidb。")]
        public string UnifiedIntroDbProviders { get; set; } = "IntroDb,Tidb";

        [DisplayName("IntroDb API 密钥")]
        [Description("可选，提高每日调用额度。")]
        public string IntroDbApiKey { get; set; } = string.Empty;

        [DisplayName("TIDB API 密钥")]
        [Description("可选，提高每日调用额度。")]
        public string TidbApiKey { get; set; } = string.Empty;
    }

    public class EpisodeRefreshDialog : EditableOptionsBase
    {
        public override string EditorTitle => string.Empty;

        [Browsable(false)]
        public List<EditorSelectOption> EpisodeRefreshOptionList { get; set; }

        [DisplayName("刷新哪些分集")]
        [Description("默认：缺简介、缺图片。")]
        [EditMultilSelect]
        [SelectItemsSource(nameof(EpisodeRefreshOptionList))]
        public string EpisodeRefreshScope { get; set; } = string.Empty;

        [DisplayName("只看最近几天入库的（天）")]
        [Description("默认 365。")]
        [MinValue(1), MaxValue(3650)]
        public int EpisodeRefreshLookbackDays { get; set; } = 365;

        [Browsable(false)]
        public IEnumerable<EditorSelectOption> EpisodeRefreshLibraryList { get; set; }

        [DisplayName("媒体库")]
        [Description("只列出剧集库，留空为全部。")]
        [EditMultilSelect]
        [SelectItemsSource(nameof(EpisodeRefreshLibraryList))]
        public string EpisodeRefreshLibraryScope { get; set; } = string.Empty;
    }

    public class MetadataEnhanceTaskDialog : EditableOptionsBase
    {
        public override string EditorTitle => string.Empty;

        [DisplayName("每天 03:00 自动运行")]
        public bool EnableScheduledMetadataEnhance { get; set; }

        [DisplayName("每次全部重新处理")]
        [Description("关闭时只处理上次之后有变化的条目。")]
        public bool FullScanOnEveryRun { get; set; }

        [DisplayName("演员刷新范围")]
        public MetadataEnhanceOptions.RefreshPersonPreference RefreshPersonPreferences { get; set; }

        [DisplayName("演练模式")]
        [Description("只在日志里记录会改什么，不写入媒体库。")]
        public bool DryRun { get; set; }
    }

    public class DoubanRatingDialog : EditableOptionsBase
    {
        public override string EditorTitle => string.Empty;

        [DisplayName("回溯天数")]
        [Description("按首播日期，只更新这么多天内的条目。默认 365。")]
        [MinValue(1), MaxValue(36500)]
        public int RatingUpdateLookbackDays { get; set; } = 365;
    }

    public class UpdatePluginDialog : EditableOptionsBase
    {
        public override string EditorTitle => string.Empty;

        [DisplayName("下载加速前缀")]
        [Description("GitHub 下载慢时填，例如 https://ghfast.top ；已配置代理一般不用填。")]
        public string GitHubProxy { get; set; } = string.Empty;

        [DisplayName("GitHub 令牌")]
        [Description("可选。仓库是公开的，不填也能更新；只有频繁检查被 GitHub 限流时才需要。")]
        public string GitHubToken { get; set; } = string.Empty;
    }

    public class MemoryGuardDialog : EditableOptionsBase
    {
        public override string EditorTitle => string.Empty;

        [DisplayName("内存上限（MB）")]
        [Description("留空不限制。")]
        [MinValue(500)]
        public int? MemoryUsageLimitMB { get; set; }

        [DisplayName("超限时重启 Emby")]
        [Description("空闲且超限满 15 分钟后重启；容器需设置为自动重启。")]
        public bool RestartOnLimit { get; set; }
    }
}
