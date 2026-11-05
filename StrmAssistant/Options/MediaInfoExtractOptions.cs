using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using MediaBrowser.Model.MediaInfo;
using StrmAssistant.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;

namespace StrmAssistant.Options
{
    public class MediaInfoExtractOptions : EditableOptionsBase
    {
        [DisplayNameL("PluginOptions_EditorTitle_Strm_Extract", typeof(Resources))]
        public override string EditorTitle => Resources.PluginOptions_EditorTitle_Strm_Extract;

        [DisplayName("包含附加内容")]
        [Description("预告片、花絮等也提取。")]
        [Required]
        public bool IncludeExtra { get; set; } = true;

        [DisplayName("没有封面时截图")]
        [Required]
        public bool EnableImageCapture { get; set; } = false;

        [DisplayName("截图位置（%）")]
        [Description("按片长百分比，默认 10。")]
        [Required, MinValue(10), MaxValue(90)]
        [VisibleCondition(nameof(EnableImageCapture), SimpleCondition.IsTrue)]
        public int ImageCapturePosition { get; set; } = 10;

        [Browsable(false)]
        [Required]
        public string ImageCaptureExcludeMediaContainers { get; set; } =
            string.Join(",", new[] { MediaContainers.MpegTs, MediaContainers.Ts, MediaContainers.M2Ts });

        public enum PersistMediaInfoOption
        {
            None,
            Default,
            Restore
        }

        /// <summary>旧版的单选列表（每次打开页面由 Plugin 填）。现在用下面的下拉框，保留只为读旧配置。</summary>
        [Browsable(false)]
        public List<EditorRadioOption> PersistMediaInfoOptionList { get; set; } = new List<EditorRadioOption>();

        [Browsable(false)]
        public List<EditorSelectOption> PersistMediaInfoModeList
        {
            get => new List<EditorSelectOption>
            {
                new EditorSelectOption { Value = PersistMediaInfoOption.Default.ToString(), Name = "提取后写入 JSON，下次优先从 JSON 恢复（推荐）", IsEnabled = true },
                new EditorSelectOption { Value = PersistMediaInfoOption.Restore.ToString(), Name = "只从 JSON 恢复，不再提取", IsEnabled = true },
                new EditorSelectOption { Value = PersistMediaInfoOption.None.ToString(), Name = "不使用 JSON", IsEnabled = true }
            };
            set { }
        }

        [DisplayName("媒体信息 JSON")]
        [Description("把提取到的媒体信息存成 JSON，刷新或重建媒体库后直接恢复，不用再探测网盘文件。")]
        [SelectItemsSource(nameof(PersistMediaInfoModeList))]
        public string PersistMediaInfoMode { get; set; } = PersistMediaInfoOption.Default.ToString();

        [DisplayName("JSON 存放目录")]
        [Description("默认 Emby 数据目录下的 strm-metadata，一般不用改；清空即恢复默认。")]
        [EditFolderPicker]
        [VisibleCondition(nameof(PersistMediaInfoMode), ValueCondition.IsNotEqual, PersistMediaInfoOption.None)]
        public string MediaInfoJsonRootFolder { get; set; } = string.Empty; // 留空：启动时按 Emby 数据目录填上（非 Docker 不是 /config）

        [Browsable(false)] // 在「高级设置」弹窗里
        public bool ClearEmbeddedMetadata { get; set; } = true;

        // 界面隐藏：没有对应的实现，显示出来只会让人以为能用（值仍保留在配置里）
        [Browsable(false)]
        [DisplayNameL("MediaInfoExtractOptions_ImageExtractionTimeoutMultiplier_Image_Timeout_Multiplier", typeof(Resources))]
        [DescriptionL("MediaInfoExtractOptions_ImageExtractionTimeoutMultiplier_Multiplier_applied_to_the_extraction_timeout_when_capturing_images__Default_is_2_", typeof(Resources))]
        [MinValue(1), MaxValue(10)]
        public double ImageExtractionTimeoutMultiplier { get; set; } = 2;

        [Browsable(false)] // 在「高级设置」弹窗里
        public string CustomProbePath { get; set; } = string.Empty;

        [Browsable(false)] // 在「高级设置」弹窗里
        public string CustomEncoderPath { get; set; } = string.Empty;

        /// <summary>
        /// 固定选项：每次都返回内置列表，忽略配置文件 / UI 回传的内容。
        /// 以前是普通自动属性：存盘后会被 UI 回传的"只有 Value"的列表覆盖（显示成英文 + undefined），
        /// 老配置里存成空数组时整个单选组一个选项都没有。
        /// </summary>
        [Browsable(false)]
        public List<EditorSelectOption> ImageCapturePostProcessModeList
        {
            get => new List<EditorSelectOption>
            {
                new EditorSelectOption { Value = string.Empty, Name = "不处理", IsEnabled = true },
                new EditorSelectOption { Value = "EnsureAspect16By9", Name = "裁成 16:9", IsEnabled = true },
                new EditorSelectOption { Value = "GenerateBackdrop", Name = "同时生成背景图", IsEnabled = true },
                new EditorSelectOption { Value = "EnsureAspect2By3", Name = "裁成 2:3 海报", IsEnabled = true }
            };
            set { }
        }

        [DisplayName("截图处理")]
        [SelectItemsSource(nameof(ImageCapturePostProcessModeList))]
        [VisibleCondition(nameof(EnableImageCapture), SimpleCondition.IsTrue)]
        public string ImageCapturePostProcessMode { get; set; } = string.Empty;

        [Browsable(false)]
        [Required]
        public string VideoThumbnailExcludeMediaContainers { get; set; } = string.Empty;

