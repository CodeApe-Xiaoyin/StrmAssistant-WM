using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>Dev only：逐个放宽筛选条件，定位片头声纹任务为什么选不到条目。</summary>
    [Route("/StrmAssistant/Dev/IntroQueryDiag", "GET", Summary = "Dev only: bisect intro fingerprint query filters")]
    [MediaBrowser.Controller.Net.Authenticated(Roles = "Admin")]
    public class DevIntroQueryDiag : IReturn<object>
    {
    }

    public class DevIntroQueryDiagService : IService
    {
        private readonly ILibraryManager _libraryManager;

        public DevIntroQueryDiagService(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
        }

        private int CountItems(Action<InternalItemsQuery> configure)
        {
            var q = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Episode" },
                Recursive = true,
                GroupByPresentationUniqueKey = false
            };
            configure?.Invoke(q);
            try
            {
                return _libraryManager.GetItemList(q).Count();
            }
            catch (Exception e)
            {
                Plugin.Instance?.Logger?.Info("IntroQueryDiag - 查询失败: " + e.Message);
                return -1;
            }
        }

        public object Get(DevIntroQueryDiag request)
        {
            var rows = new List<object>();
            void Add(string name, Action<InternalItemsQuery> cfg) => rows.Add(new { filter = name, count = CountItems(cfg) });

            Add("仅 Episode", null);
            Add("+HasAudioStream=true", q => q.HasAudioStream = true);
            Add("+WithoutChapterMarkers=IntroStart", q => q.WithoutChapterMarkers = new[] { MediaBrowser.Model.Entities.MarkerType.IntroStart });
            Add("+HasIntroDetectionFailure=false", q => q.HasIntroDetectionFailure = false);
            Add("+MinRunTimeTicks(10min)", q => q.MinRunTimeTicks = TimeSpan.FromMinutes(10).Ticks);
            Add("+MinParentIndexNumber=1", q => q.MinParentIndexNumber = 1);
            Add("全部组合（不含 MinParentIndexNumber）", q =>
            {
                q.HasAudioStream = true;
                q.WithoutChapterMarkers = new[] { MediaBrowser.Model.Entities.MarkerType.IntroStart };
                q.HasIntroDetectionFailure = false;
                q.MinRunTimeTicks = TimeSpan.FromMinutes(10).Ticks;
            });
            Add("PathStartsWithAny=/media/剧集/", q =>
                q.PathStartsWithAny = new[] { "/media/剧集/" });
            Add("PathStartsWithAny=/media/剧集（无尾斜杠）", q =>
                q.PathStartsWithAny = new[] { "/media/剧集" });
            Add("全部组合+PathStartsWithAny", q =>
            {
                q.HasAudioStream = true;
                q.WithoutChapterMarkers = new[] { MediaBrowser.Model.Entities.MarkerType.IntroStart };
                q.HasIntroDetectionFailure = false;
                q.MinRunTimeTicks = TimeSpan.FromMinutes(10).Ticks;
                q.MinParentIndexNumber = 1;
                q.PathStartsWithAny = new[] { "/media/剧集/" };
            });
            Add("全部组合（含 MinParentIndexNumber）", q =>
            {
                q.HasAudioStream = true;
                q.WithoutChapterMarkers = new[] { MediaBrowser.Model.Entities.MarkerType.IntroStart };
                q.HasIntroDetectionFailure = false;
                q.MinRunTimeTicks = TimeSpan.FromMinutes(10).Ticks;
                q.MinParentIndexNumber = 1;
            });

            return new { ok = true, rows };
        }
    }
}
