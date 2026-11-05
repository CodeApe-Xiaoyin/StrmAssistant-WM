using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StrmAssistant.Dto
{
    /// <summary>豆瓣搜索响应（frodo /search/weixin）。</summary>
    public class DoubanSearchResponse
    {
        [JsonPropertyName("items")] public List<DoubanSearchItem> Items { get; set; } = new List<DoubanSearchItem>();
    }

    public class DoubanSearchItem
    {
        [JsonPropertyName("target")] public DoubanSearchTarget Target { get; set; }
    }

    public class DoubanSearchTarget
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("year")] public string Year { get; set; }
        [JsonPropertyName("type")] public string Type { get; set; }
    }
}
