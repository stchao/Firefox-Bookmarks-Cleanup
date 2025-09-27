using System.Text;
using System.Text.RegularExpressions;
using Firefox_Bookmarks_Cleanup.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Firefox_Bookmarks_Cleanup.Services
{
    internal partial class BookmarkService(
        ILogger<BookmarkService> logger,
        IConfiguration configuration
    ) : IBookmarkService
    {
        [GeneratedRegex(
            @"^(?:.* Scans - )?(?:Chapter\s+\d+\s*-?\s*)?[,]?(?'title'.*?)(?:\s*-?\s*Chapter (?'num'\d+\.?\d*))?(?: - .*|Vol\.|Chapter|Manga|\||$)",
            RegexOptions.IgnoreCase,
            "en-US"
        )]
        private static partial Regex TitleChapterRegex();

        [GeneratedRegex(
            @"(?:\s*-?\s*(?:Chapter|Ch\.|Episode)\s*(?'num'\d+\.?\d*))",
            RegexOptions.IgnoreCase,
            "en-US"
        )]
        private static partial Regex ChapterRegex();

        private readonly HashSet<char> _nonAsciiIgnoreList =
            configuration.GetSection("Exceptions:NonAsciiIgnoreList").Get<HashSet<char>>() ?? [];
        private readonly Dictionary<string, string> _nonAsciiReplacements =
            configuration.GetSection("Exceptions:Replace").Get<Dictionary<string, string>>() ?? [];

        public List<BrowserBookmark> ProcessBrowserBookmarksToRemove(
            List<BrowserBookmark> browserBookmarks,
            Dictionary<string, long> bookmarksIdMappingDict,
            Dictionary<long, DbBookmark> bookmarksDict
        )
        {
            var browserBookmarksToRemove = new List<BrowserBookmark>();
            var startId =
                bookmarksIdMappingDict.Count > 0 ? bookmarksIdMappingDict.Max(b => b.Value) + 1 : 1;

            foreach (var browserBookmark in browserBookmarks)
            {
                var (title, chapter) = ExtractTitleAndChapter(browserBookmark);
                var isNotAscii = title.Any(
                    c => !char.IsAscii(c) && !_nonAsciiIgnoreList.Contains(c)
                );

                if (string.IsNullOrWhiteSpace(title) || chapter == 0 || isNotAscii)
                {
                    logger.LogError(
                        "Failed to extract information (isNotAscii: {isNotAscii}) from {@bookmark}",
                        isNotAscii,
                        browserBookmark
                    );
                    continue;
                }

                var titleKey = title.ToLower();
                if (!bookmarksIdMappingDict.TryGetValue(titleKey, out var mappedId))
                {
                    bookmarksIdMappingDict[titleKey] = startId;
                    bookmarksDict[startId] = new DbBookmark()
                    {
                        Title = title,
                        Chapter = chapter,
                        BrowserBookmark = browserBookmark,
                        IsCreated = true
                    };
                    startId++;
                    continue;
                }

                var bookmark = bookmarksDict[mappedId];

                if (chapter < bookmark.Chapter)
                {
                    browserBookmarksToRemove.Add(browserBookmark);
                    logger.LogInformation(
                        "Bookmark marked for deletion: {@bookmark}",
                        browserBookmark
                    );
                    continue;
                }

                if (chapter > bookmark.Chapter && bookmark.BrowserBookmark is not null)
                {
                    var clonedBookmark = new BrowserBookmark(bookmark.BrowserBookmark);
                    browserBookmarksToRemove.Add(clonedBookmark);
                    logger.LogInformation(
                        "Bookmark marked for deletion: {@bookmark}",
                        clonedBookmark
                    );
                }

                bookmark.Title = title;
                bookmark.IsUpdated =
                    bookmark.IsUpdated || (!bookmark.IsCreated && chapter > bookmark.Chapter);
                bookmark.Chapter = chapter;
                bookmark.BrowserBookmark = browserBookmark;
            }

            return browserBookmarksToRemove;
        }

        private (string title, float chapter) ExtractTitleAndChapter(BrowserBookmark bookmark)
        {
            bookmark.Title = ReplaceNonAsciiExceptions(bookmark.Title);
            Match titleChapterMatch = TitleChapterRegex().Match(bookmark.Title);
            Match chapterMatch = ChapterRegex().Match(bookmark.Title);

            if (!titleChapterMatch.Success)
            {
                return (string.Empty, 0);
            }

            var titleValue = titleChapterMatch.Groups["title"].Value.Trim();
            var chapterValue = titleChapterMatch.Groups["num"].Success
                ? titleChapterMatch.Groups["num"].Value
                : chapterMatch.Groups["num"].Value;

            if (!float.TryParse(chapterValue, out var chapter))
            {
                return (string.Empty, 0f);
            }

            return (titleValue, chapter);
        }

        private string ReplaceNonAsciiExceptions(string text)
        {
            var sb = new StringBuilder(text);

            foreach (var nonAsciiCh in _nonAsciiReplacements.Keys)
            {
                if (text.Contains(nonAsciiCh))
                {
                    sb.Replace(nonAsciiCh, _nonAsciiReplacements[nonAsciiCh]);
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Provides methods for processing and managing browser bookmarks.
    /// </summary>
    internal interface IBookmarkService
    {
        /// <summary>
        /// Determines which browser bookmarks should be removed based on the current database state.
        /// </summary>
        /// <param name="browserBookmarks">The list of browser bookmarks to evaluate.</param>
        /// <param name="bookmarksIdMappingDict">A mapping of bookmark titles to parent IDs.</param>
        /// <param name="bookmarksDict">A dictionary of existing database bookmarks.</param>
        /// <returns>A list of <see cref="BrowserBookmark"/> objects to be removed.</returns>
        public List<BrowserBookmark> ProcessBrowserBookmarksToRemove(
            List<BrowserBookmark> browserBookmarks,
            Dictionary<string, long> bookmarksIdMappingDict,
            Dictionary<long, DbBookmark> bookmarksDict
        );
    }
}
