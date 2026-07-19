using System.Text.Json;
using AniLibertyStrmPlugin.Models;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class AniLibertyModelsSerializationTests
{
    [Fact]
    public void ReleaseResponse_DeserializesNestedApiShape()
    {
        const string json = """
            {
              "id": 77,
              "alias": "alias-value",
              "type": { "value": "TV", "description": "TV series" },
              "year": 2026,
              "name": { "main": "Main", "english": "English", "alternative": "Alt" },
              "poster": {
                "preview": "/preview.webp",
                "thumbnail": "/thumb.webp",
                "src": "/legacy.jpg",
                "optimized": { "preview": "/opt-preview.webp", "thumbnail": "/opt-thumb.webp" }
              },
              "description": "Description",
              "season": { "value": "spring", "description": "Spring" },
              "episodes_total": "12.9",
              "added_in_users_favorites": "3456.7",
              "added_in_planned_collection": "100.8",
              "added_in_watched_collection": "200.9",
              "added_in_watching_collection": "300.1",
              "added_in_postponed_collection": "400.2",
              "added_in_abandoned_collection": "500.3",
              "episodes": [
                {
                  "id": "episode-id",
                  "name": "Episode",
                  "name_english": "Episode EN",
                  "ordinal": "1.5",
                  "hls_1080": "https://cdn/1080.m3u8",
                  "hls_720": "https://cdn/720.m3u8",
                  "hls_480": "https://cdn/480.m3u8",
                  "Duration": 1440,
                  "opening": { "start": 10, "stop": 90 },
                  "ending": { "start": 1300, "stop": 1400 },
                  "preview": { "preview": "/episode-preview.webp", "thumbnail": "/episode-thumb.webp" },
                  "sort_order": "2.8"
                }
              ]
            }
            """;

        var release = JsonSerializer.Deserialize<ReleaseResponse>(json);

        Assert.NotNull(release);
        Assert.Equal(77, release.Id);
        Assert.Equal("alias-value", release.Alias);
        Assert.Equal("TV", release.Type?.Value);
        Assert.Equal("TV series", release.Type?.Description);
        Assert.Equal(2026, release.Year);
        Assert.Equal("Main", release.Name.Main);
        Assert.Equal("English", release.Name.English);
        Assert.Equal("Alt", release.Name.Alternative);
        Assert.Equal("/preview.webp", release.Poster.Preview);
        Assert.Equal("/thumb.webp", release.Poster.Thumbnail);
        Assert.Equal("/legacy.jpg", release.Poster.Src);
        Assert.Equal("/opt-preview.webp", release.Poster.Optimized?.Preview);
        Assert.Equal("/opt-thumb.webp", release.Poster.Optimized?.Thumbnail);
        Assert.Equal("Description", release.Description);
        Assert.Equal("spring", release.Season?.Value);
        Assert.Equal("Spring", release.Season?.Description);
        Assert.Equal(12, release.EpisodesTotal);
        Assert.Equal(3456, release.AddedInUsersFavorites);
        Assert.Equal(100, release.AddedInPlannedCollection);
        Assert.Equal(200, release.AddedInWatchedCollection);
        Assert.Equal(300, release.AddedInWatchingCollection);
        Assert.Equal(400, release.AddedInPostponedCollection);
        Assert.Equal(500, release.AddedInAbandonedCollection);

        var episode = Assert.Single(release.Episodes);
        Assert.Equal("episode-id", episode.Id);
        Assert.Equal("Episode", episode.Name);
        Assert.Equal("Episode EN", episode.NameEnglish);
        Assert.Equal(1.5, episode.Ordinal);
        Assert.Equal("https://cdn/1080.m3u8", episode.Hls1080);
        Assert.Equal("https://cdn/720.m3u8", episode.Hls720);
        Assert.Equal("https://cdn/480.m3u8", episode.Hls480);
        Assert.Equal(1440, episode.Duration);
        Assert.Equal(10, episode.Opening?.Start);
        Assert.Equal(90, episode.Opening?.Stop);
        Assert.Equal(1300, episode.Ending?.Start);
        Assert.Equal(1400, episode.Ending?.Stop);
        Assert.Equal("/episode-preview.webp", episode.Preview?.Preview);
        Assert.Equal("/episode-thumb.webp", episode.Preview?.Thumbnail);
        Assert.Equal(2, episode.SortOrder);
    }

    [Fact]
    public void FranchiseInfo_DeserializesReleaseLinks()
    {
        const string json = """
            {
              "id": "franchise-id",
              "name": "Franchise",
              "franchise_releases": [
                {
                  "id": "link-id",
                  "sort_order": "3.4",
                  "release_id": 77,
                  "franchise_id": "franchise-id",
                  "release": {
                    "id": 77,
                    "type": { "value": "MOVIE", "description": "Movie" },
                    "year": "2025",
                    "name": { "main": "Movie main", "english": "Movie EN" }
                  }
                }
              ]
            }
            """;

        var franchise = JsonSerializer.Deserialize<FranchiseInfo>(json);

        Assert.NotNull(franchise);
        Assert.Equal("franchise-id", franchise.Id);
        Assert.Equal("Franchise", franchise.Name);

        var link = Assert.Single(franchise.FranchiseReleases);
        Assert.Equal("link-id", link.Id);
        Assert.Equal(3, link.SortOrder);
        Assert.Equal(77, link.ReleaseId);
        Assert.Equal("franchise-id", link.FranchiseId);
        Assert.Equal(77, link.Release?.Id);
        Assert.Equal("MOVIE", link.Release?.Type?.Value);
        Assert.Equal("Movie", link.Release?.Type?.Description);
        Assert.Equal(2025, link.Release?.Year);
        Assert.Equal("Movie main", link.Release?.Name?.Main);
        Assert.Equal("Movie EN", link.Release?.Name?.English);
    }
}
