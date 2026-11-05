using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using StrmAssistant.Dto;

namespace StrmAssistant
{
    /// <summary>把 IntroMarker 写成章节（任务与 API 共用）。</summary>
    public static class IntroDbApplyServiceHelper
    {
        private const long TicksPerSecond = 10_000_000L;

        /// <summary>写入片头标记；数据不合法时返回 false 且不写任何章节。</summary>
        public static bool Apply(IItemRepository itemRepository, BaseItem item, IntroMarker marker)
        {
            // 外部数据先校验：负值、首尾颠倒、超出片长一律拦掉
            var runtimeSeconds = item?.RunTimeTicks.HasValue == true && item.RunTimeTicks.Value > 0
                ? item.RunTimeTicks.Value / (double) TicksPerSecond
                : (double?) null;

            marker = StrmAssistant.Common.IntroMarkerValidator.Normalize(marker, runtimeSeconds);
            if (marker == null)
            {
                Plugin.Instance?.Logger?.Info("IntroDb - marker rejected for " + item?.Name);
                return false;
            }

            var chapters = itemRepository.GetChapters(item, System.Threading.CancellationToken.None)
                .Where(c => c.MarkerType != MarkerType.IntroStart &&
                            c.MarkerType != MarkerType.IntroEnd &&
                            c.MarkerType != MarkerType.CreditsStart)
                .ToList();

            chapters.Add(new ChapterInfo
            {
                Name = MarkerType.IntroStart + " IntroDb",
                MarkerType = MarkerType.IntroStart,
                StartPositionTicks = (long)(marker.IntroStart * TicksPerSecond)
            });
            chapters.Add(new ChapterInfo
            {
                Name = MarkerType.IntroEnd + " IntroDb",
                MarkerType = MarkerType.IntroEnd,
                StartPositionTicks = (long)(marker.IntroEnd * TicksPerSecond)
            });

            if (marker.CreditsStart > 0)
            {
                chapters.Add(new ChapterInfo
                {
                    Name = MarkerType.CreditsStart + " IntroDb",
                    MarkerType = MarkerType.CreditsStart,
                    StartPositionTicks = (long)(marker.CreditsStart * TicksPerSecond)
                });
            }

            if (StrmAssistant.Common.DryRunMode.Skip("写入片头标记 " + item?.Name))
                return false;

            chapters.Sort((a, b) => a.StartPositionTicks.CompareTo(b.StartPositionTicks));
            itemRepository.SaveChapters(item.InternalId, chapters);
            return true;
        }
    }
}