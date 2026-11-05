using System.ComponentModel;
using Emby.Web.GenericEdit;
using MediaBrowser.Model.Attributes;

namespace StrmAssistant.Options.Experience
{
    /// <summary>「Emby 直连」：远端 http .strm 直接播放时，让客户端去拉直链，不经 Emby 中转。</summary>
    public class DirectLinkOptions : EditableOptionsBase
    {
        public override string EditorTitle => "Emby 直连";

        [DisplayName("视频 Strm 直连")]
        [Description("直接播放远端 http strm 时让客户端直连源地址，不经 Emby 中转；需要转码时照常。")]
        public bool StrmDirectVideo { get; set; } = false;

        [DisplayName("跟踪 302 跳转")]
        [Description("把 strm 地址跳转后的最终地址交给客户端（内网 LitePan → 网盘 CDN，外网也能播）。")]
        [VisibleCondition(nameof(StrmDirectVideo), SimpleCondition.IsTrue)]
        public bool StrmDirectFollowRedirect { get; set; } = true;

        [DisplayName("不直连的客户端")]
        [Description("客户端名或 User-Agent 关键字，命中的不直连。逗号或换行分隔。")]
        [EditMultiline(3)]
        [VisibleCondition(nameof(StrmDirectVideo), SimpleCondition.IsTrue)]
        public string StrmDirectClientBlacklist { get; set; } = string.Empty;

        [DisplayName("音乐 Strm 直连")]
        [Description("同上，作用于音乐。")]
        public bool StrmDirectAudio { get; set; } = false;
    }
}
