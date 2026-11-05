using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Properties;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;

namespace StrmAssistant.Options
{
    public enum RefreshPersonOption
    {
        Default,
        FullRefresh,
        NoAdult
    }

    public class MetadataEnhanceOptions : EditableOptionsBase
    {
        [DisplayNameL("PluginOptions_MetadataEnhanceOptions_Metadata_Enhance", typeof(Resources))]
        public override string EditorTitle => Resources.PluginOptions_MetadataEnhanceOptions_Metadata_Enhance;
        

        [DisplayName("TMDB 中文优化")]
        [Description("TMDB 缺中文时按下面的备选语言补齐标题、简介。")]
        public bool ChineseMovieDb { get; set; }

        [DisplayName("TMDB 备选语言")]
        [Description("简体中文始终优先，其余按列表顺序备选。")]
        [EditMultilSelect]
        [SelectItemsSource(nameof(TmdbLanguageList))]
        [VisibleCondition(nameof(ChineseMovieDb), SimpleCondition.IsTrue)]
        public string FallbackLanguages
        {
            get => _fallbackLanguages;
            // 老配置是手填的（如 zh-sg、zh-tw）：按列表里的标准写法归一，勾选框才能对上
            set => _fallbackLanguages = NormalizeLanguageCodes(value);
        }
        private string _fallbackLanguages = string.Empty;

