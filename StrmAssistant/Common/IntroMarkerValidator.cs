using System;
using StrmAssistant.Dto;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 外部片头库返回的时间戳不可信：可能有负数、首尾颠倒、超出片长。
    /// 这里统一校验/裁剪，避免把坏数据写成章节（负的开始时间会直接影响播放）。
    /// </summary>
    public static class IntroMarkerValidator
    {
        /// <summary>允许的最短片头长度（秒）。</summary>
        public const double MinIntroSeconds = 1.0;

        /// <summary>
        /// 返回可安全写入的标记；不可用时返回 null。
        /// runtimeSeconds 未知时传 null（只做时间戳自身的合理性检查）。
        /// </summary>
        public static IntroMarker Normalize(IntroMarker marker, double? runtimeSeconds)
        {
            if (marker == null) return null;

            var start = marker.IntroStart;
            var end = marker.IntroEnd;
            var credits = marker.CreditsStart;

            if (double.IsNaN(start) || double.IsNaN(end) || double.IsNaN(credits)) return null;
            if (double.IsInfinity(start) || double.IsInfinity(end) || double.IsInfinity(credits)) return null;

            if (start < 0) start = 0;

            if (runtimeSeconds.HasValue && runtimeSeconds.Value > 0)
            {
                var runtime = runtimeSeconds.Value;

                if (start > runtime) return null;
                if (end > runtime) end = runtime;
                if (credits > runtime) credits = runtime;
            }

            // 片头必须有正向长度
            if (end - start < MinIntroSeconds) return null;

            // 片尾标记必须落在片头之后，否则丢弃（而不是写成重叠章节）
            if (credits > 0 && credits <= end) credits = 0;

            return new IntroMarker
            {
                IntroStart = start,
                IntroEnd = end,
                CreditsStart = credits,
                Provider = marker.Provider
            };
        }
    }
}
