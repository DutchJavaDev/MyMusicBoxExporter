using Npgsql;
using Dapper;
using Exporter.Data;
using Supabase.Postgrest.Responses;


// Export data based on max size for a playlist
// Example: max 500MB, keep uploading until you have reached 500mb in audio/image files
// Args: playlistid 1 totalsize 500 dbconnection db supabaseurl url supabasesecretkey key

var playListsId = int.Parse(args[0]);
var totalSizeInMb = long.Parse(args[1]);
var databaseString = args[2];
var supabaseUrl = args[3];
var supabaseKey = args[4];
var basePathImages = @"/home/admin/mymusicbox_production/music/images";
var basePathAudos = @"/home/admin/mymusicbox_production/";


var playslists = (await GetPlaylist(playListsId > 0 ? playListsId : -1)).ToList();

var options = new Supabase.SupabaseOptions
{
    Schema = "librebeats"
};
var client = new Supabase.Client(supabaseUrl, supabaseKey, options);

var supabase = await client.InitializeAsync();

foreach (var playlist in playslists)
{
    // Get thumbnail
    var thumbnailPath = Path.Combine(basePathImages, playlist.thumbnailpath);

    if (!File.Exists(thumbnailPath))
    {
        Console.WriteLine($"Unable to find thumbnail for playlist {playlist.name} at location {thumbnailPath}");
        Console.WriteLine("Skipping");
        continue;
    }

    // Upload thumbnail
    var uploadResult = await supabase.Storage.From("image-files").Upload(thumbnailPath, playlist.thumbnailpath, new Supabase.Storage.FileOptions { ContentType = "image/jpeg", Upsert = true });

    var publicUrl = supabase.Storage.From("image-files").GetPublicUrl(uploadResult.Split("image-files/")[1]);

    // Insert beatmix

    var existingBeatMix = supabase.From<BeatMix>().Where(i => i.Title == playlist.name).Single();

    int beatMixId = 0;

    // Insert new beatmix
    if (existingBeatMix.Result == null)
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
        beatMixId = existingBeatMix.Result.Id;
    }

    // Get songs
    var songs = await GetSongs(playListsId, totalSizeInMb);

    foreach (Song song in songs) 
    {
        var thumbnailPathSong = Path.Combine(basePathImages, song.thumbnailpath);
        var audioPath = Path.Combine(basePathAudos, song.path);

        // Upload
        uploadResult = await supabase.Storage.From("image-files").Upload(thumbnailPathSong, song.thumbnailpath, new Supabase.Storage.FileOptions { ContentType = "image/jpeg", Upsert = true });

        // Retrieve public url
        var imagePublicUrl = supabase.Storage.From("image-files").GetPublicUrl(uploadResult.Split("image-files/")[1]);

        // Upload
        uploadResult = await supabase.Storage.From("audio-files").Upload(audioPath, song.path.Split("music/")[1], new Supabase.Storage.FileOptions { ContentType = "audio/ogg", Upsert = true });

        // Retrieve public url
        var audioPublicUrl = supabase.Storage.From("audio-files").GetPublicUrl(uploadResult.Split("audio-files/")[1]);

        // Insert rawbeat
        var rawBeatId = await InsertRawBeat(song);

        // Insert beat
        var beatId = await InsertBeat(song, rawBeatId, thumbnailPathSong, audioPublicUrl);

        // inser beatmixbeat
        var beatmixbeat = await InsertBeatMixBeat(beatId, beatMixId);
    }

}

async Task<int> InsertBeatMixBeat(int beatId, int rawbeatId)
{
    var beatmix = new BeatMixBeat
    {
        Beatid = beatId,
        Beatmixid = rawbeatId
    };

    var insertResultBeatMixBeat = await supabase.From<BeatMixBeat>().Insert(beatmix, options: new Supabase.Postgrest.QueryOptions { Returning = Supabase.Postgrest.QueryOptions.ReturnType.Representation });

    if (insertResultBeatMixBeat.ResponseMessage.StatusCode != System.Net.HttpStatusCode.Created)
    {
        Console.WriteLine("Failed to insert BeatMixBeat");
        Console.WriteLine(insertResultBeatMixBeat.Content);

    }

    return insertResultBeatMixBeat.Models.First().Beatmixid;
}

async Task<int> InsertBeat(Song song,int rawBeatId, string thumbnailPublicUrl, string audioPublicUrl) 
{
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
        Console.WriteLine("Failed to insert Beat");
        Console.WriteLine(insertResultBeat.Content);
    }

    return insertResultBeat.Models.First().id;
}

async Task<int> InsertRawBeat(Song song)
{
    var rawBeat = new RawBeat
    {
        Source = $"https://www.youtube.com/watch?v={song.sourceid}",
        Thumbnaillocation = $"image-files/{song.thumbnailpath}",
        AudioLocation = $"audio-files/{song.sourceid}.opus",
        Duration = song.duration,
        Createddate = song.createdat,
    };
    // Insert beat

    var insertResultRawBeat = await supabase.From<RawBeat>().Insert(rawBeat, options: new Supabase.Postgrest.QueryOptions { Returning = Supabase.Postgrest.QueryOptions.ReturnType.Representation });

    if (insertResultRawBeat.ResponseMessage.StatusCode != System.Net.HttpStatusCode.Created)
    {
        Console.WriteLine("Failed to insert rawbeat");
        Console.WriteLine(insertResultRawBeat.Content);
    }

    return insertResultRawBeat.Models.First().Id;
}

async Task<IEnumerable<Song>> GetSongs(int playlistId, long maxSizeInMb, long maxFileSizeInMb = 50) 
{
    var maxBytes = maxSizeInMb * 1024 * 1024 / playslists.Count;
    var maxFileBytes = maxFileSizeInMb * 1024 * 1024;
    long currentBytes = 0;
    var allowedSongs = new List<Song>();
    var query = @$"SELECT s.name as title, s.path, s.thumbnailpath, s.duration, s.sourceid, s.createdat FROM song s
                   INNER JOIN playlistsong ps on ps.songid = s.id
                   where ps.playlistid = {playlistId}";

    await using var conn = new NpgsqlConnection(databaseString);
    await conn.OpenAsync();

    var songs = await conn.QueryAsync<Song>(query);

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
            Console.WriteLine($"Could not find audio path for: {thumbnailPathSong}");
            continue;
        }

        var totalBytes = new FileInfo(thumbnailPathSong).Length + new FileInfo(audioPath).Length;

        if (totalBytes > maxFileBytes) 
        {
            Console.WriteLine($"Song {song.title} exceeds 50mb limit, skipping");
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
        query = @"SELECT p.name, p.id, p.name, p.thumbnailpath, p.description p.ispublic, p.creationdate, COUNT(s.id) AS songCount
                  FROM playlistsong ps
                  INNER JOIN playlist p ON p.id = ps.playlistid
                  INNER JOIN song s ON s.id = ps.songid
                  WHERE p.id > 1
                  GROUP BY p.name, p.id
                  ORDER BY songCount DESC, p.name;";


    }
    else
    {
        query = @$"SELECT p.name, p.id, p.name, p.thumbnailpath, p.description, p.ispublic, p.creationdate, COUNT(s.id) AS songCount
                  FROM playlistsong ps
                  INNER JOIN playlist p ON p.id = ps.playlistid
                  INNER JOIN song s ON s.id = ps.songid
                  WHERE p.id = {id}
                  GROUP BY p.name, p.id
                  ORDER BY songCount DESC, p.name;";

    }
    await conn.OpenAsync();

    return await conn.QueryAsync<Playlist>(query); ;
}
