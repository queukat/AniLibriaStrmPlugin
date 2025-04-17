// ========= File: AniLibriaStrmGenerator.cs =========
using AniLibriaStrmPlugin.Models;
using MediaBrowser.Controller.Library;

using Microsoft.Extensions.Logging;
using System.Security;
using System.Text;
using MediaBrowser.Model.Entities;

namespace AniLibriaStrmPlugin;

public interface IAniLibriaStrmGenerator
{
    Task GenerateTitlesAsync(IEnumerable<TitleResponse> titles, string basePath, string resolution,
                             IProgress<double>? progress, CancellationToken token);
}

public sealed class AniLibriaStrmGenerator : IAniLibriaStrmGenerator
{
    private readonly ILogger<AniLibriaStrmGenerator> _log;
    private readonly ILibraryManager _library;

    public AniLibriaStrmGenerator(ILogger<AniLibriaStrmGenerator> log,
                                  ILibraryManager library)
    {
        _log     = log;
        _library = library;
    }

    public async Task GenerateTitlesAsync(IEnumerable<TitleResponse> titles, string basePath, string resolution,
                                          IProgress<double>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(basePath);

        var list   = titles.ToList();
        var total  = list.Count;
        var current = 0;

        foreach (var t in list)
        {
            token.ThrowIfCancellationRequested();
            current++;

            _log.LogInformation("({Cur}/{Tot})  {Name}", current, total, t.Names?.Ru ?? t.Code);
            await GenerateStrmForTitle(t, basePath, resolution, token);
            progress?.Report(current / (double)total * 100);
        }
    }

    // ---------------------------------------------------------------------
    private async Task GenerateStrmForTitle(TitleResponse title, string basePath, string resolution,
                                            CancellationToken token)
    {
        var seasonNum = title.Franchises?
                            .SelectMany(f => f.Releases ?? new())
                            .FirstOrDefault(r => r.Id == title.Id)?.Ordinal ?? 1;

        var safeShowName = MakeSafe(title.Names?.Ru ?? title.Code ?? $"Title_{title.Id}");
        var showDir   = Path.Combine(basePath, safeShowName);
        var seasonDir = Path.Combine(showDir, $"Season {seasonNum}");
        Directory.CreateDirectory(seasonDir);

        // poster
        await DownloadIfAbsentAsync("https://www.anilibria.tv" + (title.Posters?.Original?.Url ?? ""),
                                    Path.Combine(showDir, "poster.jpg"), token);

        // tvshow.nfo
        var tvshowNfo = Path.Combine(showDir, "tvshow.nfo");
        if (!File.Exists(tvshowNfo))
            await File.WriteAllTextAsync(tvshowNfo,
                $@"<tvshow><title>{MakeSafeXml(safeShowName)}</title></tvshow>",
                Encoding.UTF8, token);

        // episodes
        var lastEp = title.Player?.Episodes?.Last ?? 1;
        for (int ep = 1; ep <= lastEp; ep++)
        {
            token.ThrowIfCancellationRequested();

            var epKey  = ep.ToString();
            var epInfo = title.Player?.List?.GetValueOrDefault(epKey);
            var strm   = Path.Combine(seasonDir, $"S{seasonNum:00}E{ep:00}.strm");

            if (!File.Exists(strm))
            {
                var url = epInfo?.Hls != null
                    ? ChooseHls(epInfo.Hls, resolution, title.Player?.Host)
                    : "https://example.org/no-episode";

                await File.WriteAllTextAsync(strm, url ?? "", token);
            }

            // preview
            await DownloadIfAbsentAsync("https://www.anilibria.tv" + (epInfo?.Preview ?? ""),
                                        Path.Combine(seasonDir, $"S{seasonNum:00}E{ep:00}-preview.jpg"),
                                        token);

            // edl + intro‑chapter
            if (epInfo?.Skips is { } skips && skips.Opening?.Count >= 2)
            {
                var startSec = skips.Opening[0];
                var endSec   = skips.Opening[1];

                // 1) EDL
                var edlPath = Path.ChangeExtension(strm, ".edl");
                if (!File.Exists(edlPath))
                    await File.WriteAllLinesAsync(edlPath,
                        new[] { $"{startSec} {endSec} 0" }, token);

                // 2) Native chapter marker (Jellyfin 10.11+)
            //     try
            //     {
            //         var item = _library.GetItemByPath(strm) as MediaBrowser.Controller.Entities.Video;
            //         if (item != null)
            //         {
            //             var chapters = new[]
            //             {
            //                 new ChapterInfo
            //                 {
            //                     Name = "Intro",
            //                     StartPositionTicks = TimeSpan.FromSeconds(startSec).Ticks,
            //                     EndPositionTicks   = TimeSpan.FromSeconds(endSec).Ticks
            //                 }
            //             };
            //             await _library.UpdateChaptersAsync(item, chapters, CancellationToken.None);
            //         }
            //     }
            //     catch (Exception ex)
            //     {
            //         _log.LogWarning(ex, "Unable to set Intro chapter for {File}", strm);
            //     }
            }

            // episode nfo
            var nfoPath = Path.ChangeExtension(strm, ".nfo");
            if (!File.Exists(nfoPath))
            {
                var epTitle = string.IsNullOrWhiteSpace(epInfo?.Name) ? $"Episode {ep}" : epInfo!.Name;
                var xml = $@"<?xml version=""1.0"" encoding=""utf-8"" standalone=""yes""?>
<episodedetails>
  <title>{MakeSafeXml(epTitle)}</title>
  <season>{seasonNum}</season>
  <episode>{ep}</episode>
  <plot></plot>
  <showtitle>{MakeSafeXml(title.Names?.Ru ?? title.Code ?? "")}</showtitle>
</episodedetails>";
                await File.WriteAllTextAsync(nfoPath, xml, Encoding.UTF8, token);
            }
        }
    }

    // ---------------------------------------------------------------------
    private static async Task DownloadIfAbsentAsync(string url, string path, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(url) || File.Exists(path)) return;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var bytes = await http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(path, bytes, ct);
        }
        catch { /* не критично */ }
    }

    // helpers --------------------------------------------------------------
    private static string? ChooseHls(HlsBlock? hls, string pref, string? host)
    {
        if (hls == null || string.IsNullOrEmpty(host)) return null;
        var link = pref switch
        {
            "1080" => hls.Fhd ?? hls.Hd ?? hls.Sd,
            "720"  => hls.Hd  ?? hls.Fhd ?? hls.Sd,
            "480"  => hls.Sd  ?? hls.Hd  ?? hls.Fhd,
            _      => hls.Hd  ?? hls.Sd  ?? hls.Fhd
        };
        return link is null ? null : $"https://{host}{link}";
    }

    private static string MakeSafe(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim();
    }

    private static string MakeSafeXml(string? text) => SecurityElement.Escape(text) ?? "";
}
