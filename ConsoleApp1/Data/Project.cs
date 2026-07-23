namespace Exporter.Data
{
    sealed class Project()
    {
        public string? name { get; set; }
        public string? thumbnailpath { get; set; }
        public bool? ispublic { get; set; }
        public DateTime? creationdate { get; set; }
    }
}
