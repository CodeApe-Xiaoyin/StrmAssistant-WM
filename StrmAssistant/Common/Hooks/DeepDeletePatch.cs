using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace StrmAssistant.Common.Hooks
{
    /// <summary>
    /// 「深度删除」：用户在 Emby 里删除媒体（连同文件一起删）时，
    /// 继续删掉 STRM 指向的本地文件 / 软链接指向的真实文件，以及同名的附属文件和删空的目录。
    ///
    /// 以前这个开关只作用于一个网页从没调用过的接口，正常删除流程里什么也不做。
    ///
    /// 只在这些条件同时满足时动手：开关打开、不是演练模式、这次删除带「删除文件」（DeleteFileLocation）。
    /// 扫库时清掉失效条目走的不是这条路（不带 DeleteFileLocation），不会误删。
    /// STRM 里是网址的不处理（网盘上的文件插件删不了）。
    /// </summary>
    public static class DeepDeletePatch
    {
        public static bool Installed { get; private set; }

        public static string TryInstall()
        {
            if (Installed) return "already";

            try
            {
                var target = Type.GetType("Emby.Server.Implementations.Library.LibraryManager, Emby.Server.Implementations");
                if (target == null) return "找不到 LibraryManager";

                var method = target.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "DeleteItem" && m.GetParameters().Length == 4 &&
                                         m.GetParameters()[1].ParameterType == typeof(DeleteOptions));
                if (method == null) return "找不到 DeleteItem(item, options, parent, notify)";

                var harmony = new HarmonyLib.Harmony("StrmAssistant.DeepDelete");
                harmony.Patch(method,
                    prefix: new HarmonyLib.HarmonyMethod(typeof(DeepDeletePatch).GetMethod(nameof(Prefix),
                        BindingFlags.NonPublic | BindingFlags.Static)),
                    postfix: new HarmonyLib.HarmonyMethod(typeof(DeepDeletePatch).GetMethod(nameof(Postfix),
                        BindingFlags.NonPublic | BindingFlags.Static)));

                Installed = true;
                return "ok";
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                return "失败: " + inner.GetType().Name + ": " + inner.Message;
            }
        }

        private static void Prefix(BaseItem item, DeleteOptions options, out List<string> __state)
        {
            __state = null;
            try
            {
                if (item == null || options == null || !options.DeleteFileLocation) return;
                if (Plugin.Instance?.CurrentOptions?.ExperienceEnhanceOptions?.EnableDeepDelete != true) return;

                var items = new List<BaseItem> { item };
                if (item is Folder folder)
                {
                    items.AddRange(folder.GetItemList(new InternalItemsQuery { Recursive = true, IsFolder = false }));
                }

                var targets = items.Select(i => ResolveTarget(i.Path))
                    .Where(t => t != null)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (targets.Count == 0) return;

                if (DryRunMode.Skip("深度删除目标文件 " + string.Join(", ", targets)))
                    return;

                __state = targets;
            }
            catch (Exception e)
            {
                Plugin.Instance?.Logger?.Info("DeepDelete - 收集目标失败: " + e.Message);
            }
        }

        private static void Postfix(List<string> __state)
        {
            if (__state == null) return;

            foreach (var target in __state)
            {
                try
                {
                    DeleteWithSidecars(target);
                }
                catch (Exception e)
                {
                    Plugin.Instance?.Logger?.Info("DeepDelete - 删除失败 " + target + ": " + e.Message);
                }
            }
        }

        /// <summary>条目路径对应的「真实文件」：本地 STRM 的目标，或软链接的目标；都不是返回 null。</summary>
        internal static string ResolveTarget(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            try
            {
                var info = new FileInfo(path);
                if (info.LinkTarget != null)
                {
                    var resolved = info.ResolveLinkTarget(true)?.FullName;
                    return IsDeletableTarget(resolved, path) ? resolved : null;
                }

                if (!path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase)) return null;

                var line = File.ReadLines(path).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
                var local = ParseLocalStrmTarget(line);
                return IsDeletableTarget(local, path) ? local : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>STRM 内容 → 本地绝对路径；网址（http/https 等）返回 null。</summary>
        internal static string ParseLocalStrmTarget(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;

            if (line.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                try { return new Uri(line).LocalPath; }
                catch { return null; }
            }

            if (line.Contains("://")) return null;

            return Path.IsPathRooted(line) ? line : null;
        }

        private static bool IsDeletableTarget(string target, string itemPath)
        {
            if (string.IsNullOrWhiteSpace(target)) return false;
            if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(itemPath), StringComparison.Ordinal)) return false;
            return File.Exists(target);
        }

        /// <summary>删目标文件、同目录下同名开头的附属文件（字幕、nfo、图片），目录删空了就把目录也删掉。</summary>
        private static void DeleteWithSidecars(string target)
        {
            if (!File.Exists(target)) return;

            var dir = Path.GetDirectoryName(target);
            var baseName = Path.GetFileNameWithoutExtension(target);

            File.Delete(target);
            Plugin.Instance?.Logger?.Info("DeepDelete - 已删除目标文件: " + target);

            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            foreach (var sidecar in Directory.GetFiles(dir))
            {
                var name = Path.GetFileName(sidecar);
                if (!IsSidecarOf(name, baseName)) continue;

                File.Delete(sidecar);
                Plugin.Instance?.Logger?.Info("DeepDelete - 已删除附属文件: " + sidecar);
            }

            if (!Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
                Plugin.Instance?.Logger?.Info("DeepDelete - 已删除空目录: " + dir);
            }
        }

        private static readonly HashSet<string> SidecarExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".srt", ".ass", ".ssa", ".sub", ".idx", ".vtt", ".sup", ".smi",
            ".nfo", ".jpg", ".jpeg", ".png", ".webp", ".json", ".bif"
        };

        /// <summary>「电影.zh.srt」「电影.nfo」「电影-poster.jpg」算「电影.mkv」的附属文件；「电影2.mkv」不算。</summary>
        internal static bool IsSidecarOf(string fileName, string baseName)
        {
            if (string.IsNullOrEmpty(baseName) || fileName.Length <= baseName.Length) return false;
            // 只删字幕 / nfo / 图片 / 媒体信息 JSON 这类附属文件，视频文件一律不碰（同目录可能是别的版本）
            if (!SidecarExtensions.Contains(Path.GetExtension(fileName))) return false;
            if (!fileName.StartsWith(baseName, StringComparison.Ordinal)) return false;
            var next = fileName[baseName.Length];
            return next == '.' || next == '-';
        }
    }
}
