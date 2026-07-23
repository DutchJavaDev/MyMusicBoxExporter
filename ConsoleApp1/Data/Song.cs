using System;
using System.Collections.Generic;
using System.Text;

namespace Exporter.Data
{
    sealed class Song
    {
        public string title { get; set; }

        public string path { get; set; }

        public string thumbnailpath { get; set; }

        public int duration { get; set; }

        public DateTime? createdat { get; set; }

        public string sourceid { get; set; }
    }
}
