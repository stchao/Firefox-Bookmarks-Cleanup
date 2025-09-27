using Microsoft.Extensions.Logging;

namespace Firefox_Bookmarks_Cleanup.Services
{
    internal class BookmarkCleanupRunner(
        IDatabaseService dbService,
        IBookmarkService bookmarkService,
        ILogger<BookmarkCleanupRunner> logger
    )
    {
        public async Task RunAsync(bool dryRun = false, CancellationToken token = default)
        {
            await dbService.InitializeTablesFromUserDb(token);
            var folderIds = await dbService.GetAllFolderIdsFromBrowserDb(token);
            var bookmarksFromBrowser = await dbService.GetAllBookmarksFromBrowserDb(
                folderIds,
                token
            );
            var bookmarksIdMappingDict = await dbService.GetAllBookmarksTitlesFromUserDb(token);
            var bookmarksDict = await dbService.GetAllBookmarksFromUserDb(token);

            var bookmarksFromBrowserToDelete = bookmarkService.ProcessBrowserBookmarksToRemove(
                bookmarksFromBrowser,
                bookmarksIdMappingDict,
                bookmarksDict
            );

            logger.LogInformation(
                "{count} bookmark(s) to be deleted from browser.",
                bookmarksFromBrowserToDelete.Count
            );

            int deletedCount;
            if (dryRun)
            {
                deletedCount = 0;
                logger.LogInformation(
                    "[Dry Run] No bookmarks deleted. Would have deleted {count}.",
                    bookmarksFromBrowserToDelete.Count
                );
            }
            else
            {
                deletedCount = await dbService.DeleteBookmarksFromBrowserDb(
                    bookmarksFromBrowserToDelete,
                    token
                );
                logger.LogInformation("{count} bookmark(s) deleted from browser.", deletedCount);
            }

            var bookmarksToCreateInDb = bookmarksDict
                .Where(b => b.Value.IsCreated)
                .Select(b => b.Value)
                .ToList();
            logger.LogInformation(
                "{count} bookmark(s) to be inserted into db.",
                bookmarksToCreateInDb.Count
            );

            int insertedCount;

            if (dryRun)
            {
                insertedCount = 0;
                logger.LogInformation(
                    "[Dry Run] No bookmarks inserted. Would have inserted {count}.",
                    bookmarksToCreateInDb.Count
                );
            }
            else
            {
                insertedCount = await dbService.InsertBookmarksIntoUserDb(
                    bookmarksToCreateInDb,
                    token
                );
                logger.LogInformation("{count} bookmark(s) inserted into db.", insertedCount);
            }

            var bookmarksToUpdateInDb = bookmarksDict
                .Where(b => b.Value.IsUpdated)
                .Select(b => b.Value)
                .ToList();
            logger.LogInformation(
                "{count} bookmark(s) to be updated in db.",
                bookmarksToUpdateInDb.Count
            );

            int updatedCount;
            if (dryRun)
            {
                updatedCount = 0;
                logger.LogInformation(
                    "[Dry Run] No bookmarks updated. Would have updated {count}.",
                    bookmarksToUpdateInDb.Count
                );
            }
            else
            {
                updatedCount = await dbService.UpdateBookmarksInUserDb(
                    bookmarksToUpdateInDb,
                    token
                );
                logger.LogInformation("{count} bookmark(s) updated in db.", updatedCount);
            }
        }
    }
}
