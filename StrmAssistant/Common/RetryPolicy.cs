using System;
using System.Net;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 抓取类请求的统一重试策略：指数退避，只重试「服务端错误 / 限流 / 网络异常」。
    /// 纯逻辑，便于单元测试。
    /// </summary>
    public static class RetryPolicy
    {
        public const int MaxAttempts = 3;

        /// <summary>第 attempt 次失败后应等待的时长：300ms、900ms、2.7s。</summary>
        public static TimeSpan DelayAfterAttempt(int attempt)
        {
            if (attempt < 1) attempt = 1;
            return TimeSpan.FromMilliseconds(300 * Math.Pow(3, attempt - 1));
        }

        /// <summary>5xx 与 429 值得重试；4xx（除 429）不重试。</summary>
        public static bool ShouldRetryStatus(int statusCode)
        {
            if (statusCode == 429) return true;
            return statusCode >= 500 && statusCode <= 599;
        }

        public static bool ShouldRetryStatus(HttpStatusCode statusCode) => ShouldRetryStatus((int)statusCode);

        /// <summary>取消不算失败，不应该重试。</summary>
        public static bool ShouldRetryException(Exception exception)
        {
            return !(exception is OperationCanceledException);
        }
    }
}
