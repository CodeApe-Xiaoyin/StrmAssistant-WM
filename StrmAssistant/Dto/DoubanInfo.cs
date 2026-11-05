using System.Text.Json.Serialization;

namespace StrmAssistant.Dto
{
    /// <summary>
    /// 移植自 PRO 的 StrmAssistant.Dto.Douban（frodo /movie|/tv 详情响应的精简结构）。
    /// </summary>
    public class DoubanInfo
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("original_title")] public string OriginalTitle { get; set; }
        [JsonPropertyName("year")] public string Year { get; set; }
        [JsonPropertyName("intro")] public string Intro { get; set; }
        [JsonPropertyName("rating")] public DoubanRating Rating { get; set; }
        [JsonPropertyName("pic")] public DoubanPicture Pic { get; set; }
    }

    public class DoubanRating
    {
        [JsonPropertyName("value")] public float Value { get; set; }
        [JsonPropertyName("count")] public int Count { get; set; }
        [JsonPropertyName("star_count")] public float StarCount { get; set; }
    }

    public class DoubanPicture
    {
        [JsonPropertyName("large")] public string Large { get; set; }
        [JsonPropertyName("normal")] public string Normal { get; set; }
    }
}
