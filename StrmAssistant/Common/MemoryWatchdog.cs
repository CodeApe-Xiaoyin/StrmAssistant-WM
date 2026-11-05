using System;
using System.Diagnostics;
using MediaBrowser.Model.Logging;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 内存看门狗（对应 PRO 的 MemoryOptions：空闲时内存超过上限持续 15 分钟则重启服务器）。
    /// 决策逻辑单独抽出，便于用诊断端点验证而不必真的把进程杀掉。
    /// </summary>
    public static class MemoryWatchdog
    {
        public static readonly TimeSpan Threshold = TimeSpan.FromMinutes(15);

        private static DateTimeOffset? _exceededSince;

        public static int CurrentUsageMb =>
            (int)(Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024d);

        /// <summary>返回决策文本，便于日志与诊断端点复用。</summary>
        public static string Evaluate(int usageMb, int? limitMb, bool isIdle, DateTimeOffset now, ILogger logger = null)
        {
            if (!limitMb.HasValue || limitMb.Value <= 0)
            {
                _exceededSince = null;
                return "disabled";
            }

            if (usageMb <= limitMb.Value)
            {
                _exceededSince = null;
                return "within-limit";
            }

            if (!isIdle)
            {
                _exceededSince = null;
                return "busy";
            }

            _exceededSince ??= now;

            var elapsed = now - _exceededSince.Value;
            if (elapsed < Threshold)
            {
                return "exceeded-waiting(" + (int)elapsed.TotalMinutes + "min)";
            }

            return "exceeded-restart";
        }

        public static void Reset() => _exceededSince = null;
    }
}
