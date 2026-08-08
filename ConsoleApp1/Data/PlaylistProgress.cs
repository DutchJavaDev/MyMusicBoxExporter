namespace Exporter.Data
{
    // Per supabase-url, per playlist export state persisted in export-progress.json.
    // LastSongId is the last contiguous successfully uploaded song id: it never
    // advances past a failed song, so resuming at LastSongId + 1 retries failures.
    sealed class PlaylistProgress
    {
        public string? PlaylistName { get; set; }

        public int BeatMixId { get; set; }

        public int LastSongId { get; set; }

        public string? LastSongTitle { get; set; }

        public List<int> FailedSongIds { get; set; } = new();

        public DateTime UpdatedAtUtc { get; set; }
    }
}
