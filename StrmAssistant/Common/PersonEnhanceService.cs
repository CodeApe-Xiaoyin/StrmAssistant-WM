using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 移植自 PRO「演职人员增强 TMDB」：用 TMDb 人物资料补全 Person 条目。
    /// </summary>
    public class PersonEnhanceService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        public PersonEnhanceService(ILibraryManager libraryManager, ILogger logger)
        {
            _libraryManager = libraryManager;
            _logger = logger;
        }

        public async Task<bool> ApplyAsync(Person person, string tmdbId, CancellationToken token)
        {
            if (string.IsNullOrEmpty(tmdbId)) return false;

            var apiKey = CommonUtility.ResolveMovieDbApiKey();
            var url = $"https://api.themoviedb.org/3/person/{tmdbId}?api_key={apiKey}&language=zh-CN";

            var json = TmdbCache.TryGet(url);
            if (string.IsNullOrEmpty(json))
            {
                json = await Plugin.MetadataApi.GetMovieDbRawAsync(url, token).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(json)) TmdbCache.Put(url, json);
            }
            if (string.IsNullOrEmpty(json)) return false;

            Dto.TmdbPerson detail;
            try
            {
                detail = System.Text.Json.JsonSerializer.Deserialize<Dto.TmdbPerson>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return false;
            }

            if (detail == null) return false;

            var changed = false;

            if (!string.IsNullOrWhiteSpace(detail.Biography) &&
                !string.Equals(person.Overview, detail.Biography, StringComparison.Ordinal))
            {
                person.Overview = detail.Biography;
                changed = true;
            }

            // 生日/忌日按 UTC 零点存，避免本地时区换算导致日期偏移一天
            if (DateTime.TryParse(detail.Birthday, out var birthday))
            {
                var utcBirthday = DateTime.SpecifyKind(birthday.Date, DateTimeKind.Utc);
                if (person.PremiereDate != utcBirthday)
                {
                    person.PremiereDate = utcBirthday;
                    changed = true;
                }
            }

            if (DateTime.TryParse(detail.Deathday, out var deathday))
            {
                var utcDeathday = DateTime.SpecifyKind(deathday.Date, DateTimeKind.Utc);
                if (person.EndDate != utcDeathday)
                {
                    person.EndDate = utcDeathday;
                    changed = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(detail.PlaceOfBirth))
            {
                var places = new[] { detail.PlaceOfBirth };
                if (person.ProductionLocations == null ||
                    person.ProductionLocations.Length != 1 ||
                    person.ProductionLocations[0] != detail.PlaceOfBirth)
                {
                    person.ProductionLocations = places;
                    changed = true;
                }
            }

            if (changed)
            {
                if (DryRunMode.Skip("增强人物信息 " + person.Name, _logger)) return false;

                _libraryManager.UpdateItem(person, person.GetParent(), ItemUpdateType.MetadataEdit);
                _logger.Info("Person enhanced from TMDb: " + person.Name);
            }

            return changed;
        }
    }
}