        private static string NormalizeLanguageCodes(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var parts = value.Split(new[] { ',', ';', '|' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0)
                .Select(x => TmdbLanguages.FirstOrDefault(l => string.Equals(l.Item1, x, System.StringComparison.OrdinalIgnoreCase))?.Item1 ?? x)
                .Distinct(System.StringComparer.OrdinalIgnoreCase);
            return string.Join(",", parts);
        }

        /// <summary>
        /// TMDB 支持的语言（TMDB 用"语言-地区"格式，例如 zh-SG）。固定列表：每次都返回内置值，不受配置文件影响。
        /// 以前这里只有一个文本框，用户得自己拼 zh-SG 这类代码。
        /// </summary>
        [Browsable(false)]
        public List<EditorSelectOption> TmdbLanguageList
        {
            get => TmdbLanguages.Select(l => new EditorSelectOption { Value = l.Item1, Name = l.Item2 + "（" + l.Item1 + "）", IsEnabled = true }).ToList();
            set { }
        }

        private static readonly System.Tuple<string, string>[] TmdbLanguages =
        {
            System.Tuple.Create("zh-CN", "简体中文（始终优先）"),
            System.Tuple.Create("zh-SG", "简体中文·新加坡"),
            System.Tuple.Create("zh-HK", "繁体中文·香港"),
            System.Tuple.Create("zh-TW", "繁体中文·台湾"),
            System.Tuple.Create("ja-JP", "日语"),
            System.Tuple.Create("ko-KR", "韩语"),
            System.Tuple.Create("en-US", "英语·美国"),
            System.Tuple.Create("en-GB", "英语·英国"),
            System.Tuple.Create("fr-FR", "法语"),
            System.Tuple.Create("de-DE", "德语"),
            System.Tuple.Create("es-ES", "西班牙语"),
            System.Tuple.Create("it-IT", "意大利语"),
            System.Tuple.Create("ru-RU", "俄语"),
            System.Tuple.Create("pt-BR", "葡萄牙语·巴西"),
            System.Tuple.Create("th-TH", "泰语"),
            System.Tuple.Create("vi-VN", "越南语"),
        };

        [DisplayName("TVDB 中文 / 日文优先")]
        public bool ChineseTvdb { get; set; }
        // 本插件没有直连 TVDB 的实现：字段保留只为配置兼容，不在界面展示（避免"有开关没行为"）
        [Browsable(false)]
        [DescriptionL("ModOptions_FallbackLanguages_Fallback_languages__Default_is_zh_SG_", typeof(Resources))]
        public string TvdbFallbackLanguages { get; set; } = string.Empty;

        [DisplayName("不用英文等其他语言的简介")]
        [Description("没有中文 / 备选语言简介时宁可留空。")]
        public bool BlockNonFallbackLanguage { get; set; }

        [DisplayName("清除宣传语")]
        public bool ClearTagline { get; set; }

        [DisplayName("简介优先繁体中文")]
        public bool PreferTraditionalChinese { get; set; }

        /// <summary>旧版「显示更多选项」开关。现在相关项跟着各自的开关显示，不再需要。</summary>
        [Browsable(false)]
        public bool ShowMetadataExtraOptions { get; set; }

        [DisplayName("TMDB 剧集组")]
        [Description("按 TMDB 剧集组重新排季和集。")]
        public bool MovieDbEpisodeGroup { get; set; }
        [DisplayName("同时更新分集名称和简介")]
        [VisibleCondition(nameof(MovieDbEpisodeGroup), SimpleCondition.IsTrue)]
        public bool EnableMovieDbEpisodeGroupExtendedInfo { get; set; }

        [DisplayName("本地剧集组文件")]
        [Description("把剧集组信息存成 / 读自剧集目录里的 JSON。")]
        public bool LocalEpisodeGroup { get; set; }

        [Browsable(false)]
        public System.Collections.Generic.IEnumerable<Emby.Web.GenericEdit.Common.EditorSelectOption>
            EpisodeRefreshLibraryList { get; set; }

        [Browsable(false)] // 仪表盘 →「刷新剧集元数据」→ 配置
        public string EpisodeRefreshLibraryScope { get; set; } = string.Empty;

        [DisplayName("本地元数据缓存")]
        [Description("刮削结果另存一份到本地，重建媒体库时直接还原。")]
        public bool MetadataCacheMode { get; set; } = false;

        [DisplayName("缓存目录")]
        [Description("默认 Emby 数据目录下的 strm-metadata-cache；清空即恢复默认。")]
        [MediaBrowser.Model.Attributes.EditFolderPicker]
        [MediaBrowser.Model.Attributes.VisibleCondition(nameof(MetadataCacheMode), MediaBrowser.Model.Attributes.SimpleCondition.IsTrue)]
        public string MetadataCacheRootFolder { get; set; } = string.Empty;

        public enum RefreshPersonPreference
        {
            [DescriptionL("RefreshPersonPreference_Default_Default", typeof(Resources))]
            Default,
            [DescriptionL("RefreshPersonPreference_All_All", typeof(Resources))]
            All
        }

        [Browsable(false)] // 仪表盘 →「元数据增强」→ 配置
        public RefreshPersonPreference RefreshPersonPreferences { get; set; } = RefreshPersonPreference.Default;

        [DisplayName("演员表增强")]
        [Description("导入各季演员并更新剧集演员表。")]
        public bool EnhanceMovieDbPerson { get; set; }

        [DisplayName("原语言海报")]
        [Description("海报按作品原语言选取。")]
        public bool PreferOriginalPoster { get; set; }

        [DisplayName("拼音排序")]
        [Description("用标题拼音首字母作为排序名。")]
        public bool PinyinSortName { get; set; }

        [DisplayName("NFO 导入演员头像")]
        public bool EnhanceNfoMetadata { get; set; }

        [DisplayName("启动时清理近期 TMDB 缓存")]
        public bool ClearRecentTmdbCacheOnStartup { get; set; }

        [DisplayName("MDBList API 密钥")]
        [Description("可选，用于 MDBList 评分，留空不用。")]
        public string MdbListApiKey { get; set; } = string.Empty;

        public enum MetadataRefreshRuleName { NoOverview, NoImage, NonChineseNameOverview, DefaultEpisodeName }
        public enum PersonRefreshRuleName
        {
            DefaultMetadataRefresh, ExcludeIfChinese, ExcludeAdultMetadata, FullMetadataRefresh,
            DefaultImageRefresh, FullImageRefresh, DownloadImagesInAdvance
        }

        [Browsable(false)]
        public List<EditorSelectOption> MetadataRefreshRuleList
        {
            get { Common.OptionLabels.RepairList(_metadataRefreshRuleList); return _metadataRefreshRuleList; }
            set { _metadataRefreshRuleList = value; }
        }
        private List<EditorSelectOption> _metadataRefreshRuleList = BuildRuleList();

        /// <summary>
        /// PRO 兼容的刷新规则，填了会覆盖 EpisodeRefreshScope，和它重复。界面不再显示；旧配置里有值时启动迁移进 EpisodeRefreshScope。
        /// </summary>
        [Browsable(false)]
        public string MetadataRefreshPreferences { get; set; } = string.Empty;

        // 界面隐藏：没有对应的实现，显示出来只会让人以为能用（值仍保留在配置里）
        [Browsable(false)]
        [DisplayNameL("MetadataEnhanceOptions_MetadataRefreshLookbackDays_Lookback_Days", typeof(Resources))]
        [DescriptionL("MetadataEnhanceOptions_MetadataRefreshLookbackDays_Only_refresh_items_created_within_this_many_days__Default_is_365_", typeof(Resources))]
        [MinValue(1), MaxValue(3650)]
        public int MetadataRefreshLookbackDays { get; set; } = 365;

        [Browsable(false)]
        public List<EditorSelectOption> MetadataRefreshLibraryList { get; set; }

        [Browsable(false)]
        public string MetadataRefreshLibraryScope { get; set; } = string.Empty;

        [Browsable(false)]
        public List<EditorSelectOption> PersonRefreshOptionList
        {
            get { Common.OptionLabels.RepairList(_personRefreshOptionList); return _personRefreshOptionList; }
            set { _personRefreshOptionList = value; }
        }
        private List<EditorSelectOption> _personRefreshOptionList = BuildPersonRefreshList();

        [Browsable(false)]
        public int DoubanClient { get; set; } = 0;

        private static List<EditorSelectOption> BuildRuleList()
        {
            var list = new List<EditorSelectOption>();
            foreach (MetadataRefreshRuleName item in System.Enum.GetValues(typeof(MetadataRefreshRuleName)))
                list.Add(new EditorSelectOption { Value = item.ToString(), Name = Common.OptionLabels.Label(item.ToString()), IsEnabled = true });
            return list;
        }

        private static List<EditorSelectOption> BuildPersonRefreshList()
        {
            var list = new List<EditorSelectOption>();
            foreach (PersonRefreshRuleName item in System.Enum.GetValues(typeof(PersonRefreshRuleName)))
                list.Add(new EditorSelectOption { Value = item.ToString(), Name = Common.OptionLabels.Label(item.ToString()), IsEnabled = true });
            return list;
        }

        /// <summary>把 PRO 的规则名映射成我们内部的刷新范围枚举。</summary>
        public static string MapRefreshRulesToScope(string rules)
        {
            if (string.IsNullOrWhiteSpace(rules)) return null;

            var parts = rules.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).ToArray();
            var mapped = parts.Select(p => p == "NonChineseNameOverview" ? "NonChineseOverview" : p);

            return string.Join(",", mapped);
        }

