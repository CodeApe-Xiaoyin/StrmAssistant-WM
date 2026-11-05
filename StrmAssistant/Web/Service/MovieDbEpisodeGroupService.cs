using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Services;
using StrmAssistant.Dto;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class MovieDbEpisodeGroupService : IService
    {
        public async Task<object> Get(GetMovieDbEpisodeGroup request)
        {
            var options = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions;
            var apiKey = Common.CommonUtility.ResolveMovieDbApiKey();

            var url =
                $"https://api.themoviedb.org/3/tv/{request.TmdbId}/episode_group/{request.GroupId}" +
                $"?api_key={apiKey}&language=zh-CN";

            try
            {
                var json = await Plugin.MetadataApi
                    .GetMovieDbRawAsync(url, CancellationToken.None)
                    .ConfigureAwait(false);

                if (string.IsNullOrEmpty(json)) return null;

                return System.Text.Json.JsonSerializer.Deserialize<MovieDbEpisodeGroupResponse>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (System.Exception ex)
            {
                Plugin.Instance.Logger.Info("Episode group detail failed: " + ex.Message);
                return null;
            }
        }


        /// <summary>列出该剧集在 TMDb 上的全部剧集组。</summary>
        public async Task<object> Get(GetMovieDbEpisodeGroups request)
        {
            var options = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions;
            var apiKey = Common.CommonUtility.ResolveMovieDbApiKey();

            var url = $"https://api.themoviedb.org/3/tv/{request.TmdbId}/episode_groups?api_key={apiKey}&language=zh-CN";

            try
            {
                var json = await Plugin.MetadataApi.GetMovieDbRawAsync(url, CancellationToken.None)
                    .ConfigureAwait(false);
                if (string.IsNullOrEmpty(json)) return new MovieDbEpisodeGroupList();

                return System.Text.Json.JsonSerializer.Deserialize<MovieDbEpisodeGroupList>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (System.Exception ex)
            {
                Plugin.Instance.Logger.Info("Episode groups failed: " + ex.Message);
                return new MovieDbEpisodeGroupList();
            }
        }

        public async Task<object> Get(GetMovieDbEpisodeGroupMap request)
        {
            var response = (MovieDbEpisodeGroupResponse)await Get(
                new GetMovieDbEpisodeGroup { TmdbId = request.TmdbId, GroupId = request.GroupId })
                .ConfigureAwait(false);

            if (response?.Groups == null) return new List<MovieDbEpisodeMapping>();

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

    }
}
