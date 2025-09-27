namespace Firefox_Bookmarks_Cleanup.Models
{
    internal class BrowserBookmark
    {
        public BrowserBookmark() { }

        public BrowserBookmark(BrowserBookmark bookmark)
        {
            Id = bookmark.Id;
            Title = bookmark.Title;
            Guid = bookmark.Guid;
            Url = bookmark.Url;
        }

        public long Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Guid { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;
    }
}
