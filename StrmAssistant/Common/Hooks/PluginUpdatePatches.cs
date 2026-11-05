using System;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Model.Updates;

namespace StrmAssistant.Common.Hooks
{
    /// <summary>
    /// 「抑制指定插件更新」的挂钩实现：
    /// 在 Emby 返回可用更新列表后，把用户配置里点名的插件过滤掉。
    /// </summary>
    public static class PluginUpdatePatches
    {
        public static bool Installed { get; private set; }

        public static string TryInstall()
        {
            if (Installed) return "already";

            try
            {
                var target = Type.GetType("Emby.Server.Implementations.Updates.InstallationManager, Emby.Server.Implementations");
                var postfix = typeof(PluginUpdatePatches).GetMethod(nameof(Postfix),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                var result = HarmonyHelper.PatchPostfix(target, "GetAvailablePluginUpdates", postfix,
                    "StrmAssistant.PluginUpdates", out var patched);

                Installed = patched > 0;
                return result;
            }
            catch (Exception e)
            {
                var inner = e is System.Reflection.TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                return "失败: " + inner.GetType().Name + ": " + inner.Message;
            }
        }

        private static void Postfix(ref Task<PackageVersionInfo[]> __result)
        {
            try
            {
                var config = Plugin.Instance?.GetPluginOptions()?.ExperienceEnhanceOptions?.SuppressPluginUpdates;
                var names = Common.PluginUpdateFilter.ParseNames(config);
                if (names.Length == 0 || __result == null) return;

                var original = __result;
                __result = FilterAsync(original, config);
            }
            catch (Exception e)
            {
                Plugin.Instance?.Logger?.Info("PluginUpdateFilter - 挂钩执行失败: " + e.Message);
            }
        }

        private static async Task<PackageVersionInfo[]> FilterAsync(Task<PackageVersionInfo[]> original, string config)
        {
            var packages = await original.ConfigureAwait(false);
            if (packages == null) return packages;

            var filtered = Common.PluginUpdateFilter.Apply(packages, config);

            Plugin.Instance?.Logger?.Info("PluginUpdateFilter - 挂钩已执行：收到 " + packages.Length +
                                          " 个更新，抑制名单 " +
                                          Common.PluginUpdateFilter.ParseNames(config).Length + " 项，过滤后 " +
                                          filtered.Length + " 个");

            if (filtered.Length != packages.Length)
            {
                var removed = packages.Where(p => !filtered.Contains(p)).Select(p => p.name);
                Plugin.Instance?.Logger?.Info("PluginUpdateFilter - 已抑制 " + (packages.Length - filtered.Length) +
                                              " 个插件更新: " + string.Join(",", removed));
            }

            return filtered;
        }
    }
}
