using System;
using System.Reflection;

namespace StrmAssistant.Common.Hooks
{
    /// <summary>
    /// 统一封装「用反射调 Harmony」的样板代码。
    ///
    /// 关键点：`Harmony.Patch` 有多个重载，参数依次是
    /// (original, prefix, postfix, transpiler, finalizer, ...)。
    /// 如果只按"第二个参数是 HarmonyMethod"去挑重载，会把 **postfix 当成 prefix 传进去**，
    /// 表现为"补丁装上了但行为不对/根本没执行"。这里显式区分。
    /// </summary>
    internal static class HarmonyHelper
    {
        internal static string PatchPostfix(Type targetType, string methodName, MethodInfo postfix,
            string harmonyId, out int patched) =>
            PatchPostfix(targetType, methodName, postfix, harmonyId, null, out patched);

        /// <summary>同上，但只补丁 <paramref name="filter"/> 选中的重载。</summary>
        internal static string PatchPostfix(Type targetType, string methodName, MethodInfo postfix,
            string harmonyId, Func<MethodInfo, bool> filter, out int patched)
        {
            patched = 0;

            var harmonyType = Type.GetType("HarmonyLib.Harmony, 0Harmony");
            if (harmonyType == null) return "没有 0Harmony.dll";
            if (targetType == null) return "目标类型不存在";

            var harmonyMethodType = harmonyType.Assembly.GetType("HarmonyLib.HarmonyMethod");
            if (harmonyMethodType == null) return "HarmonyMethod 缺失";

            // 找 (MethodBase, HarmonyMethod prefix, HarmonyMethod postfix, ...) 这个重载
            MethodInfo patchMethod = null;

            foreach (var m in harmonyType.GetMethods())
            {
                if (m.Name != "Patch") continue;

                var ps = m.GetParameters();
                if (ps.Length >= 3 &&
                    ps[0].ParameterType == typeof(MethodBase) &&
                    ps[1].ParameterType == harmonyMethodType &&
                    ps[2].ParameterType == harmonyMethodType)
                {
                    patchMethod = m;
                    break;
                }
            }

            if (patchMethod == null) return "找不到 Patch(original, prefix, postfix, …) 重载";

            var harmony = Activator.CreateInstance(harmonyType, harmonyId);
            var postfixWrapper = Activator.CreateInstance(harmonyMethodType, postfix);

            var targets = targetType.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                                BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var target in targets)
            {
                if (target.Name != methodName) continue;
                if (filter != null && !filter(target)) continue;

                var args = new object[patchMethod.GetParameters().Length];
                args[0] = target;
                args[1] = null;              // prefix
                args[2] = postfixWrapper;    // postfix
                for (var i = 3; i < args.Length; i++) args[i] = null;

                patchMethod.Invoke(harmony, args);
                patched++;
            }

            var patchedList = "";
            try
            {
                var getPatched = harmonyType.GetMethod("GetPatchedMethods");
                if (getPatched != null)
                {
                    var enumerable = getPatched.Invoke(harmony, null) as System.Collections.IEnumerable;
                    var names = new System.Collections.Generic.List<string>();
                    if (enumerable != null)
                        foreach (var item in enumerable)
                            names.Add(item is MethodBase mb ? (mb.DeclaringType?.Name + "." + mb.Name) : item.ToString());

                    patchedList = " | Harmony 自报已补丁: " + names.Count + " 个 [" + string.Join(",", names) + "]";
                }
            }
            catch (Exception e)
            {
                patchedList = " | GetPatchedMethods 失败: " + e.Message;
            }

            return (patched > 0 ? "ok (" + patched + " 个重载)" : "没有匹配的方法: " + methodName) + patchedList;
        }

        /// <summary>装 prefix 补丁（返回 false 可跳过原方法）。只补丁有方法体、非抽象的重载。</summary>
        internal static string PatchPrefix(Type targetType, string methodName, MethodInfo prefix,
            string harmonyId, out int patched)
        {
            patched = 0;

            var harmonyType = Type.GetType("HarmonyLib.Harmony, 0Harmony");
            if (harmonyType == null) return "没有 0Harmony.dll";
            if (targetType == null) return "目标类型不存在";

            var harmonyMethodType = harmonyType.Assembly.GetType("HarmonyLib.HarmonyMethod");
            if (harmonyMethodType == null) return "HarmonyMethod 缺失";

            MethodInfo patchMethod = null;
            foreach (var m in harmonyType.GetMethods())
            {
                if (m.Name != "Patch") continue;
                var ps = m.GetParameters();
                if (ps.Length >= 3 && ps[0].ParameterType == typeof(MethodBase) &&
                    ps[1].ParameterType == harmonyMethodType && ps[2].ParameterType == harmonyMethodType)
                { patchMethod = m; break; }
            }

            if (patchMethod == null) return "找不到 Patch(original, prefix, postfix, …) 重载";

            var harmony = Activator.CreateInstance(harmonyType, harmonyId);
            var prefixWrapper = Activator.CreateInstance(harmonyMethodType, prefix);
            var failures = new System.Collections.Generic.List<string>();

            foreach (var target in targetType.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                                         BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (target.Name != methodName) continue;
                if (target.IsAbstract || target.GetMethodBody() == null) continue;

                try
                {
                    var args = new object[patchMethod.GetParameters().Length];
                    args[0] = target;
                    args[1] = prefixWrapper;
                    args[2] = null;
                    for (var i = 3; i < args.Length; i++) args[i] = null;
                    patchMethod.Invoke(harmony, args);
                    patched++;
                }
                catch (Exception ex)
                {
                    var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                    failures.Add(target.Name + "/" + target.GetParameters().Length + ": " + inner.Message);
                }
            }

            var msg = patched > 0 ? "ok (" + patched + " 个重载)" : "没有匹配的方法: " + methodName;
            return failures.Count > 0 ? msg + " | 失败: " + string.Join(";", failures) : msg;
        }


    }
}
