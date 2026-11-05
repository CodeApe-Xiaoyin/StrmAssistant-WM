using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace StrmAssistant.Common.Hooks
{
    /// <summary>
    /// Emby 的插件加载器不会解析插件目录里的附属程序集，
    /// 这里在模块初始化时登记 AssemblyLoadContext 解析器，
    /// 让 TinyPinyin 之类的依赖能从插件目录被找到。
    /// </summary>
    internal static class DependencyBootstrap
    {
        private static readonly string[] Preload =
        {
            "TinyPinyin.dll",
            "0Harmony.dll"
        };

        private static readonly List<string> SearchDirs = new List<string>();
        private static System.Runtime.Loader.AssemblyLoadContext _alc;

        [ModuleInitializer]
        internal static void Init()
        {
            // Emby 可能从字节流加载插件，这时 Assembly.Location 是空的，拿不到插件目录。
            // 以前只能靠写死的 /config/plugins 找到依赖（Docker 碰巧对），非 Docker 安装 Harmony 加载不到，所有挂钩失败。
            // 现在按顺序找：插件自身目录 → Emby 启动参数 -programdata 下的 plugins → Docker 默认路径；
            // Plugin 构造时再补上 Emby 报告的 PluginsPath（见 AddPluginsDirectory）。
            var location = typeof(Plugin).Assembly.Location;
            AddDir(string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location));
            AddDir(ProgramDataPluginsFromCommandLine());
            AddDir("/config/plugins");
            AddDir(Path.Combine(AppContext.BaseDirectory ?? "", "plugins"));

            _alc = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof(Plugin).Assembly);
            if (_alc != null)
            {
                _alc.Resolving += (context, name) =>
                {
                    try
                    {
                        string[] dirs;
                        lock (SearchDirs) dirs = SearchDirs.ToArray();
                        foreach (var d in dirs)
                        {
                            var candidate = Path.Combine(d, name.Name + ".dll");
                            if (File.Exists(candidate)) return context.LoadFromAssemblyPath(candidate);
                        }
                        return null;
                    }
                    catch
                    {
                        return null;
                    }
                };
            }

            PreloadAll();
        }

        /// <summary>Plugin 构造时调用：用 Emby 报告的插件目录再补一次（比命令行推断更准，Windows 也适用）。</summary>
        internal static void AddPluginsDirectory(string pluginsPath)
        {
            if (AddDir(pluginsPath)) PreloadAll();
        }

        /// <summary>登记一个插件目录：它的 StrmAssistantDeps 子目录和它本身。返回是否新增。</summary>
        private static bool AddDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return false;
            var added = false;
            lock (SearchDirs)
            {
                foreach (var d in new[] { Path.Combine(dir, "StrmAssistantDeps"), dir })
                {
                    if (SearchDirs.Contains(d)) continue;
                    SearchDirs.Add(d);
                    added = true;
                }
            }

            return added;
        }

        private static string ProgramDataPluginsFromCommandLine()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                for (var i = 0; i < args.Length - 1; i++)
                {
                    if (string.Equals(args[i], "-programdata", StringComparison.OrdinalIgnoreCase))
                        return Path.Combine(args[i + 1], "plugins");
                }
            }
            catch
            {
                // 拿不到命令行就跳过
            }

            return null;
        }

        private static void PreloadAll()
        {
            string[] dirs;
            lock (SearchDirs) dirs = SearchDirs.ToArray();

            foreach (var file in Preload)
            {
                try
                {
                    var simple = Path.GetFileNameWithoutExtension(file);
                    if (_alc != null && System.Linq.Enumerable.Any(_alc.Assemblies, a => a.GetName().Name == simple)) continue;

                    foreach (var d in dirs)
                    {
                        var candidate = Path.Combine(d, file);
                        if (!File.Exists(candidate)) continue;
                        _alc?.LoadFromAssemblyPath(candidate);
                        break;
                    }
                }
                catch
                {
                    // 预加载失败不阻断插件启动
                }
            }
        }
    }
}
