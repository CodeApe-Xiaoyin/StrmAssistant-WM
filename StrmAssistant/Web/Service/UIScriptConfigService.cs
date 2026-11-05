using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    [Unauthenticated]
    public class UIScriptConfigService : IService
    {
        public object Get(GetUIScriptConfig request)
        {
            var options = Plugin.Instance.GetPluginOptions();
            var ui = options.ExperienceEnhanceOptions.UIFunctionOptions;

            return new UIScriptConfig
            {
                HidePersonNoImage = ui.HidePerson == Options.Experience.UIFunctionOptions.HidePersonMode.NoImage,
                HidePersonActorOnly = ui.HidePerson == Options.Experience.UIFunctionOptions.HidePersonMode.ActorOnly,
                HidePersonNonChinese = ui.HidePerson == Options.Experience.UIFunctionOptions.HidePersonMode.NonChinese,
                ReverseSeasonOrder = ui.ItemOrder.ToString().Contains("ReverseSeason"),
                ReverseEpisodeOrder = ui.ItemOrder.ToString().Contains("ReverseEpisode"),
                ShowLibraryItemCount = ui.ShowLibraryItemCount,
                ReplaceUnplayedWithTotal = ui.ReplaceUnplayedWithTotal,
                ReverseLogLines = ui.ReverseLogLines,
                BeautifyMissingMetadata = ui.BeautifyMissingMetadata,
                ShowCollectionsFromSeasons = ui.ShowCollectionsFromSeasons,
                SearchTuningPreferences = options.ExperienceEnhanceOptions.SearchTuningPreferences ?? string.Empty,
                Version = "1.0"
            };
        }
    }
}
