using Npgsql;
using Dapper;
using Exporter.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Supabase.Postgrest.Responses;
using EllipsePolygon = SixLabors.ImageSharp.Drawing.EllipsePolygon;
using RectangularPolygon = SixLabors.ImageSharp.Drawing.RectangularPolygon;

// Export data based on max size for a playlist
// Example: max 500MB, keep uploading until you have reached 500mb in audio/image files
// Args example: 1 500 db url key [startSongId]
// Optional 6th arg: source song id to resume from — songs with a lower id are skipped


//GRANT SELECT, INSERT ON librebeats.beat TO service_role;
//GRANT SELECT, INSERT ON librebeats.beatmix TO service_role;
//GRANT SELECT, INSERT ON librebeats.beatmixbeat TO service_role;
//GRANT SELECT, INSERT ON librebeats.rawbeat TO service_role;

if (args.Length < 5)
{
    throw new ArgumentException("Usage: <playlistId|all> <maxSizeInMb> <connectionString> <supabaseUrl> <supabaseKey> [startSongId]");
}

var hasPlaylistsId = int.TryParse(args[0], out var playListsId);
var hasTotalSize = long.TryParse(args[1], out var totalSizeInMb);


if (!hasPlaylistsId)
{
    // All playlists
    playListsId = -1;
}

if(!hasTotalSize)
{
    throw new ArgumentException($"Invalid max size in MB: {args[1]}");
}

if (string.IsNullOrEmpty(args[2]))
{
    throw new ArgumentException("Missing database connectionstring");
}

if (string.IsNullOrEmpty(args[3]))
{
    throw new ArgumentException("Missing supabase Url");
}

if (string.IsNullOrEmpty(args[4]))
{
    throw new ArgumentException("Missing supabase published key");
}

var databaseString = args[2];
var supabaseUrl = args[3];
var supabaseKey = args[4];

// Optional: resume from this song id onwards, everything with a lower id is skipped
var startSongId = 0;

if (args.Length > 5 && !int.TryParse(args[5], out startSongId))
{
    throw new ArgumentException($"Invalid start song id: {args[5]}");
}

if (startSongId > 0)
{
    Console.WriteLine($"Resuming from song id {startSongId}");
}

var basePathImages = @"/home/admin/mymusicbox_production/music/images";
var basePathAudos = @"/home/admin/mymusicbox_production/";


var playslists = (await GetPlaylist(playListsId > 0 ? playListsId : -1)).ToList();

var options = new Supabase.SupabaseOptions
{
    Schema = "librebeats",
};

var client = new Supabase.Client(supabaseUrl, supabaseKey, options);

var supabase = await client.InitializeAsync();

// Public url of the skeleton placeholder thumbnail, uploaded once on first use
string? defaultThumbnailUrl = null;