        [Browsable(false)]
        public List<EditorSelectOption> LanguageList
        {
            get { Common.OptionLabels.RepairList(_languageList); return _languageList; }
            set { _languageList = value; }
        }
        private List<EditorSelectOption> _languageList = BuildLanguageList();

        [Browsable(false)]
        public List<EditorSelectOption> TvdbLanguageList
        {
            get { Common.OptionLabels.RepairList(_tvdbLanguageList); return _tvdbLanguageList; }
            set { _tvdbLanguageList = value; }
        }
        private List<EditorSelectOption> _tvdbLanguageList = BuildLanguageList();

        [Browsable(false)]
        public List<EditorSelectOption> DoubanAssistScrapeOptionList { get; set; } = new List<EditorSelectOption>
        {
            new EditorSelectOption { Value = "Overview", Name = "简介", IsEnabled = true },
            new EditorSelectOption { Value = "Rating", Name = "评分", IsEnabled = true },
            new EditorSelectOption { Value = "Name", Name = "标题", IsEnabled = true }
        };

        private static List<EditorSelectOption> BuildLanguageList()
        {
            var codes = new[]
            {
                "zh-CN", "zh-TW", "zh-HK", "en", "ja", "ko", "fr", "de", "es", "it", "ru", "pt", "th", "vi"
            };

            var list = new List<EditorSelectOption>();
            foreach (var code in codes)
                list.Add(new EditorSelectOption { Value = code, Name = code, IsEnabled = true });
            return list;
        }


