using System;

namespace StrmAssistant.Common.Hooks
{
    /// <summary>
    /// 挂钩能力自检：**真的**尝试打一个补丁，而不是返回固定文案。
    /// 用 Harmony 对 Probe() 打 Prefix，看能不能把返回值改掉。
    /// </summary>
    public static class HookSelfTest
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static string Probe() => "original";

        public static string Run()
        {
            try
            {
                var harmonyType = Type.GetType("HarmonyLib.Harmony, 0Harmony")
                                  ?? Type.GetType("HarmonyLib.Harmony, Lib.Harmony");

                if (harmonyType == null)
                {
                    return "UNAVAILABLE: 未找到 HarmonyLib.Harmony（0Harmony.dll 未被解析）";
                }

                var harmony = Activator.CreateInstance(harmonyType, "StrmAssistant.SelfTest");

                var probe = typeof(HookSelfTest).GetMethod(nameof(Probe),
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                var prefix = typeof(HookSelfTest).GetMethod(nameof(Prefix),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                var harmonyMethodType = harmonyType.Assembly.GetType("HarmonyLib.HarmonyMethod");

                if (harmonyMethodType == null) return "UNAVAILABLE: HarmonyMethod 类型缺失";

                var harmonyMethod = Activator.CreateInstance(harmonyMethodType, prefix);

                System.Reflection.MethodInfo patch = null;
                foreach (var m in harmonyType.GetMethods())
                {
                    if (m.Name != "Patch") continue;
                    var ps = m.GetParameters();
                    if (ps.Length >= 2 && ps[1].ParameterType == harmonyMethodType)
                    {
                        patch = m;
                        break;
                    }
                }

                if (patch == null) return "UNAVAILABLE: 未找到 Patch(MethodBase, HarmonyMethod) 重载";

                var args = new object[patch.GetParameters().Length];
                args[0] = probe;
                args[1] = harmonyMethod;
                for (var i = 2; i < args.Length; i++) args[i] = null;

                patch.Invoke(harmony, args);

                // 通过反射调用，避免 JIT 把调用点内联（内联后补丁看不到效果）
                var after = (string) probe.Invoke(null, null);
                return after == "patched"
                    ? "OK: Harmony 补丁生效（Probe 返回 patched），本环境支持方法级 detour"
                    : "PARTIAL: 补丁调用未抛异常，但 Probe 仍返回 " + after;
            }
            catch (Exception e)
            {
                var inner = e is System.Reflection.TargetInvocationException tie && tie.InnerException != null
                    ? tie.InnerException
                    : e;

                var root = inner;
                while (root.InnerException != null) root = root.InnerException;

                return "FAILED: " + inner.GetType().Name + ": " + inner.Message +
                       (root != inner ? " | root: " + root.GetType().Name + ": " + root.Message : "");
            }
        }

        private static bool Prefix(ref string __result)
        {
            __result = "patched";
            return false;
        }
    }
}