foreach (var playlist in playslists)
{
    string publicUrl;

    var thumbnailPath = string.IsNullOrEmpty(playlist.thumbnailpath)
        ? null
        : Path.Combine(basePathImages, playlist.thumbnailpath);

    if (thumbnailPath == null || !File.Exists(thumbnailPath))
    {
        Console.WriteLine($"No thumbnail found for playlist {playlist.name}, using default skeleton thumbnail");
        publicUrl = await GetDefaultThumbnailUrl();
    }
    else
    {
        // Upload thumbnail
        var thumbnailUploadResult = await supabase.Storage.From("image-files").Upload(thumbnailPath, playlist.thumbnailpath, new Supabase.Storage.FileOptions { ContentType = "image/jpeg", Upsert = true });

        publicUrl = supabase.Storage.From("image-files").GetPublicUrl(thumbnailUploadResult.Split("image-files/")[1]);
    }

    // Insert beatmix

    var existingBeatMix = await supabase.From<BeatMix>().Where(i => i.Title == playlist.name).Single();

    int beatMixId = 0;

    // Insert new beatmix
    if (existingBeatMix == null)
    {
        var beatMix = new BeatMix()
        {
            Title = playlist.name,
            Thumbnailpath = publicUrl,
            Creationdate = playlist.creationdate,
            Beatable = playlist.ispublic,
        };

        ModeledResponse<BeatMix> insertResultBeatMix;

        try
        {
            insertResultBeatMix = await supabase.From<BeatMix>().Insert(beatMix);

            if (insertResultBeatMix.ResponseMessage.StatusCode != System.Net.HttpStatusCode.Created)
            {
                Console.WriteLine($"Failed to insert {beatMix.Title}");
                Console.WriteLine(insertResultBeatMix.Content);
                continue;
            }

            beatMixId = insertResultBeatMix.Models.First().Id;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            continue;
        }
    }
    else
    {
        beatMixId = existingBeatMix.Id;
    }

    // Get songs
    var songs = await GetSongs(playlist.id ?? playListsId, totalSizeInMb, startSongId);

    foreach (Song song in songs)
    {
        try
        {
            Console.WriteLine($"Uploading song {song.id}: {song.title}");

            var thumbnailPathSong = Path.Combine(basePathImages, song.thumbnailpath);
            var audioPath = Path.Combine(basePathAudos, song.path);

            // Upload
            var uploadResult = await supabase.Storage.From("image-files").Upload(thumbnailPathSong, song.thumbnailpath, new Supabase.Storage.FileOptions { ContentType = "image/jpeg", Upsert = true });

            // Retrieve public url
            var imagePublicUrl = supabase.Storage.From("image-files").GetPublicUrl(uploadResult.Split("image-files/")[1]);

            // Upload
            uploadResult = await supabase.Storage.From("audio-files").Upload(audioPath, song.path.Split("music/")[1], new Supabase.Storage.FileOptions { ContentType = "audio/ogg", Upsert = true });

            // Retrieve public url
            var audioPublicUrl = supabase.Storage.From("audio-files").GetPublicUrl(uploadResult.Split("audio-files/")[1]);

            // Insert rawbeat
            var rawBeatId = await InsertRawBeat(song);

            // Insert beat
            var beatId = await InsertBeat(song, rawBeatId, imagePublicUrl, audioPublicUrl);

            // Insert beatmixbeat
            await InsertBeatMixBeat(beatId, beatMixId);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to process song {song.id}: {song.title}");
            Console.WriteLine(e);
        }
    }

}

// Uploads a skeleton-screen style placeholder (gray blocks with a shimmer highlight,
// like web lazy-loading skeletons) as a jpeg and returns its public url. Uploaded once per run.
async Task<string> GetDefaultThumbnailUrl()
{
    if (defaultThumbnailUrl != null)
    {
        return defaultThumbnailUrl;
    }

    var uploadResult = await supabase.Storage.From("image-files").Upload(
        CreateSkeletonThumbnailJpeg(),
        "defaults/playlist-skeleton.jpg",
        new Supabase.Storage.FileOptions { ContentType = "image/jpeg", Upsert = true });

    defaultThumbnailUrl = supabase.Storage.From("image-files").GetPublicUrl(uploadResult.Split("image-files/")[1]);

    return defaultThumbnailUrl;
}

static byte[] CreateSkeletonThumbnailJpeg()
{
    const int size = 512;

    // Skeleton blocks are lit by a diagonal shimmer highlight, frozen mid-sweep
    var shimmer = new LinearGradientBrush(
        new PointF(0, 0),
        new PointF(size, size),
        GradientRepetitionMode.None,
        new ColorStop(0f, Color.ParseHex("e2e5e9")),
        new ColorStop(0.45f, Color.ParseHex("f4f6f8")),
        new ColorStop(0.55f, Color.ParseHex("f4f6f8")),
        new ColorStop(1f, Color.ParseHex("e2e5e9")));

    using var image = new Image<Rgb24>(size, size);

    image.Mutate(ctx =>
    {
        ctx.Fill(Color.ParseHex("eceef1"));
        FillRoundedRect(ctx, shimmer, 32, 32, 448, 288, 16);
        FillRoundedRect(ctx, shimmer, 32, 352, 320, 28, 14);
        FillRoundedRect(ctx, shimmer, 32, 400, 240, 28, 14);
        FillRoundedRect(ctx, shimmer, 32, 448, 160, 28, 14);
    });

    using var stream = new MemoryStream();
    image.Save(stream, new JpegEncoder { Quality = 90 });

    return stream.ToArray();
}

