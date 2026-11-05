using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StrmAssistant.Dto
{
    /// <summary>
    /// 移植自 PRO 的 StrmAssistant.Dto.MovieDb 剧集组数据结构（TMDb /tv/{id}/episode_group/{gid}）。
    /// </summary>
    public class MovieDbEpisodeGroupResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; }
        [JsonPropertyName("episode_count")] public int EpisodeCount { get; set; }
        [JsonPropertyName("group_count")] public int GroupCount { get; set; }
        [JsonPropertyName("type")] public string Type { get; set; }
        public List<MovieDbEpisodeGroup> Groups { get; set; } = new List<MovieDbEpisodeGroup>();
    }

    public class MovieDbEpisodeGroup
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("order")] public int Order { get; set; }
        public List<MovieDbGroupEpisode> Episodes { get; set; } = new List<MovieDbGroupEpisode>();
    }

    public class MovieDbGroupEpisode
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("order")] public int Order { get; set; }
        [JsonPropertyName("season_number")] public int SeasonNumber { get; set; }
        [JsonPropertyName("episode_number")] public int EpisodeNumber { get; set; }
    }
}
