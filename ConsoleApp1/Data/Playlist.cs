namespace Exporter.Data
{
    sealed class Playlist()
    {
        public string? name { get; set; }
        public int? id { get; set; }
        public string? description { get; set; }
        public int? songcount { get; set; }
        public string? thumbnailpath { get; set; }
        public bool? ispublic { get; set; }
        public DateTime? creationdate { get; set; }

        public override string ToString()
        {
            return $"{name} has {songcount} songs";
        }
    }
}
