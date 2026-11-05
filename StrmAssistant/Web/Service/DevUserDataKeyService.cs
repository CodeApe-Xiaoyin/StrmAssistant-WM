using System;
using System.Linq;
using System.Reflection;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class DevUserDataKeyService : IService
    {
        private readonly ILibraryManager _libraryManager;

        public DevUserDataKeyService(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
        }

        public object Get(DevUserDataKey request)
        {
            var ids = (request.Ids ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToArray();

            var videoType = typeof(BaseItem).Assembly.GetType("MediaBrowser.Controller.Entities.Video");
            var keyMethod = videoType?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetUserDataKey" && m.GetParameters().Length == 1);

            var rows = ids.Select(id =>
            {
                var item = _libraryManager.GetItemById(id);
                if (item == null) return new { id, found = false, name = "", library = "", key = "", error = "" };

                string key = "";
                string error = "";

                try
                {
                    if (keyMethod != null)
                        key = keyMethod.Invoke(null, new object[] { item }) as string ?? "";
                    else
                        error = "找不到静态 GetUserDataKey";
                }
                catch (Exception e)
                {
                    error = (e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e).Message;
                }

                return new
                {
                    id,
                    found = true,
                    name = item.Name,
                    library = item.GetTopParent()?.Name ?? "",
                    key,
                    error
                };
            }).ToArray();

            return new
            {
                optionEnabled = Plugin.Instance?.GetPluginOptions()?.ExperienceEnhanceOptions?.LibraryScopePlayProgress == true,
                hookInstalled = Common.Hooks.UserDataKeyPatches.Installed,
                distinctKeys = rows.Where(r => r.found && r.key.Length > 0).Select(r => r.key).Distinct().Count(),
                rows
            };
        }
    }
}
