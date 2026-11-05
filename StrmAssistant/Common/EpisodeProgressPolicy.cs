namespace StrmAssistant.Common
{
    /// <summary>
    /// 「标记前集已看」的判定逻辑。抽成纯函数便于测试，也便于以后调整规则。
    /// </summary>
    public static class EpisodeProgressPolicy
    {
        /// <summary>播放进度是否已达到触发阈值。运行时长未知时一律不触发。</summary>
        public static bool ReachedThreshold(long positionTicks, long? runTimeTicks, int thresholdPercent)
        {
            if (!runTimeTicks.HasValue) return false;
            if (positionTicks <= 0 || runTimeTicks.Value <= 0) return false;

            var percent = (double)positionTicks / runTimeTicks.Value * 100d;
            return percent >= thresholdPercent;
        }

        /// <summary>剧集 a 是否在 b 之前（先比季，再比集）。</summary>
        public static bool IsBefore(int? aSeason, int? aEpisode, int? bSeason, int? bEpisode)
        {
            // 特别篇（第 0 季）和不知道季号的，不跟着正片自动标记：看到 S01E05 不代表看过所有特别篇
            if (!aSeason.HasValue || aSeason.Value == 0)
                return aSeason == 0 && bSeason == 0 && (aEpisode ?? 0) < (bEpisode ?? 0);
            var aS = aSeason.Value;
            var bS = bSeason ?? 0;
            if (aS != bS) return aS < bS;

            return (aEpisode ?? 0) < (bEpisode ?? 0);
        }
    }
}