        // 界面隐藏：没有对应的实现，显示出来只会让人以为能用（值仍保留在配置里）
        [Browsable(false)]
        [DisplayNameL("MetadataEnhanceOptions_MetadataUpdateTrackedFields_Tracked_Fields", typeof(Resources))]
        [DescriptionL("MetadataEnhanceOptions_MetadataUpdateTrackedFields_Comma_separated_field_names_recorded_when_metadata_changes__Blank_disables_tracking_", typeof(Resources))]
        public string MetadataUpdateTrackedFields { get; set; } = string.Empty;

        [Browsable(false)] // 仪表盘 →「豆瓣评分更新」→ 配置
        public int RatingUpdateLookbackDays { get; set; } = 365;

        [VisibleCondition(nameof(EnableDoubanAssistScraping), SimpleCondition.IsTrue)]
        [DisplayName("豆瓣 Cookie")]
        [Description("可选，用于刷新豆瓣评分。")]
        public string DoubanCookie { get; set; } = string.Empty;

        [VisibleCondition(nameof(EnableDoubanAssistScraping), SimpleCondition.IsTrue)]
        [DisplayName("豆瓣 Access Token")]
        [Description("可选。")]
        public string DoubanAccessToken { get; set; } = string.Empty;

        [DisplayName("豆瓣辅助刮削")]
        [Description("评分、标题、简介优先用豆瓣的。")]
        public bool EnableDoubanAssistScraping { get; set; }

        [Browsable(false)]
        public List<EditorSelectOption> DoubanAssistScrapeScopeList
        {
            get => new List<EditorSelectOption>
            {
                new EditorSelectOption { Value = string.Empty, Name = "电影和剧集", IsEnabled = true },
                new EditorSelectOption { Value = "Movie", Name = "仅电影", IsEnabled = true },
                new EditorSelectOption { Value = "Series", Name = "仅剧集", IsEnabled = true }
            };
            set { }
        }

        [DisplayName("豆瓣作用于")]
        [SelectItemsSource(nameof(DoubanAssistScrapeScopeList))]
        [VisibleCondition(nameof(EnableDoubanAssistScraping), SimpleCondition.IsTrue)]
        public string DoubanAssistScrapeScope { get; set; } = string.Empty;

        [DisplayName("使用替代 TMDB 地址")]
        [Description("TMDB 访问不了时，换成自己的 API / 图片地址或密钥。")]
        public bool AltMovieDbConfig { get; set; } = true;

        [DisplayName("API 地址")]
        [Description("默认 https://api.tmdb.org；留空用 Emby 默认 api.themoviedb.org。")]
        [VisibleCondition(nameof(AltMovieDbConfig), SimpleCondition.IsTrue)]
        public string AltMovieDbApiUrl { get; set; } = "https://api.tmdb.org";

        [DisplayName("图片地址")]
        [Description("留空用默认 image.tmdb.org。")]
        [VisibleCondition(nameof(AltMovieDbConfig), SimpleCondition.IsTrue)]
        public string AltMovieDbImageUrl { get; set; } = "https://image.tmdb.org";

        [DisplayName("API 密钥")]
        [Description("留空用 Emby 默认。")]
        [VisibleCondition(nameof(AltMovieDbConfig), SimpleCondition.IsTrue)]
        public string AltMovieDbApiKey { get; set; } = string.Empty;

