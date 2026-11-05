namespace StrmAssistant.Dto
{
    /// <summary>季/集在剧集组中的播放顺序。</summary>
    public class MovieDbEpisodeMapping
    {
        public string GroupName { get; set; }
        public int GroupOrder { get; set; }
        public int SeasonNumber { get; set; }
        public int EpisodeNumber { get; set; }
        public string EpisodeName { get; set; }
        public int DisplayOrder { get; set; }
    }
}
