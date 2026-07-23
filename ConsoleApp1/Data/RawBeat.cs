using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Exporter.Data
{
    [Table("rawbeat")]
    class RawBeat : BaseModel
    {
        [PrimaryKey("id", shouldInsert: false)]
        public int Id { get; set; }

        [Column("source")]
        public string Source { get; set; }

        [Column("audiolocation")]
        public string? AudioLocation { get; set; }

        [Column("thumbnaillocation")]
        public string? Thumbnaillocation { get; set; }

        [Column("createdatutc")]
        public DateTime? Createddate { get; set; }

        [Column("duration")]
        public int? Duration { get; set; }
    }
}
