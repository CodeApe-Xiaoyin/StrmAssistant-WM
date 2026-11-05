using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StrmAssistant.Dto
{
    /// <summary>TMDb /tv/{id}/episode_groups 的响应。</summary>
    public class MovieDbEpisodeGroupList
    {
        [JsonPropertyName("results")] public List<MovieDbEpisodeGroupSummary> Results { get; set; } =
            new List<MovieDbEpisodeGroupSummary>();
    }

    public class MovieDbEpisodeGroupSummary
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("type")] public int Type { get; set; }
        [JsonPropertyName("group_count")] public int GroupCount { get; set; }
        [JsonPropertyName("episode_count")] public int EpisodeCount { get; set; }
    }
}
