using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Services;
using StrmAssistant.Common;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    /// <summary>移植自 PRO：把外部片头库标记落到剧集章节。</summary>
    public class IntroDbApplyService : IService
    {
        private const long TicksPerSecond = 10_000_000L;

        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;
        private readonly IntroDbApi _introDbApi;

        public IntroDbApplyService(ILibraryManager libraryManager, IItemRepository itemRepository,
            IHttpClient httpClient)
        {
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
            _introDbApi = new IntroDbApi(httpClient);
        }

        public async Task<object> Post(ApplyIntroDbMarker request)
        {
            var episode = _libraryManager.GetItemById(request.ItemId) as Episode;
            if (episode == null) return null;

            var series = episode.Series;
            var imdbId = string.IsNullOrEmpty(request.ImdbId)
                ? (series != null && series.ProviderIds.TryGetValue("Imdb", out var id) ? id : null)
                : request.ImdbId;

            if (string.IsNullOrEmpty(imdbId)) return null;

            var marker = await _introDbApi
                .GetMarkerAsync(imdbId, episode.ParentIndexNumber ?? 0, episode.IndexNumber ?? 0,
                    CancellationToken.None).ConfigureAwait(false);

            if (marker == null) return null;

            var chapters = _itemRepository
                .GetChapters(episode, CancellationToken.None)
                .Where(c => c.MarkerType != MarkerType.IntroStart &&
                            c.MarkerType != MarkerType.IntroEnd &&
                            c.MarkerType != MarkerType.CreditsStart)
                .ToList();

            chapters.Add(new ChapterInfo
            {
                Name = MarkerType.IntroStart + " IntroDb",
                MarkerType = MarkerType.IntroStart,
                StartPositionTicks = (long)(marker.IntroStart * TicksPerSecond)
            });
            chapters.Add(new ChapterInfo
            {
                Name = MarkerType.IntroEnd + " IntroDb",
                MarkerType = MarkerType.IntroEnd,
                StartPositionTicks = (long)(marker.IntroEnd * TicksPerSecond)
            });

            if (marker.CreditsStart > 0)
            {
                chapters.Add(new ChapterInfo
                {
                    Name = MarkerType.CreditsStart + " IntroDb",
                    MarkerType = MarkerType.CreditsStart,
                    StartPositionTicks = (long)(marker.CreditsStart * TicksPerSecond)
                });
            }

            chapters.Sort((a, b) => a.StartPositionTicks.CompareTo(b.StartPositionTicks));

            if (!Common.DryRunMode.Skip("写入片头标记 " + episode.Name))
                _itemRepository.SaveChapters(episode.InternalId, chapters);

            Plugin.Instance.Logger.Info($"IntroDb marker applied to {series?.Name} S{episode.ParentIndexNumber}E{episode.IndexNumber} " +
                                        $"({marker.Provider}): {marker.IntroStart}-{marker.IntroEnd}, credits {marker.CreditsStart}");
            return null;
        }
    }
}
