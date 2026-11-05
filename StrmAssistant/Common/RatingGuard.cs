namespace StrmAssistant.Common
{
    /// <summary>
    /// 评分范围守卫：外部来源（豆瓣等）偶尔返回越界值（0.5 分制、百分制、负数）。
    /// Emby 的 CommunityRating 期望 0–10，写入越界值会让界面显示异常。
    /// </summary>
    public static class RatingGuard
    {
        public const double Min = 0d;
        public const double Max = 10d;

        /// <summary>可用则返回规范化后的评分，否则返回 null（调用方应跳过写入）。</summary>
        public static float? Normalize(double? value)
        {
            if (!value.HasValue) return null;

            var rating = value.Value;
            if (double.IsNaN(rating) || double.IsInfinity(rating)) return null;
            if (rating <= Min || rating > Max) return null;

            return (float) rating;
        }
    }
}
