using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using System.Linq;
using System.Collections.Generic;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Properties;
using System.ComponentModel;

namespace StrmAssistant.Options
{
    public class ExperienceEnhanceOptions : EditableOptionsBase
    {
        [DisplayNameL("ExperienceEnhanceOptions_EditorTitle_Experience_Enhance", typeof(Resources))]
        public override string EditorTitle => Resources.ExperienceEnhanceOptions_EditorTitle_Experience_Enhance;
        
        [DisplayName("自动合并多版本")]
        [Description("同一部作品的多个文件合并成一个条目。")]
        [Required]
        public bool MergeMultiVersion { get; set; } = false;

        public enum MergeMoviesScopeOption
        {
            [DescriptionL("MergeScopeOption_LibraryScope_LibraryScope", typeof(Resources))]
            LibraryScope,
            [DescriptionL("MergeScopeOption_GlobalScope_GlobalScope", typeof(Resources))]
            GlobalScope
        }
        
        [DisplayName("电影合并范围")]
        [VisibleCondition(nameof(MergeMultiVersion), SimpleCondition.IsTrue)]
        public MergeMoviesScopeOption MergeMoviesPreference { get; set; } = MergeMoviesScopeOption.LibraryScope;

        public enum MergeSeriesScopeOption
        {
            [DescriptionL("MergeScopeOption_LibraryScope_LibraryScope", typeof(Resources))]
            LibraryScope,
            [DescriptionL("MergeScopeOption_GlobalScope_GlobalScope", typeof(Resources))]
            GlobalScope
        }

        [DisplayName("剧集合并范围")]
        [VisibleCondition(nameof(MergeMultiVersion), SimpleCondition.IsTrue)]
        public MergeSeriesScopeOption MergeSeriesPreference { get; set; } = MergeSeriesScopeOption.LibraryScope;

        [DisplayName("中文搜索增强")]
        [Description("支持拼音、首字母、简繁互搜和模糊匹配。")]
        public bool EnhanceChineseSearch { get; set; } = false;

        public enum SearchIndexOption { Full, Lite }

        public enum SearchItemType
        {
            Movie, Collection, Series, Season, Episode, Person, LiveTv, Music, MusicAlbum,
            MusicArtist, Playlist, Video, Photo, PhotoAlbum, Genre, Book, Game, Trailer, Tag, Studio
        }

        public enum SearchTuningOption
        {
            ExcludeOriginalTitle, TraditionalToSimplifiedChinese, MixedChineseVariants,
            SuppressSearchSuggestions, ShowMediaInfo
        }

        [Browsable(false)]
        public List<EditorRadioOption> SearchIndexOptionList
        {
            get { Common.OptionLabels.RepairList(_searchIndexOptionList); return _searchIndexOptionList; }
            set { _searchIndexOptionList = value; }
        }
        private List<EditorRadioOption> _searchIndexOptionList = BuildSearchIndexOptionList();

        [Browsable(false)]
        public List<EditorSelectOption> SearchModeList
        {
            get => new List<EditorSelectOption>
            {
                new EditorSelectOption { Value = SearchIndexOption.Full.ToString(), Name = "完整：结果后面追加拼音 / 模糊匹配", IsEnabled = true },
                new EditorSelectOption { Value = SearchIndexOption.Lite.ToString(), Name = "轻量：只在搜不到时用拼音 / 模糊兜底", IsEnabled = true }
            };
            set { }
        }

        [DisplayName("搜索模式")]
        [SelectItemsSource(nameof(SearchModeList))]
        [VisibleCondition(nameof(EnhanceChineseSearch), SimpleCondition.IsTrue)]
        public string EnhanceChineseSearchMode { get; set; } = SearchIndexOption.Full.ToString();

        [Browsable(false)]
        public List<EditorSelectOption> SearchItemTypeList
        {
            get { Common.OptionLabels.RepairList(_searchItemTypeList); return _searchItemTypeList; }
            set { _searchItemTypeList = value; }
        }
        private List<EditorSelectOption> _searchItemTypeList = BuildSearchItemTypeList();

        [DisplayName("作用于")]
        [Description("留空为全部类型。")]
        [EditMultilSelect]
        [SelectItemsSource(nameof(SearchItemTypeList))]
        [VisibleCondition(nameof(EnhanceChineseSearch), SimpleCondition.IsTrue)]
        public string SearchScope { get; set; } = string.Empty;

        [Browsable(false)]
        public List<EditorSelectOption> SearchTuningOptionList
        {
            get { Common.OptionLabels.RepairList(_searchTuningOptionList); return _searchTuningOptionList; }
            set { _searchTuningOptionList = value; }
        }
        private List<EditorSelectOption> _searchTuningOptionList = BuildSearchTuningOptionList();

        [DisplayName("微调")]
        [EditMultilSelect]
        [SelectItemsSource(nameof(SearchTuningOptionList))]
        [VisibleCondition(nameof(EnhanceChineseSearch), SimpleCondition.IsTrue)]
        public string SearchTuningPreferences { get; set; } = string.Empty;

