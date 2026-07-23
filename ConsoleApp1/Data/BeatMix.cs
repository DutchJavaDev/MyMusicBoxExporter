using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Exporter.Data
{

    [Table("beatmix")]
    class BeatMix : BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Column("title")]
        public string? Title { get; set; }

        [Column("thumbnailurl")]
        public string? Thumbnailpath { get; set; }

        [Column("beatable")]
        public bool? Beatable { get; set; }

        [Column("createdon")]
        public DateTime? Creationdate { get; set; }
    }
}
