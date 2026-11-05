using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class DevTypeProbeService : IService
    {
        public object Get(DevTypeProbe request)
        {
            var name = string.IsNullOrWhiteSpace(request.Name)
                ? "MediaBrowser.Controller.Entities.Video"
                : request.Name.Trim();

            var controllerAssembly = typeof(BaseItem).Assembly;

            // 允许指定其它程序集（例如 Emby.Server.Implementations）
            if (!string.IsNullOrWhiteSpace(request.Assembly) &&
                !string.Equals(request.Assembly, "MediaBrowser.Controller", StringComparison.OrdinalIgnoreCase))
            {
                var wanted = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, request.Assembly, StringComparison.OrdinalIgnoreCase));

                if (wanted == null)
                {
                    try { wanted = Assembly.Load(request.Assembly); } catch { }
                }

                if (wanted != null) controllerAssembly = wanted;
            }

            // 只用类型所在程序集解析，避免落到别的加载上下文里的同名类型
            var type = controllerAssembly.GetType(name);

            var info = new
            {
                controllerAssembly = controllerAssembly.FullName,
                controllerLocation = SafeLocation(controllerAssembly),
                controllerAlc = AssemblyLoadContext.GetLoadContext(controllerAssembly)?.Name ?? "(default)",
                pluginAssembly = typeof(Plugin).Assembly.FullName,
                pluginAlc = AssemblyLoadContext.GetLoadContext(typeof(Plugin).Assembly)?.Name ?? "(default)",
                resolvedFrom = "controller",
                typeFound = type != null,
                typeAssembly = type?.Assembly.FullName,
                typeAlc = type == null ? null : (AssemblyLoadContext.GetLoadContext(type.Assembly)?.Name ?? "(default)"),
                baseChain = type == null ? Array.Empty<string>() : BaseChain(type),
                methods = type == null
                    ? Array.Empty<string>()
                    : type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                      BindingFlags.Static | BindingFlags.DeclaredOnly)
                        .Where(m => string.IsNullOrWhiteSpace(request.Method) || m.Name == request.Method)
                        .Select(m => m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) +
                                     ") -> " + m.ReturnType.Name +
                                     " | static=" + m.IsStatic + " virtual=" + m.IsVirtual +
                                     " abstract=" + m.IsAbstract + " body=" + (m.GetMethodBody() != null))
                        .OrderBy(x => x)
                        .ToArray()
            };

            return info;
        }

        private static string SafeLocation(Assembly assembly)
        {
            try { return assembly.Location; }
            catch { return "(unavailable)"; }
        }

        private static string[] BaseChain(Type type)
        {
            var chain = new System.Collections.Generic.List<string>();
            var current = type;

            while (current != null)
            {
                chain.Add(current.FullName + " @" + (AssemblyLoadContext.GetLoadContext(current.Assembly)?.Name ?? "(default)"));
                current = current.BaseType;
            }

            return chain.ToArray();
        }
    }
}
