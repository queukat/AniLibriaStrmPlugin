using System.Text.Json.Serialization;
using AniLibertyStrmPlugin.Converters;

namespace AniLibertyStrmPlugin.Models;

/// <summary>
/// Core AniLiberty API v1 models (minimum required by the plugin).
/// Important:
///  - In v1, <c>year</c> is on the top release level (field "year"), and <c>season</c> does NOT include year
///  - <c>poster</c>/<c>preview</c> uses image schema with preview/thumbnail (+ optional optimized), not just "src"
///  - Episodes provide name/name_english, but no description/plot
/// </summary>
public class ReleaseResponse
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("alias")] public string Alias { get; set; } = string.Empty;

    [JsonPropertyName("type")] public ReleaseType? Type { get; set; }

    // v1: release year is a separate top-level field "year"
    [JsonPropertyName("year")] public int Year { get; set; }

    [JsonPropertyName("name")] public NameBlock Name { get; set; } = new();

    // v1: poster — image(withOptimized): preview/thumbnail (+ optimized)
    [JsonPropertyName("poster")] public ImageBlock Poster { get; set; } = new();

    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;

    // v1: season = { value, description } (no year)
    [JsonPropertyName("season")] public SeasonBlock? Season { get; set; }

    [JsonPropertyName("episodes_total")]
    [JsonConverter(typeof(IntNullableConverter))]
    public int? EpisodesTotal { get; set; }

    [JsonPropertyName("added_in_users_favorites")]
    [JsonConverter(typeof(IntNullableConverter))]
    public int? AddedInUsersFavorites { get; set; }

    [JsonPropertyName("added_in_planned_collection")]
    [JsonConverter(typeof(IntNullableConverter))]
    public int? AddedInPlannedCollection { get; set; }

    [JsonPropertyName("added_in_watched_collection")]
    [JsonConverter(typeof(IntNullableConverter))]
    public int? AddedInWatchedCollection { get; set; }

    [JsonPropertyName("added_in_watching_collection")]
    [JsonConverter(typeof(IntNullableConverter))]
    public int? AddedInWatchingCollection { get; set; }

    [JsonPropertyName("added_in_postponed_collection")]
    [JsonConverter(typeof(IntNullableConverter))]
    public int? AddedInPostponedCollection { get; set; }

    [JsonPropertyName("added_in_abandoned_collection")]
    [JsonConverter(typeof(IntNullableConverter))]
    public int? AddedInAbandonedCollection { get; set; }

    [JsonPropertyName("episodes")] public List<EpisodeItem> Episodes { get; set; } = new();
}

public class SeasonBlock
{
    // winter / spring / summer / autumn
    [JsonPropertyName("value")] public string Value { get; set; } = string.Empty;

    // for example, "Autumn"
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
}

public class NameBlock
{
    [JsonPropertyName("main")] public string Main { get; set; } = string.Empty;
    [JsonPropertyName("english")] public string English { get; set; } = string.Empty;
    [JsonPropertyName("alternative")] public string Alternative { get; set; } = string.Empty;
}

/// <summary>
/// Image schema (commons.v1.models.components.image.withOptimized):
///  - preview / thumbnail
///  - optional optimized (preview/thumbnail)
/// Keep "src" for backward compatibility in case it still appears somewhere.
/// </summary>
public class ImageBlock
{
    [JsonPropertyName("preview")] public string Preview { get; set; } = string.Empty;
    [JsonPropertyName("thumbnail")] public string Thumbnail { get; set; } = string.Empty;

    // legacy / backward compatibility
    [JsonPropertyName("src")] public string Src { get; set; } = string.Empty;

    [JsonPropertyName("optimized")] public ImageBlock? Optimized { get; set; }
}

public class EpisodeItem
{
    // v1: guid/string
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;

    // v1: episode title
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    [JsonPropertyName("name_english")] public string NameEnglish { get; set; } = string.Empty;

    [JsonPropertyName("ordinal")]
    [JsonConverter(typeof(DoubleNullableConverter))]
    public double? Ordinal { get; set; }

    [JsonPropertyName("hls_1080")] public string? Hls1080 { get; set; }
    [JsonPropertyName("hls_720")] public string? Hls720 { get; set; }
    [JsonPropertyName("hls_480")] public string? Hls480 { get; set; }

    // docs: number (seconds)
    public int Duration { get; set; }

    [JsonPropertyName("opening")] public OpeningBlock? Opening { get; set; }
    [JsonPropertyName("ending")] public OpeningBlock? Ending { get; set; }

    // v1: preview is an image schema, not a src string
    [JsonPropertyName("preview")] public ImageBlock? Preview { get; set; }

    [JsonPropertyName("sort_order")]
    [JsonConverter(typeof(IntNullableConverter))]
    public int? SortOrder { get; set; }
}

public sealed class ViewTimecodeUpdateItem
{
    [JsonPropertyName("time")] public double Time { get; set; }
    [JsonPropertyName("is_watched")] public bool IsWatched { get; set; }
    [JsonPropertyName("release_episode_id")] public string ReleaseEpisodeId { get; set; } = string.Empty;
}

public sealed class ViewTimecodeEntry
{
    public double Time { get; set; }
    public bool IsWatched { get; set; }
    public string ReleaseEpisodeId { get; set; } = string.Empty;
}

public class OpeningBlock
{
    [JsonPropertyName("start")] public int? Start { get; set; }
    [JsonPropertyName("stop")] public int? Stop { get; set; }
}

/* ─────────────────────────────────────────────────────────────
   Franchises (minimum data needed from /anime/franchises/release/{id})
   ───────────────────────────────────────────────────────────── */

public class FranchiseInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    [JsonPropertyName("franchise_releases")]
    public List<FranchiseReleaseLink> FranchiseReleases { get; set; } = new();
}

public class FranchiseReleaseLink
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;

    [JsonPropertyName("sort_order")]
    [JsonConverter(typeof(IntNullableConverter))]
    public int? SortOrder { get; set; }

    [JsonPropertyName("release_id")] public int ReleaseId { get; set; }
    [JsonPropertyName("franchise_id")] public string FranchiseId { get; set; } = string.Empty;

    [JsonPropertyName("release")] public FranchiseReleaseRef? Release { get; set; }
}

public class FranchiseReleaseRef
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("type")] public ReleaseType? Type { get; set; }

    // Response can contain year directly at top level
    [JsonPropertyName("year")]
    [JsonConverter(typeof(IntNullableConverter))]
    public int? Year { get; set; }

    [JsonPropertyName("name")] public NameBlock? Name { get; set; }
}

public class ReleaseType
{
    // enum: TV, ONA, WEB, OVA, OAD, MOVIE, DORAMA, SPECIAL
    [JsonPropertyName("value")] public string Value { get; set; } = string.Empty;

    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
}
