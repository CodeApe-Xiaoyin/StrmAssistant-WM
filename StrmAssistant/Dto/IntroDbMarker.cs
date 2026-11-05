using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StrmAssistant.Dto
{
    /// <summary>外部片头库（IntroDb / TIDB）返回的标记。</summary>
    public class IntroDbMarkerResponse
    {
        [JsonPropertyName("segments")] public List<IntroDbSegment> Segments { get; set; } = new List<IntroDbSegment>();
        [JsonPropertyName("intro")] public List<IntroDbSegment> Intro { get; set; }
        [JsonPropertyName("credits")] public List<IntroDbSegment> Credits { get; set; }
    }

    public class IntroDbSegment
    {
        [JsonPropertyName("type")] public string Type { get; set; }
        [JsonPropertyName("start")] public double Start { get; set; }
        [JsonPropertyName("end")] public double End { get; set; }
        [JsonPropertyName("start_ms")] public double StartMs { get; set; }
        [JsonPropertyName("end_ms")] public double EndMs { get; set; }
    }

    /// <summary>归一化后的标记（秒）。</summary>
    public class IntroMarker
    {
        public double IntroStart { get; set; }
        public double IntroEnd { get; set; }
        public double CreditsStart { get; set; }
        public string Provider { get; set; }
    }
}
