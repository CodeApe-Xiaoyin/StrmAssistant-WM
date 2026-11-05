using System;
using System.Linq;
using System.Reflection;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;

namespace StrmAssistant.Common.Hooks
{
    /// <summary>
    /// 「自动合并多版本 → 剧集：跨媒体库合并」。
    ///
    /// Emby 开了「自动合并剧集」后，用 provider id 当剧集的 PresentationUniqueKey，
    /// 但会在末尾拼上所属媒体库的 id（Series.AddLibrariesToPresentationUniqueKey），
    /// 所以同一部剧放在两个库里永远是两个条目。
    /// 选择跨库合并时，这里把末尾的媒体库 id 去掉，同一 provider id 的剧集在各库之间共用一个 key，
    /// 再由 MergeMultiVersionTask 刷新这些剧集，Emby 就会把它们合成一部。
    ///
    /// 以前这个移植版没有这个补丁（IsModSupported 写死 false），界面上的「跨媒体库合并」对剧集不起作用。
    /// </summary>
    public static class SeriesMergePatch
    {
        public static bool Installed { get; private set; }

        public static string TryInstall()
        {
            if (Installed) return "already";

            try
            {
                var postfix = typeof(SeriesMergePatch).GetMethod(nameof(Postfix),
                    BindingFlags.NonPublic | BindingFlags.Static);

                var r = HarmonyHelper.PatchPostfix(typeof(Series), "AddLibrariesToPresentationUniqueKey", postfix,
                    "StrmAssistant.SeriesMerge", out var patched);

                Installed = patched > 0;
                return r;
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                return "失败: " + inner.GetType().Name + ": " + inner.Message;
            }
        }

        private static void Postfix(BaseItem[] collectionFolders, ref string __result)
        {
            try
            {
                var options = Plugin.Instance?.CurrentOptions?.ExperienceEnhanceOptions;
                if (options?.MergeMultiVersion != true ||
                    options.MergeSeriesPreference != Options.ExperienceEnhanceOptions.MergeSeriesScopeOption.GlobalScope)
                    return;

                // 4.10 拼 key 用 IdString，4.8/4.9 用 Id.ToString("N")，两者内容相同
                __result = StripLibraries(__result, collectionFolders?.Select(i => i.IdStringCompat()).ToArray());
            }
            catch (Exception e)
            {
                Plugin.Instance?.Logger?.Debug("SeriesMerge - 处理失败: " + e.Message);
            }
        }

        /// <summary>去掉 key 末尾的「-库id-库id」（纯函数，便于单测）。</summary>
        public static string StripLibraries(string key, string[] libraryIds)
        {
            if (string.IsNullOrEmpty(key) || libraryIds == null || libraryIds.Length == 0) return key;
            var suffix = "-" + string.Join("-", libraryIds);
            return key.EndsWith(suffix, StringComparison.Ordinal) ? key.Substring(0, key.Length - suffix.Length) : key;
        }
    }
}
