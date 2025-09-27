using Firefox_Bookmarks_Cleanup.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Firefox_Bookmarks_Cleanup.Services
{
    internal class DatabaseService(ILogger<DatabaseService> logger, IConfiguration configuration)
        : IDatabaseService
    {
        private readonly string _browserDb =
            configuration.GetConnectionString("BrowserDb") ?? string.Empty;
        private readonly string _db = configuration.GetConnectionString("Db") ?? string.Empty;
        private readonly List<string> _folderNamesOrIds =
            configuration.GetSection("FolderNamesOrIds").Get<List<string>>() ?? ["6"];
        private readonly string _booksTableName = "books";
        private readonly string _bookTitlesTableName = "book_titles";

        public async Task<List<long>> GetAllFolderIdsFromBrowserDb(
            CancellationToken cancellationToken = default
        )
        {
            var folderIds = new List<long>();

            try
            {
                using var connection = new SqliteConnection(_browserDb);
                await connection.OpenAsync(cancellationToken);
                using var command = connection.CreateCommand();

                var idParams = new List<string>();
                var titleParams = new List<string>();

                for (int i = 0; i < _folderNamesOrIds.Count; i++)
                {
                    var folderNameOrId = _folderNamesOrIds[i];
                    if (int.TryParse(folderNameOrId, out var folderId))
                    {
                        var paramName = $"@folderId{i}";
                        idParams.Add(paramName);
                        command.Parameters.AddWithValue(paramName, folderId);
                    }
                    else
                    {
                        var paramName = $"@title{i}";
                        titleParams.Add(paramName);
                        command.Parameters.AddWithValue(paramName, folderNameOrId);
                    }
                }

                command.CommandText =
                    $@"
                        SELECT id
                        FROM moz_bookmarks
                        WHERE fk IS NULL AND
                           (id IN ({string.Join(", ", idParams)}) 
                           OR title IN ({string.Join(", ", titleParams)}))
                    ";

                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    folderIds.Add(reader.GetInt64(reader.GetOrdinal("id")));
                }

                await connection.CloseAsync();
            }
            catch (SqliteException ex)
            {
                logger.LogError(ex, "SQLite error occurred in GetAllFolderIdsFromBrowserDb.");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error occurred in GetAllFolderIdsFromBrowserDb.");
                throw;
            }

            return folderIds;
        }

        public async Task<List<BrowserBookmark>> GetAllBookmarksFromBrowserDb(
            List<long> parentIds,
            CancellationToken cancellationToken = default
        )
        {
            var bookmarks = new List<BrowserBookmark>();

            try
            {
                using var connection = new SqliteConnection(_browserDb);
                await connection.OpenAsync(cancellationToken);
                using var command = connection.CreateCommand();

                var parameters = new List<string>();
                for (int i = 0; i < parentIds.Count; i++)
                {
                    var paramName = $"@parent{i}";
                    parameters.Add(paramName);
                    command.Parameters.AddWithValue(paramName, parentIds[i]);
                }

                command.CommandText =
                    $@"
                        SELECT b.id, b.title, b.guid, p.url
                        FROM moz_bookmarks b
                        LEFT JOIN moz_places p
                            ON b.fk = p.id
                        WHERE b.parent in ({string.Join(", ", parameters)})
                    ";

                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    bookmarks.Add(
                        new BrowserBookmark
                        {
                            Id = reader.GetInt64(reader.GetOrdinal("id")),
                            Title = reader.GetString(reader.GetOrdinal("title")),
                            Guid = reader.GetString(reader.GetOrdinal("guid")),
                            Url = reader.GetString(reader.GetOrdinal("url"))
                        }
                    );
                }

                await connection.CloseAsync();
            }
            catch (SqliteException ex)
            {
                logger.LogError(ex, "SQLite error occurred in GetAllBookmarksFromBrowserDb.");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error occurred in GetAllBookmarksFromBrowserDb.");
                throw;
            }

            return bookmarks;
        }

        public async Task<int> DeleteBookmarksFromBrowserDb(
            List<BrowserBookmark> bookmarks,
            CancellationToken cancellationToken = default
        )
        {
            var rowsAffected = 0;
            var unixAsMicroSeconds = new DateTimeOffset(DateTime.Now).ToUnixTimeMilliseconds();

            if (bookmarks.Count > 0)
            {
                using var connection = new SqliteConnection(_browserDb);
                await connection.OpenAsync(cancellationToken);

                using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                try
                {
                    using var command = connection.CreateCommand();

                    foreach (var bookmark in bookmarks)
                    {
                        if (string.IsNullOrWhiteSpace(bookmark.Guid) || bookmark.Id == 0)
                        {
                            logger.LogError(
                                "Guid cannot be blank and/or id cannot be 0 {@bookmark}",
                                bookmark
                            );
                            continue;
                        }

                        command.CommandText =
                            @"
                                INSERT INTO moz_bookmarks_deleted
                                    (guid, dateRemoved)
                                VALUES
                                    (@guid, @dateRemoved);

                                DELETE
                                FROM moz_bookmarks
                                WHERE ID = @id;
                            ";
                        command.Parameters.AddWithValue("@guid", bookmark.Guid);
                        command.Parameters.AddWithValue("@dateRemoved", unixAsMicroSeconds);
                        command.Parameters.AddWithValue("@id", bookmark.Id);
                        rowsAffected += await command.ExecuteNonQueryAsync(cancellationToken);
                        command.Parameters.Clear();
                    }

                    await transaction.CommitAsync(cancellationToken);
                    await connection.CloseAsync();
                }
                catch (SqliteException ex)
                {
                    logger.LogError(ex, "SQLite error occurred in DeleteBookmarksFromBrowserDb.");
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Unexpected error occurred in DeleteBookmarksFromBrowserDb."
                    );
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }

            // Each bookmark deletion affects 2 rows (moz_bookmarks and moz_bookmarks_deleted)
            return rowsAffected / 2;
        }

        public async Task InitializeTablesFromUserDb(CancellationToken cancellationToken = default)
        {
            try
            {
                using var connection = new SqliteConnection(_db);
                await connection.OpenAsync(cancellationToken);
                using var command = connection.CreateCommand();
                command.CommandText =
                    $@"
                        CREATE TABLE IF NOT EXISTS {_booksTableName} (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            title TEXT NOT NULL,
                            chapter REAL NOT NULL
                        );
                        CREATE TABLE IF NOT EXISTS {_bookTitlesTableName} (
                            parent_id INTEGER NOT NULL,
                            title TEXT NOT NULL,
                            FOREIGN KEY (parent_id) REFERENCES {_booksTableName}(id) ON DELETE CASCADE
                        );
                        CREATE INDEX IF NOT EXISTS idx_{_bookTitlesTableName}_title ON {_bookTitlesTableName}(title);
                    ";
                await command.ExecuteNonQueryAsync(cancellationToken);
                await connection.CloseAsync();
            }
            catch (SqliteException ex)
            {
                logger.LogError(ex, "SQLite error occurred in InitializeTablesFromUserDb.");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error occurred in InitializeTablesFromUserDb.");
                throw;
            }
        }

        public async Task<Dictionary<string, long>> GetAllBookmarksTitlesFromUserDb(
            CancellationToken cancellationToken = default
        )
        {
            var bookmarksTitles = new Dictionary<string, long>();

            try
            {
                using var connection = new SqliteConnection(_db);
                await connection.OpenAsync(cancellationToken);
                using var command = connection.CreateCommand();
                command.CommandText =
                    $@"
                        SELECT parent_id, title
                        FROM {_bookTitlesTableName}
                    ";

                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    bookmarksTitles.Add(
                        reader.GetString(reader.GetOrdinal("title")).ToLower(),
                        reader.GetInt64(reader.GetOrdinal("parent_id"))
                    );
                }

                await connection.CloseAsync();
            }
            catch (SqliteException ex)
            {
                logger.LogError(ex, "SQLite error occurred in GetAllBookmarksTitlesFromDb.");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error occurred in GetAllBookmarksTitlesFromDb.");
                throw;
            }

            return bookmarksTitles;
        }

        public async Task<Dictionary<long, DbBookmark>> GetAllBookmarksFromUserDb(
            CancellationToken cancellationToken = default
        )
        {
            var bookmarks = new Dictionary<long, DbBookmark>();

            try
            {
                using var connection = new SqliteConnection(_db);
                await connection.OpenAsync(cancellationToken);
                using var command = connection.CreateCommand();
                command.CommandText =
                    $@"
                        SELECT id, title, chapter
                        FROM {_booksTableName}
                    ";

                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var id = reader.GetInt64(reader.GetOrdinal("id"));

                    bookmarks.Add(
                        id,
                        new DbBookmark()
                        {
                            Id = id,
                            Chapter = reader.GetFloat(reader.GetOrdinal("chapter")),
                            Title = reader.GetString(reader.GetOrdinal("title"))
                        }
                    );
                }

                await connection.CloseAsync();
            }
            catch (SqliteException ex)
            {
                logger.LogError(ex, "SQLite error occurred in GetAllBookmarksFromDb.");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error occurred in GetAllBookmarksFromDb.");
                throw;
            }

            return bookmarks;
        }

        public async Task<int> InsertBookmarksIntoUserDb(
            List<DbBookmark> bookmarks,
            CancellationToken cancellationToken = default
        )
        {
            var rowsAffected = 0;

            if (bookmarks.Count > 0)
            {
                using var connection = new SqliteConnection(_db);
                await connection.OpenAsync(cancellationToken);

                using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                try
                {
                    using var command = connection.CreateCommand();

                    foreach (var bookmark in bookmarks)
                    {
                        command.CommandText =
                            $@"
                                INSERT INTO {_booksTableName}
                                    (title, chapter)
                                VALUES
                                    (@title, @chapter);
                            ";
                        command.Parameters.AddWithValue("@title", bookmark.Title.Trim());
                        command.Parameters.AddWithValue("@chapter", bookmark.Chapter);
                        rowsAffected += await command.ExecuteNonQueryAsync(cancellationToken);
                        command.Parameters.Clear();
                    }

                    command.CommandText =
                        $@"
                            INSERT INTO {_bookTitlesTableName}
                                (parent_id, title)
                            SELECT id, title
                            FROM {_booksTableName}
                            WHERE id NOT IN (SELECT parent_id
                                             FROM {_bookTitlesTableName});
                        ";
                    rowsAffected += await command.ExecuteNonQueryAsync(cancellationToken);

                    await transaction.CommitAsync(cancellationToken);
                    await connection.CloseAsync();
                }
                catch (SqliteException ex)
                {
                    logger.LogError(ex, "SQLite error occurred in InsertBookmarksIntoDb.");
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unexpected error occurred in InsertBookmarksIntoDb.");
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }

            // Each bookmark insertion affects 2 rows (mangas and manga_titles)
            return rowsAffected / 2;
        }

        public async Task<int> UpdateBookmarksInUserDb(
            List<DbBookmark> bookmarks,
            CancellationToken cancellationToken = default
        )
        {
            var rowsAffected = 0;

            if (bookmarks.Count > 0)
            {
                using var connection = new SqliteConnection(_db);
                await connection.OpenAsync(cancellationToken);

                using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                try
                {
                    using var command = connection.CreateCommand();

                    foreach (var bookmark in bookmarks)
                    {
                        command.CommandText =
                            $@"
                                UPDATE {_booksTableName}
                                SET title = @title,
                                    chapter = @chapter
                                WHERE id = @id;
                            ";
                        command.Parameters.AddWithValue("@id", bookmark.Id);
                        command.Parameters.AddWithValue("@title", bookmark.Title.Trim());
                        command.Parameters.AddWithValue("@chapter", bookmark.Chapter);
                        rowsAffected += await command.ExecuteNonQueryAsync(cancellationToken);
                        command.Parameters.Clear();
                    }

                    await transaction.CommitAsync(cancellationToken);
                    await connection.CloseAsync();
                }
                catch (SqliteException ex)
                {
                    logger.LogError(ex, "SQLite error occurred in UpdateBookmarksInDb.");
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unexpected error occurred in UpdateBookmarksInDb.");
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }

            return rowsAffected;
        }
    }

    /// <summary>
    /// Provides methods for interacting with the application's and user's databases, including reading, inserting,
    /// updating, and deleting bookmarks.
    /// </summary>
    internal interface IDatabaseService
    {
        /// <summary>
        /// Retrieves the IDs of all bookmark folders from the browser database that match the configured folder names or IDs.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
        /// <returns>A list of folder IDs as <see cref="long"/> values.</returns>
        public Task<List<long>> GetAllFolderIdsFromBrowserDb(
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// Retrieves all bookmarks from the browser database whose parent IDs match the specified list.
        /// </summary>
        /// <param name="parentIds">A list of parent folder IDs to filter bookmarks.</param>
        /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
        /// <returns>A list of <see cref="BrowserBookmark"/> objects found under the specified parent IDs.</returns>
        public Task<List<BrowserBookmark>> GetAllBookmarksFromBrowserDb(
            List<long> parentIds,
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// Deletes the specified bookmarks from the browser database.
        /// </summary>
        /// <param name="bookmarks">The bookmarks to delete.</param>
        /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
        /// <returns>The number of bookmarks deleted.</returns>
        public Task<int> DeleteBookmarksFromBrowserDb(
            List<BrowserBookmark> bookmarks,
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// Creates any missing tables and indexes in the user's database.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
        public Task InitializeTablesFromUserDb(CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves all bookmark titles and their parent IDs from the database.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
        /// <returns>A dictionary mapping bookmark titles to parent IDs.</returns>
        public Task<Dictionary<string, long>> GetAllBookmarksTitlesFromUserDb(
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// Retrieves all bookmarks from the user's database.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
        /// <returns>A dictionary mapping bookmark IDs to <see cref="DbBookmark"/> objects.</returns>
        public Task<Dictionary<long, DbBookmark>> GetAllBookmarksFromUserDb(
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// Inserts the specified bookmarks into the database.
        /// </summary>
        /// <param name="bookmarks">The bookmarks to insert.</param>
        /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
        /// <returns>The number of bookmarks inserted.</returns>
        public Task<int> InsertBookmarksIntoUserDb(
            List<DbBookmark> bookmarks,
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// Updates the specified bookmarks in the database.
        /// </summary>
        /// <param name="bookmarks">The bookmarks to update.</param>
        /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
        /// <returns>The number of bookmarks updated.</returns>
        public Task<int> UpdateBookmarksInUserDb(
            List<DbBookmark> bookmarks,
            CancellationToken cancellationToken = default
        );
    }
}