// Rounded rectangle as a union of two rectangles and four corner circles
static void FillRoundedRect(IImageProcessingContext ctx, Brush brush, float x, float y, float w, float h, float r)
{
    ctx.Fill(brush, new RectangularPolygon(x + r, y, w - 2 * r, h));
    ctx.Fill(brush, new RectangularPolygon(x, y + r, w, h - 2 * r));
    ctx.Fill(brush, new EllipsePolygon(x + r, y + r, r));
    ctx.Fill(brush, new EllipsePolygon(x + w - r, y + r, r));
    ctx.Fill(brush, new EllipsePolygon(x + r, y + h - r, r));
    ctx.Fill(brush, new EllipsePolygon(x + w - r, y + h - r, r));
}

async Task<int> InsertBeatMixBeat(int beatId, int beatMixId)
{
    // Already linked (e.g. resuming a previous run), the composite PK would reject a duplicate
    var existing = await supabase.From<BeatMixBeat>().Where(i => i.Beatid == beatId && i.Beatmixid == beatMixId).Get();

    if (existing.Models.Count > 0)
    {
        return existing.Models.First().Beatmixid;
    }

    var beatmix = new BeatMixBeat
    {
        Beatid = beatId,
        Beatmixid = beatMixId
    };

    var insertResultBeatMixBeat = await supabase.From<BeatMixBeat>().Insert(beatmix, options: new Supabase.Postgrest.QueryOptions { Returning = Supabase.Postgrest.QueryOptions.ReturnType.Representation });

    if (insertResultBeatMixBeat.ResponseMessage.StatusCode != System.Net.HttpStatusCode.Created)
    {
        throw new Exception($"Failed to insert BeatMixBeat: {insertResultBeatMixBeat.Content}");
    }

    return insertResultBeatMixBeat.Models.First().Beatmixid;
}

async Task<int> InsertBeat(Song song,int rawBeatId, string thumbnailPublicUrl, string audioPublicUrl) 
{
    var existingBeat = await supabase.From<Beat>().Where(i => i.rawbeatid == rawBeatId).Single();

    if (existingBeat != null)
    {
        // Fix missing url path instead of filesystem path
        if (existingBeat.thumbnailurl.Contains("/home/admin/mymusicbox_production/"))
        {
            // GRANT UPDATE ON librebeats.beat TO service_role
            existingBeat.thumbnailurl = thumbnailPublicUrl;

            await supabase.From<Beat>().Update(existingBeat, options: new Supabase.Postgrest.QueryOptions { Returning = Supabase.Postgrest.QueryOptions.ReturnType.Representation });
        }

        return existingBeat.id;
    }

    var beat = new Beat
    {
        thumbnailurl = thumbnailPublicUrl,
        streamingurl = audioPublicUrl,
        title = song.title,
        published = true,
        artist = song.title,
        rawbeatid = rawBeatId,
        tags = string.Empty,
    };

    var insertResultBeat = await supabase.From<Beat>().Insert(beat, options: new Supabase.Postgrest.QueryOptions { Returning = Supabase.Postgrest.QueryOptions.ReturnType.Representation });

    if (insertResultBeat.ResponseMessage.StatusCode != System.Net.HttpStatusCode.Created)
    {
        throw new Exception($"Failed to insert Beat: {insertResultBeat.Content}");
    }

    return insertResultBeat.Models.First().id;
}

