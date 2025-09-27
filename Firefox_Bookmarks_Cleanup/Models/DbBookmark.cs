namespace Firefox_Bookmarks_Cleanup.Models
{
    internal class DbBookmark
    {
        public long Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public float Chapter { get; set; }

        public BrowserBookmark? BrowserBookmark { get; set; }

        public bool IsCreated { get; set; } = false;

        public bool IsUpdated { get; set; } = false;
    }
}
