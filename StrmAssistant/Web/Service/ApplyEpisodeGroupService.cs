using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Services;
using StrmAssistant.Dto;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    /// <summary>
    /// 移植自 PRO「剧集组支持」的应用步骤：按剧集组顺序重排条目的季/集号。
    /// </summary>
    public class ApplyEpisodeGroupService : IService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly MovieDbEpisodeGroupService _episodeGroupService = new MovieDbEpisodeGroupService();

        public ApplyEpisodeGroupService(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
        }

        public async Task<object> Post(ApplyMovieDbEpisodeGroup request)
        {
            var series = _libraryManager.GetItemById(request.SeriesId) as Series;
            if (series == null) return null;

            var mapping = (List<MovieDbEpisodeMapping>)await _episodeGroupService
                .Get(new GetMovieDbEpisodeGroupMap { TmdbId = request.TmdbId, GroupId = request.GroupId })
                .ConfigureAwait(false);

            if (mapping == null || mapping.Count == 0) return null;

            ApplyMapping(series, mapping);
            return null;
        }

        /// <summary>把映射应用到剧集下的分集（本地剧集组 JSON 也走这里）。</summary>
        public void ApplyMapping(Series series, List<MovieDbEpisodeMapping> mapping)
        {
            var query = new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(Episode).Name },
                Recursive = true
            };
            query.AncestorIds = new[] { series.InternalId };

            var episodes = _libraryManager.GetItemList(query)
                .OfType<Episode>()
                .OrderBy(e => e.ParentIndexNumber)
                .ThenBy(e => e.IndexNumber)
                .ToList();

            var count = Math.Min(episodes.Count, mapping.Count);

            // 「剧集组扩展信息」：连同组内分集名一起写入
            var applyExtended = Plugin.Instance?.GetPluginOptions()?.MetadataEnhanceOptions?
                .EnableMovieDbEpisodeGroupExtendedInfo == true;

            var appliedCount = 0;
            var failedCount = 0;

            for (var i = 0; i < count; i++)
            {
                var episode = episodes[i];
                var target = mapping[i];

                episode.ParentIndexNumber = target.SeasonNumber;
                episode.IndexNumber = target.EpisodeNumber;

                if (applyExtended && !string.IsNullOrWhiteSpace(target.EpisodeName) &&
                    !string.Equals(episode.Name, target.EpisodeName, StringComparison.Ordinal))
                {
                    episode.Name = target.EpisodeName;
                }

                if (Common.DryRunMode.Skip("应用剧集组映射 " + series.Name + " → S" + target.SeasonNumber + "E" + target.EpisodeNumber))
                    continue;

                try
                {
                    _libraryManager.UpdateItem(episode, series, ItemUpdateType.MetadataEdit);
                    appliedCount++;
                }
                catch (Exception e)
                {
                    // 单集失败不影响其它集，但要如实报出来，避免"改了一半"没人知道
                    failedCount++;
                    Plugin.Instance?.Logger?.Info(
                        "Episode group - failed for " + episode.Name + ": " + e.Message);
                }
            }

            if (failedCount > 0)
            {
                Plugin.Instance?.Logger?.Info(
                    $"Episode group - 部分成功：{appliedCount} 集已应用，{failedCount} 集失败（可重跑，幂等）");
            }

            Plugin.Instance.Logger.Info("Episode group applied to {0} episodes of {1}", count, series.Name);
        }

        /// <summary>读取本地剧集组 JSON（对应 PRO 的「本地剧集组」）。</summary>
        /// <summary>季集号合法性：季 >= 0，集 >= 1，且不超过 10000。</summary>
        internal static bool IsValidNumber(MovieDbGroupEpisode entry)
        {
            if (entry == null) return false;
            if (entry.SeasonNumber < 0 || entry.SeasonNumber > 10000) return false;
            if (entry.EpisodeNumber < 1 || entry.EpisodeNumber > 10000) return false;
            return true;
        }

        public static List<MovieDbEpisodeMapping> LoadLocalMapping(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return null;

            try
            {
                var json = Common.TextFile.ReadAllText(path);
                var response = System.Text.Json.JsonSerializer.Deserialize<MovieDbEpisodeGroupResponse>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (response?.Groups == null) return null;

                // 外部 JSON 的季集号可能非法（负数、超大、0），过滤掉再映射，
                // 否则会把整部剧的季集号写坏
                foreach (var group in response.Groups)
                {
                    if (group?.Episodes == null) continue;
                    group.Episodes = group.Episodes.Where(IsValidNumber).ToList();
                }

                return response.Groups
                    .OrderBy(g => g.Order)
                    .SelectMany(g => g.Episodes.Select(e => new MovieDbEpisodeMapping
                    {
                        GroupName = g.Name,
                        GroupOrder = g.Order,
                        SeasonNumber = e.SeasonNumber,
                        EpisodeNumber = e.EpisodeNumber,
                        EpisodeName = e.Name
                    }))
                    .OrderBy(m => m.GroupOrder)
                    .ThenBy(m => m.EpisodeNumber)
                    .Select((m, index) =>
                    {
                        m.DisplayOrder = index + 1;
                        return m;
                    })
                    .ToList();
            }
            catch
            {
                return null;
            }
        }
    }
}
