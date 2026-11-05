using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Properties;
using System.Collections.Generic;
using System.ComponentModel;

namespace StrmAssistant.Options
{
    public class IntroSkipOptions : EditableOptionsBase
    {
        [DisplayNameL("PluginOptions_IntroSkipOptions_Intro_Credits_Detection", typeof(Resources))]
        public override string EditorTitle => "片头片尾增强";
        
        [DisplayName("让 Emby 片头检测支持 strm")]
        [Description("解锁 Emby 自带的片头检测（声纹比对）对 strm 的支持。")]
        [Required]
        public bool UnlockIntroSkip { get; set; } = false;

        [DisplayName("声纹取前几分钟")]
        [Description("2–20，默认 10。")]
        [MinValue(2), MaxValue(20)]
        [Required]
        [VisibleCondition(nameof(UnlockIntroSkip), SimpleCondition.IsTrue)]
        public int IntroDetectionFingerprintMinutes { get; set; } = 10;

        [Browsable(false)]
        public IEnumerable<EditorSelectOption> MarkerEnabledLibraryList { get; set; }

        [DisplayNameL("IntroSkipOptions_MarkerEnabledLibraryScope_Library_Scope", typeof(Resources))]
        [DescriptionL("IntroSkipOptions_MarkerEnabledLibraryScope_Intro_detection_enabled_library_scope__Blank_includes_all_", typeof(Resources))]
        [EditMultilSelect]
        [SelectItemsSource(nameof(MarkerEnabledLibraryList))]
        [VisibleCondition(nameof(UnlockIntroSkip), SimpleCondition.IsTrue)]
        [Browsable(false)]
        public string MarkerEnabledLibraryScope
        {
            get => string.Empty;
            set { }
        }

        [DisplayName("播放时学习片头片尾")]
        [Description("根据用户在播放中的拖动，自动记下剧集的片头片尾位置。")]
        public bool EnableIntroSkip { get; set; } = false;

        [Browsable(false)] // 在「跳过规则」弹窗里
        public int MaxIntroDurationSeconds { get; set; } = 150;

        [Browsable(false)] // 在「跳过规则」弹窗里
        public int MaxCreditsDurationSeconds { get; set; } = 360;

        [Browsable(false)] // 在「跳过规则」弹窗里
        public int MinOpeningPlotDurationSeconds { get; set; } = 60;

        [Browsable(false)]
        public IEnumerable<EditorSelectOption> LibraryList { get; set; }

        [Browsable(false)] // 在弹窗里
        public string LibraryScope { get; set; } = string.Empty;

        [DisplayName("跳过的关键字")]
        [Description("路径包含这些词的剧集不提取声纹，逗号分隔。")]
        public string FingerprintExcludeKeywords { get; set; } = string.Empty;

        [Browsable(false)]
        public IEnumerable<EditorSelectOption> UserList { get; set; }

        [Browsable(false)] // 在弹窗里
        public string UserScope { get; set; } = string.Empty;

        [Browsable(false)] // 在弹窗里
        public string ClientScope { get; set; } = "Emby,Infuse,SenPlayer";

        public enum IntroSkipPreference
        {
            [DescriptionL("IntroSkipControl_ResetAndOverwrite_ResetAndOverwrite", typeof(Resources))]
            ResetAndOverwrite,
            [DescriptionL("IntroSkipPreference_NoDetectionButReset_NoDetectionButReset", typeof(Resources))]
            NoDetectionButReset
        }

        [Browsable(false)]
        public List<EditorSelectOption> IntroSkipPreferenceList
        {
            get { Common.OptionLabels.RepairList(_introSkipPreferenceList); return _introSkipPreferenceList; }
            set { _introSkipPreferenceList = value ?? new List<EditorSelectOption>(); }
        }
        private List<EditorSelectOption> _introSkipPreferenceList = new List<EditorSelectOption>();

        [Browsable(false)] // 在弹窗里
        public string IntroSkipPreferences { get; set; } = string.Empty;

        [DisplayName("并行计算片头")]
        [Description("同一季多集同时比对，更快但更占 CPU。")]
        public bool ParallelMarkerComputation { get; set; } = false;

        [Browsable(false)] // 在弹窗里
        public string UnifiedIntroDbProviders { get; set; } = "IntroDb,Tidb";

        [Browsable(false)] // 在弹窗里
        public string IntroDbApiKey { get; set; } = string.Empty;

        [Browsable(false)] // 在弹窗里
        public string TidbApiKey { get; set; } = string.Empty;

        [DisplayName("新剧集自动查片头库")]
        [Description("入库时从外部片头库获取片头片尾标记，可能拖慢扫描。")]
        public bool EnableAutomaticIntroScraping { get; set; } = false;

        [VisibleCondition(nameof(EnableIntroSkip), SimpleCondition.IsTrue)]
        public Emby.Web.GenericEdit.Elements.List.GenericItemList SkipRulesEntry
        {
            get => Dialogs.OptionDialogs.ConfigRow("跳过规则", "片头 ≤ " + MaxIntroDurationSeconds + " 秒，片尾 ≤ " + MaxCreditsDurationSeconds + " 秒" +
                    (string.IsNullOrWhiteSpace(ClientScope) ? "" : "，客户端 " + ClientScope),
                "introskip-rules", Emby.Web.GenericEdit.Elements.IconNames.tune);
            set { }
        }

        public Emby.Web.GenericEdit.Elements.List.GenericItemList IntroDbEntry
        {
            get => Dialogs.OptionDialogs.ConfigRow("片头库接口", (UnifiedIntroDbProviders ?? "") +
                    (string.IsNullOrWhiteSpace(IntroDbApiKey) && string.IsNullOrWhiteSpace(TidbApiKey) ? "，未填密钥" : "，已填密钥"),
                "introdb-api", Emby.Web.GenericEdit.Elements.IconNames.vpn_key);
            set { }
        }

        public override MediaBrowser.Model.GenericEdit.IEditObjectContainer CreateEditContainer() =>
            UI.OptionGroups.Apply(base.CreateEditContainer(),
                new UI.OptionGroup("片头检测", nameof(UnlockIntroSkip), nameof(IntroDetectionFingerprintMinutes),
                    nameof(ParallelMarkerComputation), nameof(FingerprintExcludeKeywords)),
                new UI.OptionGroup("片头跳过", nameof(EnableIntroSkip), nameof(SkipRulesEntry)),
                new UI.OptionGroup("外部片头库", nameof(EnableAutomaticIntroScraping), nameof(IntroDbEntry)));
    }
}
