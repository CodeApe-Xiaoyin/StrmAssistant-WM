using System;
using System.Linq;
using System.Threading;
using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class PluginUpdateProbeService : IService
    {
        public object Get(PluginUpdateProbeApi request)
        {
            var manager = Plugin.InstallationManager;
            if (manager == null)
                return new { ok = false, error = "IInstallationManager 未注入" };

            try
            {
                var current = Plugin.Instance?.Version ?? new Version(4, 10, 0, 0);
                var updates = manager.GetAvailablePluginUpdates(current, CancellationToken.None)
                    .GetAwaiter().GetResult();

                var suppressConfig = Plugin.Instance?.GetPluginOptions()?.ExperienceEnhanceOptions?.SuppressPluginUpdates;

                return new
                {
                    ok = true,
                    count = updates?.Length ?? 0,
                    names = updates?.Select(u => u.name).ToArray() ?? Array.Empty<string>(),
                    suppressConfig = suppressConfig ?? string.Empty,
                    hookInstalled = StrmAssistant.Common.Hooks.PluginUpdatePatches.Installed,
                    managerType = manager.GetType().FullName,
                    managerAssembly = manager.GetType().Assembly.FullName,
                    managerAlc = System.Runtime.Loader.AssemblyLoadContext
                        .GetLoadContext(manager.GetType().Assembly)?.Name ?? "(default)",
                    pluginAlc = System.Runtime.Loader.AssemblyLoadContext
                        .GetLoadContext(typeof(Plugin).Assembly)?.Name ?? "(default)",
                    sameTypeInstance = ReferenceEquals(
                        manager.GetType(),
                        Type.GetType("Emby.Server.Implementations.Updates.InstallationManager, Emby.Server.Implementations"))
                };
            }
            catch (Exception e)
            {
                return new { ok = false, error = e.GetBaseException().Message };
            }
        }
    }
}