async Task<int> InsertRawBeat(Song song)
{
    var audioLocation = $"audio-files/{song.sourceid}.opus";

    var existingRawBeat = await supabase.From<RawBeat>().Where(i => i.AudioLocation == audioLocation).Single();

    if (existingRawBeat != null)
    {
        return existingRawBeat.Id;
    }

    var rawBeat = new RawBeat
    {
        Source = $"https://www.youtube.com/watch?v={song.sourceid}",
        Thumbnaillocation = $"image-files/{song.thumbnailpath}",
        AudioLocation = audioLocation,
        Duration = song.duration,
        Createddate = song.createdat,
    };
    // Insert beat

    var insertResultRawBeat = await supabase.From<RawBeat>().Insert(rawBeat, options: new Supabase.Postgrest.QueryOptions { Returning = Supabase.Postgrest.QueryOptions.ReturnType.Representation });

    if (insertResultRawBeat.ResponseMessage.StatusCode != System.Net.HttpStatusCode.Created)
    {
        throw new Exception($"Failed to insert RawBeat: {insertResultRawBeat.Content}");
    }

    return insertResultRawBeat.Models.First().Id;
}

async Task<IEnumerable<Song>> GetSongs(int playlistId, long maxSizeInMb, int startSongId = 0, long maxFileSizeInMb = 50)
{
    var maxBytes = maxSizeInMb * 1024 * 1024 / playslists.Count;
    var maxFileBytes = maxFileSizeInMb * 1024 * 1024;
    long currentBytes = 0;
    var allowedSongs = new List<Song>();
    var query = @"SELECT s.id, s.name as title, s.path, s.thumbnailpath, s.duration, s.sourceid, s.createdat FROM song s
                   INNER JOIN playlistsong ps on ps.songid = s.id
                   where ps.playlistid = @playlistId and s.id >= @startSongId
                   order by s.id";

    await using var conn = new NpgsqlConnection(databaseString);
    await conn.OpenAsync();

    var songs = await conn.QueryAsync<Song>(query, new { playlistId, startSongId });

    foreach (Song song in songs) 
    {
        var thumbnailPathSong = Path.Combine(basePathImages, song.thumbnailpath);
        var audioPath = Path.Combine(basePathAudos, song.path);

        if (!File.Exists(thumbnailPathSong))
        {
            Console.WriteLine($"Could not find thumbnail path for: {thumbnailPathSong}");
            continue;
        }

        if (!File.Exists(audioPath))
        {
            Console.WriteLine($"Could not find audio path for: {audioPath}");
            continue;
        }

        var totalBytes = new FileInfo(thumbnailPathSong).Length + new FileInfo(audioPath).Length;

        if (totalBytes > maxFileBytes) 
        {
            Console.WriteLine($"Song {song.title} exceeds {maxFileSizeInMb}mb limit, skipping");
            continue;
        }

        if (totalBytes + currentBytes > maxBytes)
        {
            Console.WriteLine("Reached max size, stopping");
            break;
        }

        allowedSongs.Add(song);
        currentBytes += totalBytes;
    }

    return allowedSongs;
}

async Task<IEnumerable<Playlist>> GetPlaylist(int id = -1)
{
    await using var conn = new NpgsqlConnection(databaseString);

    string query;

    if (id == -1)
    {
        // Get all playlist
        query = @"SELECT p.name, p.id, p.thumbnailpath, p.description, p.ispublic, p.creationdate, COUNT(s.id) AS songCount
                  FROM playlistsong ps
                  INNER JOIN playlist p ON p.id = ps.playlistid
                  INNER JOIN song s ON s.id = ps.songid
                  WHERE p.id > 1
                  GROUP BY p.name, p.id
                  ORDER BY songCount DESC, p.name;";


    }
    else
    {
        query = @"SELECT p.name, p.id, p.thumbnailpath, p.description, p.ispublic, p.creationdate, COUNT(s.id) AS songCount
                  FROM playlistsong ps
                  INNER JOIN playlist p ON p.id = ps.playlistid
                  INNER JOIN song s ON s.id = ps.songid
                  WHERE p.id = @id
                  GROUP BY p.name, p.id
                  ORDER BY songCount DESC, p.name;";

    }
    await conn.OpenAsync();

    return await conn.QueryAsync<Playlist>(query, new { id });
}
