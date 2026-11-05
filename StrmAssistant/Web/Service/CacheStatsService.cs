using MediaBrowser.Model.Services;
using StrmAssistant.Common;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class CacheStatsService : IService
    {
        public object Get(CacheStatsApi request) => TmdbCache.GetStats();
    }
}
