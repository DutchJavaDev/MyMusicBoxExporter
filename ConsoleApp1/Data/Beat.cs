using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Exporter.Data
{
    [Table("beat")]
    class Beat : BaseModel
    {
        [PrimaryKey("id", shouldInsert: false)]
        public int id { get; set; }

        [Column("rawbeatid")]
        public int rawbeatid { get; set; }

        [Column("title")]
        public string title { get; set; }

        [Column("artist")]
        public string artist { get; set; }

        [Column("streamingurl")]
        public string streamingurl { get; set; }

        [Column("thumbnailurl")]
        public string thumbnailurl { get; set; }

        [Column("published")]
        public bool published { get; set; }

        [Column("tags")]
        public string tags { get; set; }
    }
}
