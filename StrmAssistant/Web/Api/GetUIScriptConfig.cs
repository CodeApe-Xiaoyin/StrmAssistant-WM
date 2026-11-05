using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    [Unauthenticated]
    [Route("/StrmAssistant/UIScript/Config", "GET", Summary = "Front-end script config")]
    public class GetUIScriptConfig : IReturn<UIScriptConfig>
    {
    }

    public class UIScriptConfig
    {
        public bool HidePersonNoImage { get; set; }
        public bool HidePersonActorOnly { get; set; }
        public bool HidePersonNonChinese { get; set; }
        public bool ReverseSeasonOrder { get; set; }
        public bool ReverseEpisodeOrder { get; set; }
        public bool ShowLibraryItemCount { get; set; }
        public bool ReplaceUnplayedWithTotal { get; set; }
        public bool ReverseLogLines { get; set; }
        public bool BeautifyMissingMetadata { get; set; }
        public bool ShowCollectionsFromSeasons { get; set; }
        public string SearchTuningPreferences { get; set; }
        public string Version { get; set; }
    }
}
