using System;
using System.Linq;
using System.Threading;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;

namespace StrmAssistant.Common
{
    /// <summary>把「播放进度达到阈值后，之前未看的剧集标记为已看」这件事抽成可复用逻辑。</summary>
    public static class PreviousEpisodeMarker
    {
        private static int _depth;

        /// <summary>标记当前分集之前（季集号更小）且未看过的分集，返回标记数量。</summary>
        public static int MarkAsync(User user, Episode episode, ILibraryManager libraryManager,
            IUserDataManager userDataManager, ILogger logger)
        {
            if (user == null || episode == null || libraryManager == null || userDataManager == null) return 0;

            // 自己写 user data 会再次触发 UserDataSaved，这里做重入保护
            if (Interlocked.CompareExchange(ref _depth, 1, 0) != 0) return 0;

            try
            {
                var series = episode.Series ?? libraryManager.GetItemById(episode.SeriesId) as Series;
                if (series == null) return 0;

                var query = new InternalItemsQuery
                {
                    Recursive = true,
                    IncludeItemTypes = new[] { typeof(Episode).Name },
                    AncestorIds = new[] { series.InternalId }
                };

                var marked = 0;

                foreach (var sibling in libraryManager.GetItemList(query).OfType<Episode>())
                {
                    if (!EpisodeProgressPolicy.IsBefore(sibling.ParentIndexNumber, sibling.IndexNumber,
                            episode.ParentIndexNumber, episode.IndexNumber)) continue;

                    var data = userDataManager.GetUserData(user, sibling);
                    if (data == null || data.Played) continue;

                    data.Played = true;
                    data.PlaybackPositionTicks = 0;
                    userDataManager.SaveUserData(user, sibling, data, UserDataSaveReason.TogglePlayed,
                        CancellationToken.None);
                    marked++;
                    logger?.Info("MarkPrevious - marked " + sibling.Name);
                }

                return marked;
            }
            catch (Exception e)
            {
                logger?.Info("MarkPrevious - failed: " + e.Message);
                return 0;
            }
            finally
            {
                Interlocked.Exchange(ref _depth, 0);
            }
        }
    }
}
