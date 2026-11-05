using System.ComponentModel;
using Emby.Web.GenericEdit;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Properties;

namespace StrmAssistant.Options.Experience
{
    /// <summary>移植自 PRO 的 UIFunctionOptions（体验增强里的「界面功能」）。</summary>
    public class UIFunctionOptions : EditableOptionsBase
    {
        [DisplayNameL("UIFunctionOptions_EditorTitle_UI_Functions", typeof(Resources))]
        public override string EditorTitle => "界面";

        public enum HidePersonMode
        {
            [DescriptionL("HidePersonOption_None_None", typeof(Resources))]
            None,
            [DescriptionL("HidePersonOption_NoImage_NoImage", typeof(Resources))]
            NoImage,
            [DescriptionL("HidePersonOption_ActorOnly_ActorOnly", typeof(Resources))]
            ActorOnly,
            [DescriptionL("HidePersonOption_NonChinese_NonChinese", typeof(Resources))]
            NonChinese
        }

        public enum ItemOrderOption
        {
            [DescriptionL("ItemOrderOption_NaturalSort_NaturalSort", typeof(Resources))]
            NaturalSort,
            [DescriptionL("ItemOrderOption_ReverseSeasonOrder_ReverseSeasonOrder", typeof(Resources))]
            ReverseSeasonOrder,
            [DescriptionL("ItemOrderOption_ReverseEpisodeOrder_ReverseEpisodeOrder", typeof(Resources))]
            ReverseEpisodeOrder,
            [DescriptionL("ItemOrderOption_CollectionPerItemDateCreated_CollectionPerItemDateCreated", typeof(Resources))]
            CollectionPerItemDateCreated
        }

        [DisplayName("隐藏演职人员")]
        public HidePersonMode HidePerson { get; set; } = HidePersonMode.None;

        // 界面隐藏：Emby 4.10 的季/集列表是虚拟滚动（display:block），CSS 倒序不生效；「按合集条目加入时间」也没有实现（2026-11-05 实测）
        [Browsable(false)]
        [DisplayNameL("UIFunctionOptions_ItemOrder_Item_Order", typeof(Resources))]
        [DescriptionL("UIFunctionOptions_ItemOrder_Control_item_order_in_season_and_episode_views__Default_is_NaturalSort", typeof(Resources))]
        public ItemOrderOption ItemOrder { get; set; } = ItemOrderOption.NaturalSort;

        [DisplayName("美化缺失的分集信息")]
        public bool BeautifyMissingMetadata { get; set; } = false;

        [DisplayName("剧集页显示各季所属合集")]
        public bool ShowCollectionsFromSeasons { get; set; } = false;

        // 界面隐藏：Emby 4.10 的日志页里没有脚本要找的 .logContainer 等元素，开了也没效果（2026-11-05 实测）
        [Browsable(false)]
        [DisplayNameL("UIFunctionOptions_ReverseLogLines_Reverse_Log_Lines", typeof(Resources))]
        [DescriptionL("UIFunctionOptions_ReverseLogLines_Show_the_newest_log_line_at_the_top__Default_is_OFF_", typeof(Resources))]
        public bool ReverseLogLines { get; set; } = false;

        [DisplayName("首页显示媒体库条目数")]
        public bool ShowLibraryItemCount { get; set; } = false;

        [DisplayName("剧集角标显示总集数")]
        [Description("代替未看集数。")]
        public bool ReplaceUnplayedWithTotal { get; set; } = false;

        [DisplayName("所有用户的媒体库顺序跟第一个管理员一致")]
        public bool EnforceLibraryOrder { get; set; } = false;

        // 界面隐藏：对应的挂钩已不安装，与上面「不自动创建合集」重复
        [Browsable(false)]
        public bool NoBoxsetsAutoCreation { get; set; } = false;

        [DisplayName("缺失分集支持 TMDB")]
        public bool EnhanceMissingEpisodes { get; set; } = false;



        [DisplayName("看完一集后把前面的标为已看")]
        public bool MarkPreviousEpisodesPlayed { get; set; } = false;

        [DisplayName("看到多少算看完（%）")]
        [VisibleCondition(nameof(MarkPreviousEpisodesPlayed), SimpleCondition.IsTrue)]
        [MediaBrowser.Model.Attributes.MinValue(10)]
        [MediaBrowser.Model.Attributes.MaxValue(100)]
        public int MarkPreviousEpisodesPlayedThreshold { get; set; } = 90;
    }
}
