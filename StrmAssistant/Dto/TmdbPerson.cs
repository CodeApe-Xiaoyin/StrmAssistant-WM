using System.Text.Json.Serialization;

namespace StrmAssistant.Dto
{
    /// <summary>TMDb /person/{id} 响应（用于演职人员增强）。</summary>
    public class TmdbPerson
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("biography")] public string Biography { get; set; }
        [JsonPropertyName("birthday")] public string Birthday { get; set; }
        [JsonPropertyName("deathday")] public string Deathday { get; set; }
        [JsonPropertyName("place_of_birth")] public string PlaceOfBirth { get; set; }
    }
}
