using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AniLibertyStrmPlugin.Configuration;
using AniLibertyStrmPlugin.Models;
using AniLibertyStrmPlugin.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class ManagedMirrorTests
{
    [Fact]
    public async Task GenerateTitles_DoesNotOverwriteExistingUnmarkedNfo()
    {
        var outDir = NewTempDir();
        try
        {
            var showDir = Path.Combine(outDir, "demo show");
            Directory.CreateDirectory(showDir);
            var tvshowNfo = Path.Combine(showDir, "tvshow.nfo");
            await File.WriteAllTextAsync(tvshowNfo, "<tvshow><title>Custom</title></tvshow>");

            await NewGenerator().GenerateTitlesAsync(
                new[] { BuildRelease(description: "Generated description") },
                outDir,
                "1080",
                progress: null,
                token: CancellationToken.None);

            var content = await File.ReadAllTextAsync(tvshowNfo);
            Assert.Equal("<tvshow><title>Custom</title></tvshow>", content);
        }
        finally
        {
            DeleteTempDir(outDir);
        }
    }

    [Fact]
    public async Task GenerateTitles_DoesNotOverwriteExistingUnmarkedEpisodeAndSeasonNfo()
    {
        var outDir = NewTempDir();
        try
        {
            var seasonDir = Path.Combine(outDir, "demo show", "Season 01");
            Directory.CreateDirectory(seasonDir);
            var seasonNfo = Path.Combine(seasonDir, "season.nfo");
            var episodeNfo = Path.Combine(seasonDir, "S01E01.nfo");
            await File.WriteAllTextAsync(seasonNfo, "<season><title>Custom Season</title></season>");
            await File.WriteAllTextAsync(episodeNfo, "<episodedetails><title>Custom Episode</title></episodedetails>");

            await NewGenerator().GenerateTitlesAsync(
                new[] { BuildRelease(episodeName: "Generated Episode") },
                outDir,
                "1080",
                progress: null,
                token: CancellationToken.None);

            Assert.Equal("<season><title>Custom Season</title></season>", await File.ReadAllTextAsync(seasonNfo));
            Assert.Equal("<episodedetails><title>Custom Episode</title></episodedetails>", await File.ReadAllTextAsync(episodeNfo));
        }
        finally
        {
            DeleteTempDir(outDir);
        }
    }

    [Fact]
    public async Task GenerateTitles_RefreshesGeneratedNfoOnNextRun()
    {
        var outDir = NewTempDir();
        try
        {
            var generator = NewGenerator();
            await generator.GenerateTitlesAsync(
                new[] { BuildRelease(episodeName: "Old title") },
                outDir,
                "1080",
                progress: null,
                token: CancellationToken.None);

            await generator.GenerateTitlesAsync(
                new[] { BuildRelease(episodeName: "New title") },
                outDir,
                "1080",
                progress: null,
                token: CancellationToken.None);

            var episodeNfo = Assert.Single(Directory.GetFiles(outDir, "S01E01.nfo", SearchOption.AllDirectories));
            var content = await File.ReadAllTextAsync(episodeNfo);
            Assert.Contains("<title>New title</title>", content, StringComparison.Ordinal);
            Assert.Contains(ManagedLibraryManifest.GeneratedXmlMarker, content, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDir(outDir);
        }
    }

    [Fact]
    public async Task GenerateTitles_TracksGeneratedFilesInManagedManifest()
    {
        var outDir = NewTempDir();
        try
        {
            await NewGenerator().GenerateTitlesAsync(
                new[] { BuildRelease() },
                outDir,
                "1080",
                progress: null,
                token: CancellationToken.None);

            var manifestPath = Path.Combine(outDir, ManagedLibraryManifest.StateDirectoryName, ManagedLibraryManifest.ManifestFileName);
            Assert.True(File.Exists(manifestPath));

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
            Assert.Equal(PluginIdentity.ProductToken, document.RootElement.GetProperty("product").GetString());

            var files = document.RootElement.GetProperty("files").EnumerateArray().ToList();
            Assert.Contains(files, x => HasEntry(x, "demo show/Season 01/S01E01.strm", "strm"));
            Assert.Contains(files, x => HasEntry(x, "demo show/Season 01/S01E01.aniid", "aniid"));
            Assert.Contains(files, x => HasEntry(x, "demo show/tvshow.nfo", "tvshow-nfo"));
            Assert.Contains(files, x => HasEntry(x, "demo show/Season 01/season.nfo", "season-nfo"));
            Assert.Contains(files, x => HasEntry(x, "demo show/Season 01/S01E01.nfo", "episode-nfo"));
            Assert.All(files, x =>
            {
                var relativePath = x.GetProperty("relativePath").GetString() ?? string.Empty;
                Assert.False(Path.IsPathRooted(relativePath));
                Assert.DoesNotContain('\\', relativePath);
            });
        }
        finally
        {
            DeleteTempDir(outDir);
        }
    }

    [Fact]
    public async Task GenerateTitles_WritesGeneratedMarkerIntoNfoFiles()
    {
        var outDir = NewTempDir();
        try
        {
            await NewGenerator().GenerateTitlesAsync(
                new[] { BuildRelease() },
                outDir,
                "1080",
                progress: null,
                token: CancellationToken.None);

            var nfoFiles = Directory.GetFiles(outDir, "*.nfo", SearchOption.AllDirectories);
            Assert.NotEmpty(nfoFiles);
            Assert.All(nfoFiles, path =>
                Assert.Contains(ManagedLibraryManifest.GeneratedXmlMarker, File.ReadAllText(path), StringComparison.Ordinal));
        }
        finally
        {
            DeleteTempDir(outDir);
        }
    }

    [Fact]
    public async Task ManagedManifest_DryRunKeepsStaleFile_DeleteRemovesIt()
    {
        var outDir = NewTempDir();
        try
        {
            var stalePath = Path.Combine(outDir, "old.strm");
            var customPath = Path.Combine(outDir, "custom.strm");
            await File.WriteAllTextAsync(stalePath, "https://example.invalid/old.m3u8");
            await File.WriteAllTextAsync(customPath, "https://example.invalid/custom.m3u8");

            var first = await ManagedLibraryManifest.LoadAsync(outDir, CancellationToken.None);
            first.TrackText(stalePath, "strm", "https://example.invalid/old.m3u8", encoding: null, releaseId: 1, episodeId: null, source: null);
            await first.SaveAsync(StaleCleanupMode.DryRun, CancellationToken.None);

            var dryRun = await ManagedLibraryManifest.LoadAsync(outDir, CancellationToken.None);
            await dryRun.ApplyCleanupAsync(StaleCleanupMode.DryRun, NullLogger.Instance, CancellationToken.None);
            await dryRun.SaveAsync(StaleCleanupMode.DryRun, CancellationToken.None);
            Assert.True(File.Exists(stalePath));

            var delete = await ManagedLibraryManifest.LoadAsync(outDir, CancellationToken.None);
            await delete.ApplyCleanupAsync(StaleCleanupMode.Delete, NullLogger.Instance, CancellationToken.None);
            await delete.SaveAsync(StaleCleanupMode.Delete, CancellationToken.None);
            Assert.False(File.Exists(stalePath));
            Assert.True(File.Exists(customPath));
        }
        finally
        {
            DeleteTempDir(outDir);
        }
    }

    [Fact]
    public async Task ManagedManifest_OffKeepsStaleFileAndManifestEntry()
    {
        var outDir = NewTempDir();
        try
        {
            var stalePath = Path.Combine(outDir, "old.strm");
            await File.WriteAllTextAsync(stalePath, "https://example.invalid/old.m3u8");

            var first = await ManagedLibraryManifest.LoadAsync(outDir, CancellationToken.None);
            first.TrackText(stalePath, "strm", "https://example.invalid/old.m3u8", encoding: null, releaseId: 1, episodeId: null, source: null);
            await first.SaveAsync(StaleCleanupMode.DryRun, CancellationToken.None);

            var off = await ManagedLibraryManifest.LoadAsync(outDir, CancellationToken.None);
            await off.ApplyCleanupAsync(StaleCleanupMode.Off, NullLogger.Instance, CancellationToken.None);
            await off.SaveAsync(StaleCleanupMode.Off, CancellationToken.None);

            Assert.True(File.Exists(stalePath));
            AssertManifestContains(outDir, "old.strm");
        }
        finally
        {
            DeleteTempDir(outDir);
        }
    }

    [Fact]
    public async Task ManagedManifest_DeleteRemovesEmptyGeneratedParents()
    {
        var outDir = NewTempDir();
        try
        {
            var nestedDir = Path.Combine(outDir, "old show", "Season 01");
            Directory.CreateDirectory(nestedDir);
            var stalePath = Path.Combine(nestedDir, "S01E01.strm");
            await File.WriteAllTextAsync(stalePath, "https://example.invalid/old.m3u8");

            var first = await ManagedLibraryManifest.LoadAsync(outDir, CancellationToken.None);
            first.TrackText(stalePath, "strm", "https://example.invalid/old.m3u8", encoding: null, releaseId: 1, episodeId: null, source: null);
            await first.SaveAsync(StaleCleanupMode.DryRun, CancellationToken.None);

            var delete = await ManagedLibraryManifest.LoadAsync(outDir, CancellationToken.None);
            await delete.ApplyCleanupAsync(StaleCleanupMode.Delete, NullLogger.Instance, CancellationToken.None);
            await delete.SaveAsync(StaleCleanupMode.Delete, CancellationToken.None);

            Assert.False(File.Exists(stalePath));
            Assert.False(Directory.Exists(nestedDir));
            Assert.True(File.Exists(Path.Combine(outDir, ManagedLibraryManifest.StateDirectoryName, ManagedLibraryManifest.ManifestFileName)));
        }
        finally
        {
            DeleteTempDir(outDir);
        }
    }

    [Fact]
    public async Task ManagedManifest_FirstRunDoesNotPrune()
    {
        var outDir = NewTempDir();
        try
        {
            var existingPath = Path.Combine(outDir, "existing.strm");
            await File.WriteAllTextAsync(existingPath, "https://example.invalid/existing.m3u8");

            var manifest = await ManagedLibraryManifest.LoadAsync(outDir, CancellationToken.None);
            Assert.True(manifest.IsFirstRun);

            await manifest.ApplyCleanupAsync(StaleCleanupMode.Delete, NullLogger.Instance, CancellationToken.None);
            await manifest.SaveAsync(StaleCleanupMode.Delete, CancellationToken.None);

            Assert.True(File.Exists(existingPath));
        }
        finally
        {
            DeleteTempDir(outDir);
        }
    }

    private static IAniLibertyStrmGenerator NewGenerator()
    {
        return new AniLibertyStrmGenerator(
            NullLogger<AniLibertyStrmGenerator>.Instance,
            serverHost: null!,
            networkManager: null!,
            library: null!,
            chapters: null!,
            client: new StubClient());
    }

    private static ReleaseResponse BuildRelease(string episodeName = "Episode 1", string description = "Description")
    {
        return new ReleaseResponse
        {
            Id = 123,
            Alias = "demo",
            Name = new NameBlock { Main = "Demo Show", English = "Demo Show" },
            Type = new ReleaseType { Value = "TV" },
            Year = 2025,
            Description = description,
            Episodes =
            [
                new EpisodeItem
                {
                    Id = "11111111-1111-1111-1111-111111111111",
                    Ordinal = 1,
                    Name = episodeName,
                    Hls1080 = "/public/hls/demo/one.m3u8"
                }
            ]
        };
    }

    private static string NewTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "alib-managed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDir(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static bool HasEntry(JsonElement entry, string relativePath, string kind)
    {
        return string.Equals(entry.GetProperty("relativePath").GetString(), relativePath, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(entry.GetProperty("kind").GetString(), kind, StringComparison.Ordinal);
    }

    private static void AssertManifestContains(string rootPath, string relativePath)
    {
        var manifestPath = Path.Combine(rootPath, ManagedLibraryManifest.StateDirectoryName, ManagedLibraryManifest.ManifestFileName);
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var files = document.RootElement.GetProperty("files").EnumerateArray();
        Assert.Contains(files, x => string.Equals(x.GetProperty("relativePath").GetString(), relativePath, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class StubClient : IAniLibertyClient
    {
        public Task<string> GetStringWithLoggingAsync(string url, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<string> GetStringAuthAsync(string url, string bearer, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<List<ReleaseResponse>> FetchAllTitlesAsync(int pageSize, int maxPages, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<List<ReleaseResponse>> FetchFavoritesAsync(
            string bearerToken,
            int pageSize,
            int maxPages,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> UpdateViewTimecodesAsync(
            string bearerToken,
            IEnumerable<ViewTimecodeUpdateItem> updates,
            CancellationToken ct)
            => Task.FromResult(true);

        public Task<List<ViewTimecodeEntry>> FetchViewTimecodesAsync(
            string bearerToken,
            DateTimeOffset? since,
            CancellationToken ct)
            => Task.FromResult(new List<ViewTimecodeEntry>());

        public Task<ReleaseResponse?> FetchReleaseByIdAsync(int id, CancellationToken ct)
            => Task.FromResult<ReleaseResponse?>(null);

        public Task<List<FranchiseInfo>?> FetchFranchisesForReleaseAsync(int releaseId, CancellationToken ct)
            => Task.FromResult<List<FranchiseInfo>?>(null);
    }
}
