using System;
using System.Linq;
using System.Reflection;
using MediaBrowser.Controller.Entities;

namespace StrmAssistant.Common.Hooks
{
    /// <summary>
    /// 「播放进度按库分离」。
    ///
    /// Emby 的观看进度是按 <c>GetUserDataKey()</c> 共享的；`Video` 上有三个**静态** helper
    /// （`GetUserDataKey(BaseItem)` 等）用 provider id 推导 key。
    /// 打开选项后给 key 追加所属库 id：不同库各自独立，同库内仍共享。
    /// </summary>
    public static class UserDataKeyPatches
    {
        public static bool Installed { get; private set; }

        public static string TryInstall()
        {
            if (Installed) return "already";

            var results = new System.Collections.Generic.List<string>();

            try
            {
                var harmonyType = Type.GetType("HarmonyLib.Harmony, 0Harmony");
                if (harmonyType == null) return "没有 0Harmony.dll";

                var harmonyMethodType = harmonyType.Assembly.GetType("HarmonyLib.HarmonyMethod");
                if (harmonyMethodType == null) return "HarmonyMethod 缺失";

                MethodInfo patchMethod = null;
                foreach (var m in harmonyType.GetMethods())
                {
                    if (m.Name != "Patch") continue;
                    var ps = m.GetParameters();
                    if (ps.Length >= 3 && ps[0].ParameterType == typeof(MethodBase) &&
                        ps[1].ParameterType == harmonyMethodType && ps[2].ParameterType == harmonyMethodType)
                    {
                        patchMethod = m;
                        break;
                    }
                }

                if (patchMethod == null) return "找不到 Patch(original, prefix, postfix, …) 重载";

                var harmony = Activator.CreateInstance(harmonyType, "StrmAssistant.UserDataKey");
                var postfix = typeof(UserDataKeyPatches).GetMethod(nameof(Postfix),
                    BindingFlags.NonPublic | BindingFlags.Static);
                var postfixWrapper = Activator.CreateInstance(harmonyMethodType, postfix);

                var videoType = typeof(BaseItem).Assembly.GetType("MediaBrowser.Controller.Entities.Video");
                if (videoType == null) return "找不到 Video 类型";

                var patched = 0;

                foreach (var m in videoType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                       BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (m.Name != "GetUserDataKey" || m.ReturnType != typeof(string)) continue;
                    if (m.GetParameters().Length == 0) continue;
                    if (m.GetParameters()[0].ParameterType != typeof(BaseItem)) continue;

                    try
                    {
                        var args = new object[patchMethod.GetParameters().Length];
                        args[0] = m;
                        args[1] = null;
                        args[2] = postfixWrapper;
                        for (var i = 3; i < args.Length; i++) args[i] = null;

                        patchMethod.Invoke(harmony, args);
                        patched++;
                    }
                    catch (Exception ex)
                    {
                        var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                        results.Add("失败 " + m.Name + "(" + m.GetParameters().Length + " 参): " + inner.Message);
                    }
                }

                Installed = patched > 0;
                results.Add("已补丁 " + patched + " 个静态 GetUserDataKey");
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                return "失败: " + inner.GetType().Name + ": " + inner.Message;
            }

            return string.Join(" | ", results);
        }

        private static void Postfix(BaseItem __0, ref string __result)
        {
            try
            {
                var options = Plugin.Instance?.GetPluginOptions()?.ExperienceEnhanceOptions;
                if (options?.LibraryScopePlayProgress != true) return;
                if (__0 == null || string.IsNullOrEmpty(__result)) return;

                // 三个静态重载会互相调用，这里保证只追加一次
                if (__result.Contains("|lib:")) return;

                var libraryId = __0.GetTopParent()?.InternalId.ToString();
                if (string.IsNullOrEmpty(libraryId)) return;

                __result = __result + "|lib:" + libraryId;
            }
            catch
            {
                // 计算失败保持原值，不影响播放
            }
        }
    }
}
