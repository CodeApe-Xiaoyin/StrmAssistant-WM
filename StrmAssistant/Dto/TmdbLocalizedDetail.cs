using System.Text.Json.Serialization;

namespace StrmAssistant.Dto
{
    /// <summary>按语言取回的名称/简介（中文名优化用）。</summary>
    public class TmdbLocalizedDetail
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("overview")] public string Overview { get; set; }
        [JsonPropertyName("original_language")] public string OriginalLanguage { get; set; }
    }
}