        [Browsable(false)]
        public string VideoThumbnailExcludeKeywords { get; set; } = string.Empty;

        [Browsable(false)]
        public IEnumerable<EditorSelectOption> LibraryList { get; set; }

        [DisplayName("媒体库")]
        [Description("留空为全部。")]
        [EditMultilSelect]
        [SelectItemsSource(nameof(LibraryList))]
        public string LibraryScope { get; set; } = string.Empty;

        [DisplayName("独占提取")]
        [Description("由插件自己探测 strm 目标，不依赖 Emby 刷新；Emby 探测远端文件失败时打开。")]
        public bool ExclusiveExtract { get; set; } = false;

        [DisplayName("播放时预加载下一集")]
        [Description("播放剧集时在后台为下一集提取媒体信息。")]
        public bool EnableMediaInfoPreload { get; set; } = false;

        [DisplayName("打开详情页时提取")]
        [Description("条目还没有媒体信息时，打开详情页就在后台提取。")]
        public bool ExtractOnBrowse { get; set; } = false;

        public enum ExclusiveControl
        {
            IgnoreFileChange,
            ExtractOnFileChange,
            CatchAllAllow,
            CatchAllAllowChapterImage,
            CatchAllBlock,
            IgnoreExtSubChange,
            NoPersistIntegration,
            NoNfoSaverOptimization,
            ExtractAlternative
        }

        [Browsable(false)]
        public List<EditorSelectOption> ExclusiveControlList
        {
            get
            {
                Common.OptionLabels.RepairList(_exclusiveControlList);
                // 配置文件里存过旧的完整列表，加载后会覆盖默认值，所以这里也要去掉预留项
                _exclusiveControlList?.RemoveAll(o => System.Enum.TryParse<ExclusiveControl>(o?.Value, out var c) && Reserved.Contains(c));
                return _exclusiveControlList;
            }
            set { _exclusiveControlList = value; }
        }
        private List<EditorSelectOption> _exclusiveControlList = BuildExclusiveControlList();

        private static readonly HashSet<ExclusiveControl> Reserved = new HashSet<ExclusiveControl>
        {
            ExclusiveControl.CatchAllAllowChapterImage, ExclusiveControl.NoPersistIntegration,
            ExclusiveControl.NoNfoSaverOptimization, ExclusiveControl.ExtractAlternative
        };

        private static List<EditorSelectOption> BuildExclusiveControlList()
        {
            var list = new List<EditorSelectOption>();
            foreach (ExclusiveControl item in System.Enum.GetValues(typeof(ExclusiveControl)))
            {
                // 预留项没有实现，不放进可选列表（枚举保留，旧配置里勾过也不会报错）
                if (Reserved.Contains(item)) continue;
                list.Add(new EditorSelectOption { Value = item.ToString(), Name = Common.OptionLabels.Label(item.ToString()), IsEnabled = true });
            }
            return list;
        }

        [Browsable(false)] // 在「高级设置」弹窗里
        public string ExclusiveControlFeatures { get; set; } = string.Empty;

        /// <summary>判断某个独占式控制项是否被勾选。</summary>
        public bool HasControl(ExclusiveControl control)
        {
            var value = ExclusiveControlFeatures ?? string.Empty;
            return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Any(x => string.Equals(x, control.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        [Browsable(false)] // 在「高级设置」弹窗里
        public int MediaInfoExtractionTimeoutMs { get; set; } = 25000;

        [Browsable(false)] // 在「高级设置」弹窗里
        public string MediaInfoExcludeMediaContainers { get; set; } = string.Empty;

        [Browsable(false)] // 在「高级设置」弹窗里
        public string MediaInfoExcludeKeywords { get; set; } = string.Empty;

        /// <summary>「高级设置」一行，点「配置」打开弹窗（超时、跳过规则、ffprobe/ffmpeg 路径等）。</summary>
        public Emby.Web.GenericEdit.Elements.List.GenericItemList AdvancedEntry
        {
            get => Dialogs.OptionDialogs.ConfigRow("高级设置", AdvancedSummary(), "mediainfo-advanced",
                Emby.Web.GenericEdit.Elements.IconNames.tune);
            set { }
        }

        private string AdvancedSummary()
        {
            var parts = new List<string> { "超时 " + MediaInfoExtractionTimeoutMs / 1000 + " 秒" };
            if (!string.IsNullOrWhiteSpace(MediaInfoExcludeMediaContainers)) parts.Add("跳过 " + MediaInfoExcludeMediaContainers);
            if (!string.IsNullOrWhiteSpace(MediaInfoExcludeKeywords)) parts.Add("跳过关键字 " + MediaInfoExcludeKeywords);
            if (!string.IsNullOrWhiteSpace(CustomProbePath) || !string.IsNullOrWhiteSpace(CustomEncoderPath)) parts.Add("自定义 ffmpeg");
            return string.Join("，", parts);
        }

        public override MediaBrowser.Model.GenericEdit.IEditObjectContainer CreateEditContainer() =>
            UI.OptionGroups.Apply(base.CreateEditContainer(),
                new UI.OptionGroup("提取范围", nameof(LibraryScope), nameof(IncludeExtra)),
                new UI.OptionGroup("自动提取", nameof(EnableMediaInfoPreload), nameof(ExtractOnBrowse)),
                new UI.OptionGroup("JSON 持久化", nameof(PersistMediaInfoMode), nameof(MediaInfoJsonRootFolder)),
                new UI.OptionGroup("截图", nameof(EnableImageCapture), nameof(ImageCapturePosition), nameof(ImageCapturePostProcessMode)),
                new UI.OptionGroup("高级", nameof(ExclusiveExtract), nameof(AdvancedEntry)));
    }
}
