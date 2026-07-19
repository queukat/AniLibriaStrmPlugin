using System.Text.Json.Serialization;
using AniLibertyStrmPlugin.Utils;

namespace AniLibertyStrmPlugin.Models;

internal sealed class AniLibertyPopularityDocument
{
    public const string FileName = "aniliberty-popularity.json";
    public const string SourceName = "AniLiberty";
    public const string FavoritesMetric = "added_in_users_favorites";

    [JsonPropertyName("generatedBy")]
    public string GeneratedBy { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("metric")]
    public string Metric { get; set; } = string.Empty;

    [JsonPropertyName("releaseId")]
    public int ReleaseId { get; set; }

    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;

    [JsonPropertyName("favorites")]
    public int? Favorites { get; set; }

    [JsonPropertyName("collections")]
    public AniLibertyCollectionCounts Collections { get; set; } = new();

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public static AniLibertyPopularityDocument FromRelease(ReleaseResponse release)
        => new()
        {
            GeneratedBy = ManagedLibraryManifest.GeneratedXmlMarker,
            Source = SourceName,
            Metric = FavoritesMetric,
            ReleaseId = release.Id,
            Alias = release.Alias,
            Favorites = release.AddedInUsersFavorites,
            Collections = new AniLibertyCollectionCounts
            {
                Planned = release.AddedInPlannedCollection,
                Watched = release.AddedInWatchedCollection,
                Watching = release.AddedInWatchingCollection,
                Postponed = release.AddedInPostponedCollection,
                Abandoned = release.AddedInAbandonedCollection
            },
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };

    public bool HasAnyPopularityCount()
        => Favorites.HasValue ||
           Collections.Planned.HasValue ||
           Collections.Watched.HasValue ||
           Collections.Watching.HasValue ||
           Collections.Postponed.HasValue ||
           Collections.Abandoned.HasValue;

    public bool IsPluginGenerated()
        => string.Equals(Source, SourceName, StringComparison.Ordinal) &&
           string.Equals(Metric, FavoritesMetric, StringComparison.Ordinal) &&
           GeneratedBy.Contains(ManagedLibraryManifest.GeneratedXmlMarker, StringComparison.Ordinal);
}

internal sealed class AniLibertyCollectionCounts
{
    [JsonPropertyName("planned")]
    public int? Planned { get; set; }

    [JsonPropertyName("watched")]
    public int? Watched { get; set; }

    [JsonPropertyName("watching")]
    public int? Watching { get; set; }

    [JsonPropertyName("postponed")]
    public int? Postponed { get; set; }

    [JsonPropertyName("abandoned")]
    public int? Abandoned { get; set; }
}

internal sealed class AniLibertyPopularityResponse
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = AniLibertyPopularityDocument.SourceName;

    [JsonPropertyName("metric")]
    public string Metric { get; set; } = AniLibertyPopularityDocument.FavoritesMetric;

    [JsonPropertyName("label")]
    public string Label { get; set; } = "Popularity count";

    [JsonPropertyName("releaseId")]
    public int ReleaseId { get; set; }

    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int? Count { get; set; }

    [JsonPropertyName("collections")]
    public AniLibertyCollectionCounts Collections { get; set; } = new();

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; set; }

    [JsonPropertyName("iconUrl")]
    public string IconUrl { get; set; } = "/AniLibertyMetadata/Assets/aniliberty-rating.png";

    public static AniLibertyPopularityResponse FromDocument(AniLibertyPopularityDocument document)
        => new()
        {
            ReleaseId = document.ReleaseId,
            Alias = document.Alias,
            Count = document.Favorites,
            Collections = document.Collections,
            GeneratedAtUtc = document.GeneratedAtUtc
        };
}
