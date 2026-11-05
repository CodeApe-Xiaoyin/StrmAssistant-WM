using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>
    /// Dev only：诊断为什么某些分集拿不到片头指纹。
    /// 逐集报告 IsShortcut / IsFileProtocol / RunTimeTicks / 指纹文件是否存在 /
    /// Emby 自身 IsIntroDetectionSupported 的判定结果。
    /// </summary>
    [Route("/StrmAssistant/Dev/IntroDiag", "GET", Summary = "Dev only: diagnose intro detection support per episode")]
    [MediaBrowser.Controller.Net.Authenticated(Roles = "Admin")]
    public class DevIntroDiag : IReturn<object>
    {
        public string SeriesId { get; set; }
        public int Limit { get; set; }
    }

    public class DevIntroDiagService : IService
    {
        private readonly ILibraryManager _libraryManager;

        public DevIntroDiagService(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
        }

        public object Get(DevIntroDiag request)
        {
            var limit = request.Limit > 0 ? request.Limit : 30;
            var series = _libraryManager.GetItemById(request.SeriesId ?? "0") as Series;
            if (series == null) return new { ok = false, error = "series not found" };

            var manager = Plugin.FingerprintApi.GetAudioFingerprintManager();
            var supported = manager?.GetType().GetMethod("IsIntroDetectionSupported",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            var episodes = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Episode" },
                Recursive = true,
                AncestorIds = new[] { series.InternalId }
            }).OfType<Episode>().Take(limit).ToList();

            var rows = new List<object>();
            foreach (var e in episodes)
            {
                var libOptions = _libraryManager.GetLibraryOptions(e);
                bool? isSupported = null;
                string error = null;
                try
                {
                    if (supported != null && manager != null)
                        isSupported = (bool?)supported.Invoke(manager, new object[] { e, libOptions });
                }
                catch (Exception ex)
                {
                    error = (ex is TargetInvocationException tie && tie.InnerException != null
                        ? tie.InnerException : ex).Message;
                }

                rows.Add(new
                {
                    name = e.Name,
                    index = e.IndexNumber,
                    isShortcut = e.IsShortcut,
                    isFileProtocol = e.IsFileProtocol,
                    isPlaceHolder = e.IsPlaceHolder,
                    isCompleteMedia = e.IsCompleteMedia,
                    runTimeTicks = e.RunTimeTicks,
                    path = e.Path,
                    isIntroDetectionSupported = isSupported,
                    error
                });
            }

            return new { ok = true, series = series.Name, count = rows.Count, rows };
        }
    }
}
