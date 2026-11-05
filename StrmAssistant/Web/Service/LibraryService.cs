using MediaBrowser.Controller.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using StrmAssistant.Web.Api;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static StrmAssistant.Common.CommonUtility;
using static StrmAssistant.Common.LanguageUtility;

namespace StrmAssistant.Web.Service
{
    public class LibraryService : BaseApiService
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly IItemRepository _itemRepository;

        public LibraryService(ILibraryManager libraryManager, IItemRepository itemRepository, IFileSystem fileSystem)
        {
            _logger = Plugin.Instance.Logger;
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
            _fileSystem = fileSystem;
        }

        public void Any(DeleteVersion request)
        {
            if (Common.DryRunMode.Skip("删除版本 " + request.Id, _logger)) return;

            var item = _libraryManager.GetItemById(request.Id);

            // 不满足条件时返回明确的错误（以前一律静默返回 204，网页会提示「删除版本成功」，其实什么也没删）
            if (!(item is Video video) || !(video is Movie || video is Episode) || !video.IsFileProtocol ||
                video.GetAlternateVersionIds().Count == 0)
            {
                throw new ArgumentException("不是本地文件的多版本条目，没有可删除的版本");
            }
            
            var user = GetUserForRequest(null);
            var collectionFolders = _libraryManager.GetCollectionFolders(item);

            if (user is null ? !item.CanDelete() : !item.CanDelete(user, collectionFolders))
            {
                _logger.Info("DeleteVersion - 无删除权限: " + (user?.Name ?? "?") + " → " + item.Name);
                throw new MediaBrowser.Controller.Net.SecurityException("没有删除这个条目的权限", (MediaBrowser.Controller.Net.SecurityExceptionType)2); // 2 = Forbidden，Emby 返回 403
            }

            var deleteItems = item is Episode episode && request.DeleteParent
                ? GetSeasonEpisodesSameVersion(episode)
                : new List<BaseItem> { item };

            foreach (var deleteItem in deleteItems)
            {
                var proceedToDelete = true;
                var deletePaths = Plugin.LibraryApi.GetDeletePaths(deleteItem);

                foreach (var path in deletePaths)
                {
                    try
                    {
                        if (!path.IsDirectory)
                        {
                            _logger.Info("DeleteVersion - Attempting to delete file: " + path.FullName);
                            _fileSystem.DeleteFile(path.FullName, true);
                        }
                    }
                    catch (Exception e)
                    {
                        if (e is IOException || e is UnauthorizedAccessException)
                        {
                            proceedToDelete = false;
                            _logger.Error("DeleteVersion - Failed to delete file: " + path.FullName);
                            _logger.Error(e.Message);
                            _logger.Debug(e.StackTrace);
                        }
                    }
                }

                if (proceedToDelete)
                {
                    _itemRepository.DeleteItems(new[] { deleteItem });

                    try
                    {
                        // 删的是本集自己的元数据目录（以前误用了被点的那一集 item，其它集的目录不会被删）
                        _fileSystem.DeleteDirectory(deleteItem.GetInternalMetadataPath(), true, true);
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
        }

        private List<BaseItem> GetSeasonEpisodesSameVersion(Episode episode)
        {
            var seasonFolderChildren = episode.Parent.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { nameof(Episode) },
                    Recursive = false,
                    GroupByPresentationUniqueKey = false,
                })
                .ToList();

            var seasonEpisodesCount = episode.Season.GetEpisodeIds(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { nameof(Episode) }, GroupByPresentationUniqueKey = true
                })
                .Length;

            if (seasonFolderChildren.Count == seasonEpisodesCount)
            {
                return seasonFolderChildren;
            }

            var targetCleaned = CleanEpisodeName(episode.FileNameWithoutExtension);

            var allEpisodes = episode.Season
                .GetEpisodes(new InternalItemsQuery
                {
                    GroupByPresentationUniqueKey = false, EnableTotalRecordCount = false
                })
                .Items.ToList();

            var similarEpisodes = new List<BaseItem>();
            var targetTags = Common.VersionTags.Of(episode.FileNameWithoutExtension);
            var targetDir = System.IO.Path.GetDirectoryName(episode.Path ?? string.Empty);
            var targetExt = System.IO.Path.GetExtension(episode.Path ?? string.Empty);

            foreach (var ep in allEpisodes)
            {
                // 同一版本 = 同一目录、同一扩展名、清晰度/编码/来源这些版本标签完全一致，再加上文件名足够像。
                // 以前只看文件名相似度（> 0.92），文件名一长「2160p」和「1080p」就只差几个字符，
                // 在 4K 那一集上点「删除整季」会把 1080p 版本的文件也一起删掉。
                if (!string.Equals(System.IO.Path.GetDirectoryName(ep.Path ?? string.Empty), targetDir, StringComparison.Ordinal)) continue;
                if (!string.Equals(System.IO.Path.GetExtension(ep.Path ?? string.Empty), targetExt, StringComparison.OrdinalIgnoreCase)) continue;
                if (!Common.VersionTags.Of(ep.FileNameWithoutExtension).SetEquals(targetTags)) continue;

                var cleanedName = CleanEpisodeName(ep.FileNameWithoutExtension);
                var similarity = LevenshteinDistance(targetCleaned, cleanedName);

                if (similarity > 0.92)
                {
                    similarEpisodes.Add(ep);
                }
            }

            return similarEpisodes;
        }
    }
}