        private static List<EditorRadioOption> BuildSearchIndexOptionList()
        {
            var list = new List<EditorRadioOption>();
            foreach (SearchIndexOption item in System.Enum.GetValues(typeof(SearchIndexOption)))
                list.Add(new EditorRadioOption { Value = item.ToString(), PrimaryText = Common.OptionLabels.Label(item.ToString()), IsEnabled = true });
            return list;
        }

        private static List<EditorSelectOption> BuildSearchItemTypeList()
        {
            var list = new List<EditorSelectOption>();
            foreach (SearchItemType item in System.Enum.GetValues(typeof(SearchItemType)))
                list.Add(new EditorSelectOption { Value = item.ToString(), Name = Common.OptionLabels.Label(item.ToString()), IsEnabled = true });
            return list;
        }

        private static List<EditorSelectOption> BuildSearchTuningOptionList()
        {
            var list = new List<EditorSelectOption>();
            foreach (SearchTuningOption item in System.Enum.GetValues(typeof(SearchTuningOption)))
                list.Add(new EditorSelectOption { Value = item.ToString(), Name = Common.OptionLabels.Label(item.ToString()), IsEnabled = true });
            return list;
        }

        /// <summary>逗号分隔的多选值里是否包含某项。</summary>
        public bool HasSearchTuning(SearchTuningOption option)
        {
            var value = SearchTuningPreferences ?? string.Empty;
            return value.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Any(x => string.Equals(x, option.ToString(), System.StringComparison.OrdinalIgnoreCase));
        }

        public enum MultiVersionDisplayOption { SystemDefault, MediaInfo, MediaInfoExtended, FileNameWithoutExtension }
        public enum MultiVersionSortOption { SystemDefault, HighToLowQuality, LowToHighQuality, PathAscending, PathDescending }

        [DisplayName("美化版本列表")]
        [Description("详情页和播放时的「版本」下拉框按画质命名、排序；不改条目标题。")]
        public bool BeautifyMultiVersion { get; set; } = false;

        [Browsable(false)]
        public List<EditorRadioOption> MultiVersionDisplayPreferenceList
        {
            get { Common.OptionLabels.RepairList(_multiVersionDisplayPreferenceList); return _multiVersionDisplayPreferenceList; }
            set { _multiVersionDisplayPreferenceList = value; }
        }
        private List<EditorRadioOption> _multiVersionDisplayPreferenceList = BuildDisplayList();

        [Browsable(false)]
        public List<EditorSelectOption> VersionNameList
        {
            get => new List<EditorSelectOption>
            {
                new EditorSelectOption { Value = MultiVersionDisplayOption.SystemDefault.ToString(), Name = "不改（Emby 默认）", IsEnabled = true },
                new EditorSelectOption { Value = MultiVersionDisplayOption.MediaInfo.ToString(), Name = "画质：4K HDR10", IsEnabled = true },
                new EditorSelectOption { Value = MultiVersionDisplayOption.MediaInfoExtended.ToString(), Name = "详细：4K HEVC HDR10 · EAC3 5.1 · 25.3GB", IsEnabled = true },
                new EditorSelectOption { Value = MultiVersionDisplayOption.FileNameWithoutExtension.ToString(), Name = "文件名", IsEnabled = true }
            };
            set { }
        }

        [DisplayName("版本名称")]
        [SelectItemsSource(nameof(VersionNameList))]
        [VisibleCondition(nameof(BeautifyMultiVersion), SimpleCondition.IsTrue)]
        public string MultiVersionDisplayPreference { get; set; } = MultiVersionDisplayOption.SystemDefault.ToString();

        [Browsable(false)]
        public List<EditorRadioOption> MultiVersionReorderPreferenceList
        {
            get { Common.OptionLabels.RepairList(_multiVersionReorderPreferenceList); return _multiVersionReorderPreferenceList; }
            set { _multiVersionReorderPreferenceList = value; }
        }
        private List<EditorRadioOption> _multiVersionReorderPreferenceList = BuildSortList();

        [Browsable(false)]
        public List<EditorSelectOption> VersionSortList
        {
            get => new List<EditorSelectOption>
            {
                new EditorSelectOption { Value = MultiVersionSortOption.SystemDefault.ToString(), Name = "不改（Emby 默认）", IsEnabled = true },
                new EditorSelectOption { Value = MultiVersionSortOption.HighToLowQuality.ToString(), Name = "画质从高到低", IsEnabled = true },
                new EditorSelectOption { Value = MultiVersionSortOption.LowToHighQuality.ToString(), Name = "画质从低到高", IsEnabled = true },
                new EditorSelectOption { Value = MultiVersionSortOption.PathAscending.ToString(), Name = "路径 A→Z", IsEnabled = true },
                new EditorSelectOption { Value = MultiVersionSortOption.PathDescending.ToString(), Name = "路径 Z→A", IsEnabled = true }
            };
            set { }
        }

