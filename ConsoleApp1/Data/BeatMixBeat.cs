using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Exporter.Data
{
    [Table("beatmixbeat")]
    class BeatMixBeat : BaseModel
    {
        [PrimaryKey("beatid", shouldInsert: true)]
        public int Beatid { get; set; }

        [PrimaryKey("beatmixid", shouldInsert: true)]
        public int Beatmixid { get; set; }
    }
}
