using System;
using System.Net;
using System.Net.Http;

namespace StrmAssistant.Common.Hooks
{
    /// <summary>
    /// 全局代理的挂钩实现。
    ///
    /// 为什么要挂钩：Emby 自己创建 SocketsHttpHandler，实测不会吃 HttpClient.DefaultProxy
    /// （设置后它的出站请求仍然走直连），所以只能在 Handler 创建后把代理塞进去。
    /// </summary>
    public static class GlobalProxyPatches
    {
        private const string TargetType = "Emby.Server.Implementations.ApplicationHost";
        private const string TargetMethod = "CreateHttpClientHandler";

        public static bool Installed { get; private set; }

        public static string TryInstall()
        {
            if (Installed) return "already";

            try
            {
                var target = Type.GetType(TargetType + ", Emby.Server.Implementations");
                var postfix = typeof(GlobalProxyPatches).GetMethod(nameof(Postfix),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                var result = HarmonyHelper.PatchPostfix(target, TargetMethod, postfix,
                    "StrmAssistant.GlobalProxy", out var patched);

                Installed = patched > 0;
                return result;
            }
            catch (Exception e)
            {
                var inner = e is System.Reflection.TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                return "失败: " + inner.GetType().Name + ": " + inner.Message;
            }
        }

        /// <summary>在 Emby 创建 Handler 之后注入代理。</summary>
        private static void Postfix(ref HttpMessageHandler __result)
        {
            try
            {
                if (!ProxyConfig.GlobalEnabled) return;
                var uri = ProxyConfig.Current;
                if (uri == null) return;

                if (__result is SocketsHttpHandler sockets)
                {
                    sockets.UseProxy = true;
                    sockets.Proxy = ProxyConfig.CreateWebProxy(uri);
                    Plugin.Instance?.Logger?.Info("GlobalProxy - 已为新建 Handler 注入代理 " + uri);
                }
                else if (__result is HttpClientHandler classic)
                {
                    classic.UseProxy = true;
                    classic.Proxy = ProxyConfig.CreateWebProxy(uri);
                    Plugin.Instance?.Logger?.Info("GlobalProxy - 已为新建 Handler 注入代理（classic） " + uri);
                }
            }
            catch (Exception e)
            {
                Plugin.Instance?.Logger?.Info("GlobalProxy - 注入失败: " + e.Message);
            }
        }
    }
}
