using System;
using System.Collections.Concurrent;
using System.Threading;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 后台队列的进度（仪表盘「刷新队列」用）：每个队列记录当前这一批的总数和已完成数。
    /// 批次开始时 Begin，每处理完一项 Step，整批结束 End（结束后保留最后一次的数字，直到下一批开始）。
    /// </summary>
    public static class QueueProgress
    {
        public sealed class Batch
        {
            public int Total;
            public int Done;
            public bool Running;
            public DateTime StartedUtc;
            public DateTime? FinishedUtc;
        }

        private static readonly ConcurrentDictionary<string, Batch> Batches = new ConcurrentDictionary<string, Batch>();

        public static void Begin(string name, int total)
        {
            Batches[name] = new Batch { Total = total, Done = 0, Running = true, StartedUtc = DateTime.UtcNow };
        }

        public static void Step(string name)
        {
            if (Batches.TryGetValue(name, out var b)) Interlocked.Increment(ref b.Done);
        }

        public static void End(string name)
        {
            if (Batches.TryGetValue(name, out var b))
            {
                b.Running = false;
                b.FinishedUtc = DateTime.UtcNow;
            }
        }

        public static Batch Get(string name) => Batches.TryGetValue(name, out var b) ? b : null;
    }
}