        [Browsable(false)]
        [Required]
        public string RefreshPersonMode { get; set; } = RefreshPersonOption.Default.ToString();

        public enum EpisodeRefreshOption
        {
            [DescriptionL("EpisodeRefreshOption_NoOverview_No_Overview", typeof(Resources))]
            NoOverview,
            [DescriptionL("EpisodeRefreshOption_NoImage_No_Image", typeof(Resources))]
            NoImage,
            [DescriptionL("EpisodeRefreshOption_NonChineseOverview_Non_Chinese_Overview", typeof(Resources))]
            NonChineseOverview,
            [DescriptionL("EpisodeRefreshOption_DefaultEpisodeName_Default_Episode_Name", typeof(Resources))]
            DefaultEpisodeName,
            [DescriptionL("EpisodeRefreshOption_ReplaceCapturedImage_Replace_Captured_Image", typeof(Resources))]
            ReplaceCapturedImage
        }

        [Browsable(false)]
        public List<EditorSelectOption> EpisodeRefreshOptionList
        {
            get { Common.OptionLabels.RepairList(_episodeRefreshOptionList); return _episodeRefreshOptionList; }
            set { _episodeRefreshOptionList = value ?? new List<EditorSelectOption>(); }
        }
        private List<EditorSelectOption> _episodeRefreshOptionList = new List<EditorSelectOption>();

        [Browsable(false)] // 仪表盘 →「刷新剧集元数据」→ 配置
        public string EpisodeRefreshScope { get; set; } = string.Join(",", EpisodeRefreshOption.NoOverview.ToString(),
            EpisodeRefreshOption.NoImage.ToString());
        
        [Browsable(false)] // 仪表盘 →「刷新剧集元数据」→ 配置
        public int EpisodeRefreshLookbackDays { get; set; } = 365;

        [Browsable(false)] // 仪表盘 →「元数据增强」→ 配置
        public bool FullScanOnEveryRun { get; set; } = false;

        [Browsable(false)] // 仪表盘 →「元数据增强」→ 配置
        public bool EnableScheduledMetadataEnhance { get; set; } = false;

        [Browsable(false)] // 仪表盘 →「元数据增强」→ 配置
        public bool DryRun { get; set; } = false;

        public override MediaBrowser.Model.GenericEdit.IEditObjectContainer CreateEditContainer() =>
            UI.OptionGroups.Apply(base.CreateEditContainer(),
                new UI.OptionGroup("中文元数据", nameof(ChineseMovieDb), nameof(FallbackLanguages), nameof(BlockNonFallbackLanguage),
                    nameof(PreferTraditionalChinese), nameof(ClearTagline), nameof(ChineseTvdb), nameof(PinyinSortName)),
                new UI.OptionGroup("图片与演员", nameof(PreferOriginalPoster), nameof(EnhanceMovieDbPerson), nameof(EnhanceNfoMetadata)),
                new UI.OptionGroup("剧集组", nameof(MovieDbEpisodeGroup), nameof(EnableMovieDbEpisodeGroupExtendedInfo), nameof(LocalEpisodeGroup)),
                new UI.OptionGroup("豆瓣", nameof(EnableDoubanAssistScraping), nameof(DoubanAssistScrapeScope), nameof(DoubanCookie), nameof(DoubanAccessToken)),
                new UI.OptionGroup("TMDB 接口", nameof(AltMovieDbConfig), nameof(AltMovieDbApiUrl), nameof(AltMovieDbImageUrl),
                    nameof(AltMovieDbApiKey), nameof(ClearRecentTmdbCacheOnStartup), nameof(MdbListApiKey)),
                new UI.OptionGroup("本地缓存", nameof(MetadataCacheMode), nameof(MetadataCacheRootFolder))
                {
                    Description = "剧集刷新、元数据增强、豆瓣评分等任务的参数在首页「计划任务」里配置。"
                });
    }
}
