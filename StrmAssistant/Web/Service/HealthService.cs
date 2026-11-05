using System;
using System.Linq;
using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class HealthService : IService
    {
        public object Get(HealthApi request)
        {
            var pinyin = false;
            var traditional = false;
            string conflict = "unknown";
            var cache = (object) null;
            var memory = (object) null;
            var taskCount = 0;
            var error = (string) null;

            try
            {
                var deps = (System.Collections.Generic.Dictionary<string, string>)
                    new DependencyCheckService().Get(new DependencyCheckApi());

                pinyin = deps.TryGetValue("pinyin", out var py) && py == "SQ";

                traditional = deps.TryGetValue("traditional", out var tr) &&
                              !string.IsNullOrEmpty(tr) && !tr.StartsWith("FAIL");

                conflict = deps.TryGetValue("conflictPlugins", out var cf) ? cf : "unknown";
            }
            catch (Exception e)
            {
                error = "dependency: " + e.Message;
            }

            try
            {
                cache = new CacheStatsService().Get(new CacheStatsApi());
            }
            catch (Exception e)
            {
                error = (error == null ? "" : error + " | ") + "cache: " + e.Message;
            }

            try
            {
                memory = new MemoryCheckService().Get(new MemoryCheckApi());
            }
            catch (Exception e)
            {
                error = (error == null ? "" : error + " | ") + "memory: " + e.Message;
            }

            try
            {
                var known = new[]
                {
                    "MetadataEnhanceTask", "IntroDbMarkerTask", "DoubanRatingTask",
                    "MetadataCacheRestoreTask", "MetadataCacheCleanupTask",
                    "MarkPreviousEpisodesPlayedTask", "MemoryWatchdogTask"
                };
                taskCount = Plugin.TaskManager?.ScheduledTasks?
                    .Count(w => known.Contains(w.ScheduledTask?.Key) &&
                                w.ScheduledTask.GetType().Assembly == typeof(HealthService).Assembly) ?? 0;
            }
            catch (Exception e)
            {
                error = (error == null ? "" : error + " | ") + "tasks: " + e.Message;
            }

            return new
            {
                ok = error == null && pinyin && traditional,
                version = Plugin.Instance?.Version?.ToString(),
                dependencies = new { pinyin, traditional, conflictPlugins = conflict },
                tasksRegistered = taskCount,
                cache,
                memory,
                error
            };
        }
    }
}
