using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    public class MediaInfoApi
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IMediaSourceManager _mediaSourceManager;
        private readonly IItemRepository _itemRepository;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IFileSystem _fileSystem;
        private readonly IProviderManager _providerManager;

        private const string MediaInfoFileExtension = "-mediainfo.json";

        private readonly bool _fallbackApproach;
        private readonly MethodInfo _getStaticMediaSources;

        internal class MediaSourceWithChapters
        {
            public MediaSourceInfo MediaSourceInfo { get; set; }
            public List<ChapterInfo> Chapters { get; set; } = new List<ChapterInfo>();
            public bool? ZeroFingerprintConfidence { get; set; }
            public string EmbeddedImage { get; set; }
        }

        public MediaInfoApi(ILibraryManager libraryManager, IFileSystem fileSystem, IProviderManager providerManager,
            IMediaSourceManager mediaSourceManager, IItemRepository itemRepository, IJsonSerializer jsonSerializer,
            ILibraryMonitor libraryMonitor)
        {
            _logger = Plugin.Instance.Logger;
            _libraryManager = libraryManager;
            _fileSystem = fileSystem;
            _providerManager = providerManager;
            _mediaSourceManager = mediaSourceManager;
            _itemRepository = itemRepository;
            _jsonSerializer = jsonSerializer;

            if (Plugin.Instance.ApplicationHost.ApplicationVersion >= new Version("4.9.0.25"))
            {
                try
                {
                    _getStaticMediaSources = mediaSourceManager.GetType()
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                        .Where(m => m.Name == "GetStaticMediaSources")
                        .OrderByDescending(m => m.GetParameters().Length)
                        .FirstOrDefault(m =>
                        {
                            var parameters = m.GetParameters();
                            return parameters.Length >= 7 &&
                                   parameters[0].ParameterType == typeof(BaseItem) &&
                                   parameters.Any(p => p.ParameterType == typeof(LibraryOptions));
                        });
                    _fallbackApproach = true;
                }
                catch (Exception e)
                {
                    if (Plugin.Instance.DebugMode)
                    {
                        _logger.Debug(e.Message);
                        _logger.Debug(e.StackTrace);
                    }
                }

                if (_getStaticMediaSources is null)
                {
                    _logger.Warn($"{nameof(MediaInfoApi)} Init Failed");
                }
            }

            try
            {
                var embyServerImplementationsAssembly = Assembly.Load("Emby.Server.Implementations");
                var libraryMonitorImpl =
                    embyServerImplementationsAssembly.GetType("Emby.Server.Implementations.IO.LibraryMonitor");
                if (!TryAppendStringArrayField(libraryMonitorImpl, libraryMonitor, "_alwaysIgnoreExtensions", ".json"))
                {
                    TryAppendStringArrayField(libraryMonitorImpl, libraryMonitor, "_alwaysIgnoreSubstrings",
                        MediaInfoFileExtension);
                }
            }
            catch (Exception e)
            {
                if (Plugin.Instance.DebugMode)
                {
                    _logger.Debug(e.Message);
                    _logger.Debug(e.StackTrace);
                }

                _logger.Warn($"{nameof(MediaInfoApi)} Init Failed");
            }
        }

        private List<MediaSourceInfo> GetStaticMediaSourcesByApi(BaseItem item, bool enableAlternateMediaSources,
            LibraryOptions libraryOptions)
        {
#if EMBY48
            return _mediaSourceManager.GetStaticMediaSources(item, enableAlternateMediaSources, false,
                libraryOptions, null, null);
#else
            return _mediaSourceManager.GetStaticMediaSources(item, enableAlternateMediaSources, false, false,
                null, libraryOptions, null, null);
#endif
        }

        private List<MediaSourceInfo> GetStaticMediaSourcesByRef(BaseItem item, bool enableAlternateMediaSources,
            LibraryOptions libraryOptions)
        {
            var parameters = _getStaticMediaSources.GetParameters();
            var collectionFolders = (BaseItem[])_libraryManager.GetCollectionFolders(item);

            object[] args;
            switch (parameters.Length)
            {
                case 10:
                    args = new object[]
                    {
                        item, enableAlternateMediaSources, false, true, true, collectionFolders, libraryOptions, null,
                        null, CancellationToken.None
                    };
                    break;
                case 8:
                    args = new object[]
                    {
                        item, enableAlternateMediaSources, false, true, collectionFolders, libraryOptions, null, null
                    };
                    break;
                case 7:
                    args = new object[] { item, enableAlternateMediaSources, false, true, libraryOptions, null, null };
                    break;
                default:
                    args = new object[] { item, false, true, null, null };
                    break;
            }

            return (List<MediaSourceInfo>)_getStaticMediaSources.Invoke(_mediaSourceManager, args);
        }

        private static bool TryAppendStringArrayField(Type targetType, object target, string fieldName, string value)
        {
            var field = targetType?.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (!(field?.GetValue(target) is string[] currentArray)) return false;
            if (currentArray.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase))) return true;

            var newArray = new string[currentArray.Length + 1];
            Array.Copy(currentArray, newArray, currentArray.Length);
            newArray[newArray.Length - 1] = value;
            field.SetValue(target, newArray);
            return true;
        }

        public List<MediaSourceInfo> GetStaticMediaSources(BaseItem item, bool enableAlternateMediaSources)
        {
            var options = _libraryManager.GetLibraryOptions(item);

            return !_fallbackApproach
                ? GetStaticMediaSourcesByApi(item, enableAlternateMediaSources, options)
                : GetStaticMediaSourcesByRef(item, enableAlternateMediaSources, options);
        }

        public MetadataRefreshOptions GetMediaInfoRefreshOptions()
        {
            return new MetadataRefreshOptions(new DirectoryService(_logger, _fileSystem))
            {
                EnableRemoteContentProbe = true,
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllMetadata = false,
                ImageRefreshMode = MetadataRefreshMode.ValidationOnly,
                ReplaceAllImages = false,
                EnableThumbnailImageExtraction = false,
                EnableSubtitleDownloading = false
            };
        }

        public static string GetMediaInfoJsonPath(BaseItem item)
        {
            var jsonRootFolder = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.MediaInfoJsonRootFolder;

            var relativePath = item.ContainingFolderPath;
            if (!string.IsNullOrEmpty(jsonRootFolder) && Path.IsPathRooted(item.ContainingFolderPath))
            {
                relativePath = Path.GetRelativePath(Path.GetPathRoot(item.ContainingFolderPath)!,
                    item.ContainingFolderPath);
            }

            var mediaInfoJsonPath = !string.IsNullOrEmpty(jsonRootFolder)
                ? Path.Combine(jsonRootFolder, relativePath, item.FileNameWithoutExtension + MediaInfoFileExtension)
                : Path.Combine(item.ContainingFolderPath!, item.FileNameWithoutExtension + MediaInfoFileExtension);

            return mediaInfoJsonPath;
        }

        public bool MediaInfoJsonExists(BaseItem item, IDirectoryService directoryService)
        {
            var file = directoryService.GetFile(ResolveExistingMediaInfoJsonPath(item, directoryService));

            return file?.Exists == true;
        }

        /// <summary>
        /// 读取时用：根目录下没有，就回退到 strm 旁边的 JSON（老配置根目录留空时写在那里）。
        /// 写入始终用 <see cref="GetMediaInfoJsonPath"/>。
        /// </summary>
        public static string ResolveExistingMediaInfoJsonPath(BaseItem item, IDirectoryService directoryService)
        {
            var path = GetMediaInfoJsonPath(item);
            if (directoryService.GetFile(path)?.Exists == true) return path;

            if (!string.IsNullOrEmpty(item.ContainingFolderPath))
            {
                var sidecar = Path.Combine(item.ContainingFolderPath, item.FileNameWithoutExtension + MediaInfoFileExtension);
                if (!string.Equals(sidecar, path, StringComparison.Ordinal) && directoryService.GetFile(sidecar)?.Exists == true)
                    return sidecar;
            }

            return path;
        }

        private async Task<bool> SerializeMediaInfo(BaseItem item, IDirectoryService directoryService, bool overwrite,
            string source)
        {
            // 演练模式：不写文件
            if (DryRunMode.Skip("序列化媒体信息 " + item?.Name, _logger)) return false;

            var mediaInfoJsonPath = GetMediaInfoJsonPath(item);
            var file = directoryService.GetFile(mediaInfoJsonPath);

            if (overwrite || file?.Exists != true)
            {
                try
                {
                    var options = _libraryManager.GetLibraryOptions(item);
                    var mediaSources = item.GetMediaSources(false, false, options);
                    var chapters = _itemRepository.GetChapters(item);
                    var mediaSourcesWithChapters = mediaSources.Select(mediaSource =>
                            new MediaSourceWithChapters
                                { MediaSourceInfo = mediaSource, Chapters = chapters })
                        .ToList();

                    foreach (var jsonItem in mediaSourcesWithChapters)
                    {
                        jsonItem.MediaSourceInfo.Id = null;
                        jsonItem.MediaSourceInfo.ItemId = null;
                        jsonItem.MediaSourceInfo.Path = null;

                        foreach (var subtitle in jsonItem.MediaSourceInfo.MediaStreams.Where(m =>
                                     m.IsExternal && m.Type == MediaStreamType.Subtitle &&
                                     m.Protocol == MediaProtocol.File))
                        {
                            subtitle.Path = _fileSystem.GetFileInfo(subtitle.Path).Name;
                        }

                        var clearEmbedded = Plugin.Instance?.GetPluginOptions()?.MediaInfoExtractOptions?.ClearEmbeddedMetadata == true;

                        if (clearEmbedded)
                        {
                            // 只丢掉普通章节（内嵌章节 + 章节图），片头/片尾标记必须保留：
                            // 以前这里整个清空，删库重加后片头片尾标记无法从 JSON 还原（生产实测 恶缘 6 集各 3 个标记全丢）。
                            jsonItem.Chapters = (jsonItem.Chapters ?? new List<ChapterInfo>())
                                .Where(c => c.MarkerType != MarkerType.Chapter)
                                .ToList();
                            foreach (var chapter in jsonItem.Chapters) chapter.ImageTag = null;
                            jsonItem.EmbeddedImage = null;
                        }
                        else
                        {
                            foreach (var chapter in jsonItem.Chapters)
                            {
                                chapter.ImageTag = null;
                            }
                        }

                        if (item is Episode)
                        {
                            jsonItem.ZeroFingerprintConfidence =
                                !string.IsNullOrEmpty(_itemRepository.GetIntroDetectionFailureResult(item.InternalId));
                        }

                        if (item is Audio && Plugin.Instance?.GetPluginOptions()?.MediaInfoExtractOptions?.ClearEmbeddedMetadata != true)
                        {
                            var primaryImageInfo = item.GetImageInfo(ImageType.Primary, 0);
                            if (primaryImageInfo != null && _fileSystem.FileExists(primaryImageInfo.Path))
                            {
                                var imageBytes = await _fileSystem
                                    .ReadAllBytesAsync(primaryImageInfo.Path, CancellationToken.None)
                                    .ConfigureAwait(false);
                                var base64String = Convert.ToBase64String(imageBytes);
                                jsonItem.EmbeddedImage = base64String;
                            }
                        }
                    }

                    var parentDirectory = Path.GetDirectoryName(mediaInfoJsonPath);
                    if (!string.IsNullOrEmpty(parentDirectory))
                    {
                        Directory.CreateDirectory(parentDirectory);
                    }

                    _jsonSerializer.SerializeToFile(mediaSourcesWithChapters, mediaInfoJsonPath);

                    _logger.Info("MediaInfoPersist - Serialization Success (" + source + "): " + mediaInfoJsonPath);

                    return true;
                }
                catch (Exception e)
                {
                    _logger.Error("MediaInfoPersist - Serialization Failed (" + source + "): " + mediaInfoJsonPath);
                    _logger.Error(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }

            return false;
        }

        public async Task<bool> SerializeMediaInfo(long itemId, IDirectoryService directoryService, bool overwrite,
            string source)
        {
            var workItem = _libraryManager.GetItemById(itemId);

            if (!Plugin.LibraryApi.HasMediaInfo(workItem))
            {
                _logger.Info("MediaInfoPersist - Serialization Skipped - No MediaInfo (" + source + ")");
                return false;
            }

            if (!Plugin.LibraryApi.IsLibraryInScope(workItem)) return false;

            var ds = directoryService ?? new DirectoryService(_logger, _fileSystem);

            return await SerializeMediaInfo(workItem, ds, overwrite, source).ConfigureAwait(false);
        }

        /// <summary>
        /// 直接用手上这个条目实例做持久化。
        /// 原来的入口用 itemId 反查，拿到的是缓存实例，媒体流还没刷新，
        /// 于是 HasMediaInfo 判为 false → "Serialization Skipped - No MediaInfo"（生产实测）。
        /// </summary>
        public async Task<bool> SerializeMediaInfoDirect(BaseItem item, IDirectoryService directoryService,
            bool overwrite, string source)
        {
            if (item == null) return false;

            if (!Plugin.LibraryApi.HasMediaInfo(item))
            {
                _logger.Info("MediaInfoPersist - Serialization Skipped - No MediaInfo (" + source + ", 实例直传)");
                return false;
            }

            if (!Plugin.LibraryApi.IsLibraryInScope(item)) return false;

            var ds = directoryService ?? new DirectoryService(_logger, _fileSystem);

            return await SerializeMediaInfo(item, ds, overwrite, source).ConfigureAwait(false);
        }

        /// <summary>
        /// 把探测到的媒体流写进数据库。
        /// 只设置 item.MediaStreams 属性并不会落库，HasMediaInfo / 持久化 / 片头标记都读不到，
        /// 必须走 itemRepository.SaveMediaStreams（生产实测：探测成功但"Serialization Skipped - No MediaInfo"）。
        /// </summary>
        /// <summary>
        /// 片头/片尾标记算出来后只写进了数据库；持久化开启时把这一季有标记的分集重新导出 JSON，
        /// 否则删库重加后标记无法从 JSON 还原。计划任务和追更队列都要调用。
        /// </summary>
        public async Task PersistIntroMarkersForSeason(MediaBrowser.Controller.Entities.TV.Season season, string source)
        {
            if (season == null) return;
            var mode = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.PersistMediaInfoMode;
            if (mode == Options.MediaInfoExtractOptions.PersistMediaInfoOption.None.ToString() ||
                mode == Options.MediaInfoExtractOptions.PersistMediaInfoOption.Restore.ToString()) return;

            var ds = new DirectoryService(_logger, _fileSystem);
            var episodes = season.GetEpisodes(new InternalItemsQuery
                    { GroupByPresentationUniqueKey = false, EnableTotalRecordCount = false })
                .Items.OfType<MediaBrowser.Controller.Entities.TV.Episode>();

            foreach (var ep in episodes)
            {
                if (!Plugin.ChapterApi.HasIntro(ep)) continue;
                await SerializeMediaInfo(ep.InternalId, ds, true, source).ConfigureAwait(false);
            }
        }

        public void PersistStreams(long itemId, System.Collections.Generic.IEnumerable<MediaBrowser.Model.Entities.MediaStream> streams)
        {
            if (streams == null) return;
            var list = streams.ToList();
            if (list.Count == 0)
            {
                // 空集合 = 清空该条目的流（Dev/ClearMediaInfo 用于验证还原链路）
                _itemRepository.SaveMediaStreams(itemId, list, CancellationToken.None);
                return;
            }

            // StreamIndex 必须唯一（MediaStreams2 上有 UNIQUE(ItemId, StreamIndex)）。
            // 探测结果里字幕流常常没有 index，默认都是 0，直接写库会冲突：
            // "UNIQUE constraint failed: MediaStreams2.ItemId, MediaStreams2.StreamIndex"
            // 只给重复的流重新分配编号，保留原本唯一的 Index：
            // 转码时 Emby 用 StreamIndex 去 -map 对应的 ffmpeg 流，整体重排会让音轨/字幕选错。
            var used = new System.Collections.Generic.HashSet<int>();
            var next = list.Max(st => st.Index) + 1;
            foreach (var stream in list)
            {
                if (!used.Add(stream.Index))
                {
                    stream.Index = next++;
                    used.Add(stream.Index);
                }
            }

            _itemRepository.SaveMediaStreams(itemId, list, CancellationToken.None);
        }

        public async Task<bool> DeserializeMediaInfo(BaseItem item, IDirectoryService directoryService, string source,
            bool ignoreFileChange)
        {
            var workItem = _libraryManager.GetItemById(item.InternalId);

            if (Plugin.LibraryApi.HasMediaInfo(workItem)) return true;

            var mediaInfoJsonPath = ResolveExistingMediaInfoJsonPath(item, directoryService);
            var file = directoryService.GetFile(mediaInfoJsonPath);

            if (file?.Exists == true)
            {
                if (DryRunMode.Skip("还原媒体信息 " + item?.Name, _logger)) return true;

                try
                {
                    var mediaSourceWithChapters =
                        (await _jsonSerializer
                            .DeserializeFromFileAsync<List<MediaSourceWithChapters>>(mediaInfoJsonPath)
                            .ConfigureAwait(false)).ToArray()[0];

                    // 没有容器的 JSON 是旧版残缺探测导出的：还原它会让条目被当成 .strm 直接播放，宁可重新探测
                    var jsonMissingContainer = item.IsShortcut &&
                                               string.IsNullOrEmpty(mediaSourceWithChapters?.MediaSourceInfo?.Container);
                    if (jsonMissingContainer)
                        _logger.Info("MediaInfoPersist - JSON 缺少容器信息，跳过还原: " + mediaInfoJsonPath);

                    if (!jsonMissingContainer &&
                        mediaSourceWithChapters?.MediaSourceInfo?.RunTimeTicks.HasValue is true &&
                        (ignoreFileChange || !Plugin.LibraryApi.HasFileChanged(item, directoryService)))
                    {
                        foreach (var subtitle in mediaSourceWithChapters.MediaSourceInfo.MediaStreams.Where(m =>
                                     m.IsExternal && m.Type == MediaStreamType.Subtitle &&
                                     m.Protocol == MediaProtocol.File))
                        {
                            subtitle.Path = Path.Combine(workItem.ContainingFolderPath,
                                _fileSystem.GetFileInfo(subtitle.Path).Name);
                        }

                        _itemRepository.SaveMediaStreams(item.InternalId,
                            mediaSourceWithChapters.MediaSourceInfo.MediaStreams, CancellationToken.None);

                        if (workItem is Audio && !string.IsNullOrEmpty(mediaSourceWithChapters.EmbeddedImage))
                        {
                            var imageBytes = Convert.FromBase64String(mediaSourceWithChapters.EmbeddedImage);
                            var tempPath = Path.Combine(Plugin.Instance.ApplicationPaths.TempDirectory,
                                Guid.NewGuid() + ".jpg");
                            await _fileSystem.WriteAllBytesAsync(tempPath, imageBytes, CancellationToken.None)
                                .ConfigureAwait(false);

                            if (DryRunMode.Skip("写入内嵌封面 " + workItem?.Name, _logger))
                            {
                                try { _fileSystem.DeleteFile(tempPath); } catch { }
                            }
                            else
                            {
                                var libraryOptions = _libraryManager.GetLibraryOptions(workItem);

                                await _providerManager.SaveImage(workItem, libraryOptions, tempPath, ImageType.Primary,
                                        null, Array.Empty<long>(), directoryService, false, CancellationToken.None)
                                    .ConfigureAwait(false);

                                // 图片信息需要写回条目，否则接口侧仍视为无图
                                _libraryManager.UpdateItem(workItem, workItem.GetParent(), ItemUpdateType.ImageUpdate);
                            }
                        }

                        workItem.Size = mediaSourceWithChapters.MediaSourceInfo.Size.GetValueOrDefault();
                        workItem.RunTimeTicks = mediaSourceWithChapters.MediaSourceInfo.RunTimeTicks;
                        workItem.Container = mediaSourceWithChapters.MediaSourceInfo.Container;
                        workItem.TotalBitrate = mediaSourceWithChapters.MediaSourceInfo.Bitrate.GetValueOrDefault();

                        var videoStream = mediaSourceWithChapters.MediaSourceInfo.MediaStreams
                            .Where(s => s.Type == MediaStreamType.Video && s.Width.HasValue && s.Height.HasValue)
                            .OrderByDescending(s => (long)s.Width.Value * s.Height.Value)
                            .FirstOrDefault();

                        if (videoStream != null)
                        {
                            workItem.Width = videoStream.Width.GetValueOrDefault();
                            workItem.Height = videoStream.Height.GetValueOrDefault();
                        }

                        if (!DryRunMode.Skip("写入媒体信息 " + workItem?.Name, _logger))
                        {
                            _libraryManager.UpdateItems(new List<BaseItem> { workItem }, null,
                                ItemUpdateType.MetadataImport, false, false, null, CancellationToken.None);
                        }

                        if (workItem is Video video)
                        {
                            await DeserializeChapterInfo(video, mediaSourceWithChapters.Chapters, directoryService,
                                source).ConfigureAwait(false);

                            if (video is Episode && mediaSourceWithChapters.ZeroFingerprintConfidence is true)
                            {
                                _itemRepository.LogIntroDetectionFailureFailure(video.InternalId,
                                    item.DateModified.ToUnixTimeSeconds());
                            }
                        }

                        _logger.Info("MediaInfoPersist - Deserialization Success (" + source + "): " + mediaInfoJsonPath);

                        return true;
                    }

                    _logger.Info("MediaInfoPersist - Deserialization Skipped (" + source + "): " + mediaInfoJsonPath);
                }
                catch (Exception e)
                {
                    _logger.Error("MediaInfoPersist - Deserialization Failed (" + source + "): " + mediaInfoJsonPath);
                    _logger.Error(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }

            return false;
        }

        public void DeleteMediaInfoJson(BaseItem item, IDirectoryService directoryService, string source)
        {
            // 旧版 JSON 可能在 strm 旁边而不是根目录下：读和删都要找「实际存在的那一份」
            var mediaInfoJsonPath = ResolveExistingMediaInfoJsonPath(item, directoryService);

            var file = directoryService.GetFile(mediaInfoJsonPath);

            if (file?.Exists is true)
            {
                try
                {
                    _logger.Info($"MediaInfoPersist - Attempting to delete ({source}): {mediaInfoJsonPath}");

                    if (!DryRunMode.Skip("删除 mediaInfo 文件 " + mediaInfoJsonPath, _logger))
                        _fileSystem.DeleteFile(mediaInfoJsonPath);

                    var jsonRoot = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.MediaInfoJsonRootFolder;

                    if (!string.IsNullOrWhiteSpace(jsonRoot))
                    {
                        jsonRoot = _fileSystem.GetFullPath(jsonRoot).TrimEnd(Path.DirectorySeparatorChar);

                        var currentDir =
                            _fileSystem.GetFullPath(_fileSystem.GetDirectoryName(mediaInfoJsonPath) ?? string.Empty);

                        while (!string.IsNullOrEmpty(currentDir) &&
                               !string.Equals(currentDir, jsonRoot, StringComparison.OrdinalIgnoreCase) &&
                               CommonUtility.IsDirectoryEmpty(currentDir))
                        {
                            _logger.Info(
                                $"MediaInfoPersist - Attempting to delete empty folder ({source}): {currentDir}");
                            _fileSystem.DeleteDirectory(currentDir, false);
                            currentDir = Path.GetDirectoryName(currentDir);
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.Error(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }
        }

        public async Task<bool> DeserializeIntroMarker(Episode item, IDirectoryService directoryService, string source)
        {
            // 旧版 JSON 可能在 strm 旁边而不是根目录下：读和删都要找「实际存在的那一份」
            var mediaInfoJsonPath = ResolveExistingMediaInfoJsonPath(item, directoryService);
            var file = directoryService.GetFile(mediaInfoJsonPath);

            if (file?.Exists == true)
            {
                try
                {
                    var mediaSourceWithChapters =
                        (await _jsonSerializer
                            .DeserializeFromFileAsync<List<MediaSourceWithChapters>>(mediaInfoJsonPath)
                            .ConfigureAwait(false)).ToArray()[0];

                    if (mediaSourceWithChapters.ZeroFingerprintConfidence is true)
                    {
                        _itemRepository.LogIntroDetectionFailureFailure(item.InternalId,
                            item.DateModified.ToUnixTimeSeconds());

                        _logger.Info("ChapterInfoPersist - Log Zero Fingerprint Confidence (" + source + "): " + mediaInfoJsonPath);

                        return true;
                    }

                    var introStart = mediaSourceWithChapters.Chapters
                        .FirstOrDefault(c => c.MarkerType == MarkerType.IntroStart);

                    var introEnd = mediaSourceWithChapters.Chapters
                        .FirstOrDefault(c => c.MarkerType == MarkerType.IntroEnd);

                    if (introStart != null && introEnd != null && introEnd.StartPositionTicks > introStart.StartPositionTicks)
                    {
                        var chapters = _itemRepository.GetChapters(item);
                        chapters.RemoveAll(c =>
                            c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd);
                        chapters.Add(introStart);
                        chapters.Add(introEnd);
                        chapters.Sort((c1, c2) => c1.StartPositionTicks.CompareTo(c2.StartPositionTicks));

                        if (!DryRunMode.Skip("写入章节 " + item?.Name, _logger))
                            _itemRepository.SaveChapters(item.InternalId, chapters);

                        _logger.Info("ChapterInfoPersist - Deserialization Success (" + source + "): " + mediaInfoJsonPath);

                        return true;
                    }
                }
                catch (Exception e)
                {
                    _logger.Error("ChapterInfoPersist - Deserialization Failed (" + source + "): " + mediaInfoJsonPath);
                    _logger.Error(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }

            return false;
        }

        public async Task<bool> DeserializeChapterInfo(Video item, List<ChapterInfo> chapters,
            IDirectoryService directoryService, string source)
        {
            var thumbnailResult = false;

            if (Plugin.Instance.IsModSupported || !item.IsShortcut)
            {
                var localThumbnailSets =
                    Video.GetLocalThumbnailSetInfos(item.Path, item.Id, false, directoryService);

                if (localThumbnailSets.Length > 0)
                {
                    var options = _libraryManager.GetLibraryOptions(item);
                    var dummyLibraryOptions = new LibraryOptions
                    {
                        ThumbnailImagesIntervalSeconds = 10,
                        CacheImages = options.CacheImages
                    };

                    thumbnailResult = await Plugin.VideoThumbnailApi.RefreshThumbnailImages(item,
                        dummyLibraryOptions, directoryService, chapters, false, false,
                        CancellationToken.None);

                    if (thumbnailResult)
                    {
                        _logger.Info("ChapterInfoPersist - Video Thumbnail Restore Success (" + source + "): " +
                                     localThumbnailSets[0].Path);
                    }
                }
            }

            if (!DryRunMode.Skip("写入章节 " + item?.Name, _logger))
                _itemRepository.SaveChapters(item.InternalId, true, chapters);

            return thumbnailResult;
        }

        public void QueueRefreshAlternateVersions(BaseItem item, MetadataRefreshOptions options, bool force)
        {
            if (!(item is Video video)) return;

            var altIds = video.GetAlternateVersionIds();

            if (!altIds.Any()) return;

            var itemsToRefresh = force
                ? altIds
                : _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        ItemIds = altIds.ToArray(),
                        HasPath = true,
                        HasAudioStream = false,
                        MediaTypes = new[] { MediaType.Video }
                    })
                    .Select(i => i.InternalId);

            foreach (var altId in itemsToRefresh)
            {
                _providerManager.QueueRefresh(altId, options, RefreshPriority.Normal);
            }
        }

        public BaseItem GetItemByMediaSourceId(BaseItem item, string mediaSourceId)
        {
            if (string.IsNullOrEmpty(mediaSourceId)) return null;

            BaseItem targetItem = null;

            if (item.GetDefaultMediaSourceId() == mediaSourceId)
            {
                targetItem = item;
            }
            else
            {
                var mediaSource = item.GetMediaSources(true, false, null).FirstOrDefault(s => s.Id == mediaSourceId);

                if (mediaSource != null)
                {
                    targetItem = _libraryManager.GetItemById(mediaSource.ItemId);
                }
            }

            return targetItem;
        }
    }
}