        [DisplayName("版本顺序")]
        [Description("排第一的就是默认播放的版本。")]
        [SelectItemsSource(nameof(VersionSortList))]
        [VisibleCondition(nameof(BeautifyMultiVersion), SimpleCondition.IsTrue)]
        public string MultiVersionReorderPreference { get; set; } = MultiVersionSortOption.SystemDefault.ToString();

        private static List<EditorRadioOption> BuildDisplayList()
        {
            var list = new List<EditorRadioOption>();
            foreach (MultiVersionDisplayOption item in System.Enum.GetValues(typeof(MultiVersionDisplayOption)))
                list.Add(new EditorRadioOption { Value = item.ToString(), PrimaryText = Common.OptionLabels.Label(item.ToString()), IsEnabled = true });
            return list;
        }

        private static List<EditorRadioOption> BuildSortList()
        {
            var list = new List<EditorRadioOption>();
            foreach (MultiVersionSortOption item in System.Enum.GetValues(typeof(MultiVersionSortOption)))
                list.Add(new EditorRadioOption { Value = item.ToString(), PrimaryText = Common.OptionLabels.Label(item.ToString()), IsEnabled = true });
            return list;
        }


        [DisplayName("不自动创建合集")]
        [Description("刮削到「属于某系列」时不自动建合集。")]
        public bool NoBoxsetsAutoCreation { get; set; } = false;

        [DisplayName("通知 Webhook 地址")]
        [Description("任务完成等消息发到这里。MoviePilot：http://IP:3000/api/v1/webhook?token=API_TOKEN&source=Emby。首页「维护」里可发测试消息。")]
        public string NotificationWebhookUrl { get; set; } = string.Empty;

        [DisplayName("播放进度按媒体库分开")]
        [Description("Emby 默认按 TMDB 等 ID 共享进度，打开后不同媒体库各记各的。")]
        public bool LibraryScopePlayProgress { get; set; } = false;

        [DisplayName("深度删除")]
        [Description("删除 strm / 软链接时，连同它指向的本地文件一起删除；指向网址的不受影响。")]
        public bool EnableDeepDelete { get; set; } = false;

        [DisplayName("剧集通知带分集详情")]
        public bool EnhanceNotification { get; set; } = false;

        [DisplayName("strm 字幕下载优化")]
        [Description("让 Open Subtitles 能为 strm 搜索下载字幕。")]
        public bool OptimizeSubtitle { get; set; } = false;

        // 界面隐藏：没有对应的实现，显示出来只会让人以为能用（值仍保留在配置里）
        [Browsable(false)]
        [DisplayNameL("ExperienceEnhanceOptions_FolderVideoGroupingLimit_Folder_Video_Grouping_Limit", typeof(Resources))]
        [DescriptionL("ExperienceEnhanceOptions_FolderVideoGroupingLimit_Adjust_video_grouping_limit_per_folder__Default_is_8_", typeof(Resources))]
        public int FolderVideoGroupingLimit { get; set; } = 8;

        [DisplayName("界面")]
        public Experience.UIFunctionOptions UIFunctionOptions { get; set; } = new Experience.UIFunctionOptions();

        [DisplayName("Emby 直连")]
        public Experience.DirectLinkOptions DirectLinkOptions { get; set; } = new Experience.DirectLinkOptions();

        [DisplayName("不自动更新的插件")]
        [Description("插件名，逗号分隔，例如 MovieDb,Tvdb。")]
        public string SuppressPluginUpdates { get; set; } = string.Empty;

        public override MediaBrowser.Model.GenericEdit.IEditObjectContainer CreateEditContainer() =>
            UI.OptionGroups.Apply(base.CreateEditContainer(),
                new UI.OptionGroup("多版本", nameof(MergeMultiVersion), nameof(MergeMoviesPreference), nameof(MergeSeriesPreference),
                    nameof(BeautifyMultiVersion), nameof(MultiVersionDisplayPreference), nameof(MultiVersionReorderPreference)),
                new UI.OptionGroup("搜索", nameof(EnhanceChineseSearch), nameof(EnhanceChineseSearchMode), nameof(SearchScope),
                    nameof(SearchTuningPreferences)),
                new UI.OptionGroup(null, nameof(DirectLinkOptions)),
                new UI.OptionGroup("播放与删除", nameof(LibraryScopePlayProgress), nameof(EnableDeepDelete), nameof(NoBoxsetsAutoCreation)),
                new UI.OptionGroup("通知与字幕", nameof(NotificationWebhookUrl), nameof(EnhanceNotification), nameof(OptimizeSubtitle)),
                new UI.OptionGroup(null, nameof(UIFunctionOptions)),
                new UI.OptionGroup("其他", nameof(SuppressPluginUpdates)));
    }
}
