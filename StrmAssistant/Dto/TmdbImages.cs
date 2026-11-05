using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StrmAssistant.Dto
{
    public class TmdbTvDetail
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("original_language")] public string OriginalLanguage { get; set; }
    }

    public class TmdbImageList
    {
        [JsonPropertyName("posters")] public List<TmdbImage> Posters { get; set; } = new List<TmdbImage>();
    }

    public class TmdbImage
    {
        [JsonPropertyName("file_path")] public string FilePath { get; set; }
        [JsonPropertyName("iso_639_1")] public string Language { get; set; }
        [JsonPropertyName("vote_average")] public double VoteAverage { get; set; }
        [JsonPropertyName("width")] public int Width { get; set; }
    }
}
