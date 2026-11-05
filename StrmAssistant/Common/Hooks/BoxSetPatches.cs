using System;
using System.Linq;
using System.Reflection;
using MediaBrowser.Controller.Entities;

namespace StrmAssistant.Common.Hooks
{
    /// <summary>
    /// 「阻止合集自动创建」。
    ///
    /// Emby 在元数据里发现影片属于某个合集时，会调用
    /// `CollectionManager.AddCollection(BaseItem, string name)` 建 BoxSet。
    /// 打开选项后这里直接返回 false 跳过原方法，于是不会自动建合集。
    ///
    /// 只拦"自动创建"这一条路径；用户在界面里手动新建合集不受影响
    /// （那是 `CreateCollection`）。
    /// </summary>
    public static class BoxSetPatches
    {
        public static bool Installed { get; private set; }

        public static string TryInstall()
        {
            if (Installed) return "already";

            try
            {
                var target = Type.GetType("Emby.Server.Implementations.Collections.CollectionManager, Emby.Server.Implementations");
                if (target == null) return "找不到 CollectionManager";

                var prefix = typeof(BoxSetPatches).GetMethod(nameof(Prefix),
                    BindingFlags.NonPublic | BindingFlags.Static);

                var result = HarmonyHelper.PatchPrefix(target, "AddCollection", prefix,
                    "StrmAssistant.NoBoxsets", out var patched);

                Installed = patched > 0;

                return result;
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                return "失败: " + inner.GetType().Name + ": " + inner.Message;
            }
        }

        private static bool Prefix(BaseItem item, string collectionName)
        {
            try
            {
                var options = Plugin.Instance?.GetPluginOptions()?.ExperienceEnhanceOptions;

                if (options?.NoBoxsetsAutoCreation != true) return true;

                Plugin.Instance?.Logger?.Info("NoBoxsets - 已阻止自动创建合集: " + collectionName +
                                              "（条目 " + (item?.Name ?? "?") + "）");
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
