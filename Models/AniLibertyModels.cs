
using System.Text.Json.Serialization;
using AniLibertyStrmPlugin.Converters;

namespace AniLibertyStrmPlugin.Models;

public class ReleaseResponse
{
    [JsonPropertyName("id")]      public int    Id          { get; set; }
    [JsonPropertyName("alias")]   public string Alias       { get; set; } = string.Empty;

    [JsonPropertyName("name")]    public NameBlock   Name   { get; set; } = null!;
    [JsonPropertyName("poster")]  public PosterBlock Poster { get; set; } = null!;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;

    [JsonPropertyName("season")]  public SeasonBlock? Season { get; set; }

    [JsonPropertyName("episodes_total")]
    [JsonConverter(typeof(IntNullableConverter))]
    public int? EpisodesTotal { get; set; }

    [JsonPropertyName("episodes")] public List<EpisodeItem> Episodes { get; set; } = new();
}

public class SeasonBlock
{
    // winter / spring / summer / autumn  (. /anime/catalog/references/seasons)
    [JsonPropertyName("value")] public string Value { get; set; } = string.Empty;
    [JsonPropertyName("year")]  public int    Year  { get; set; }
}

public class NameBlock
{
    [JsonPropertyName("main")]        public string Main        { get; set; } = string.Empty;
    [JsonPropertyName("english")]     public string English     { get; set; } = string.Empty;
    [JsonPropertyName("alternative")] public string Alternative { get; set; } = string.Empty;
}

public class PosterBlock
{
    [JsonPropertyName("src")]        public string Src       { get; set; } = string.Empty;
    [JsonPropertyName("preview")]    public string Preview   { get; set; } = string.Empty;
    [JsonPropertyName("thumbnail")]  public string Thumbnail { get; set; } = string.Empty;
}

public class EpisodeItem
{
    [JsonPropertyName("ordinal")]
    [JsonConverter(typeof(IntNullableConverter))]
    public int? Ordinal { get; set; }

    [JsonPropertyName("hls_1080")] public string? Hls1080 { get; set; }
    [JsonPropertyName("hls_720")]  public string? Hls720  { get; set; }
    [JsonPropertyName("hls_480")]  public string? Hls480  { get; set; }

    [JsonPropertyName("duration")] public int Duration { get; set; }

    [JsonPropertyName("opening")] public OpeningBlock? Opening { get; set; }
    [JsonPropertyName("ending")]  public OpeningBlock? Ending  { get; set; }

    [JsonPropertyName("preview")] public PreviewBlock? Preview { get; set; }
}

public class OpeningBlock
{
    [JsonPropertyName("start")] public int? Start { get; set; }
    [JsonPropertyName("stop")]  public int? Stop  { get; set; }
}

public class PreviewBlock
{
    [JsonPropertyName("src")] public string Src { get; set; } = string.Empty;
}

/* ─────────────────────────────────────────────────────────────
   Франшизы (минимум, который нам нужен из /anime/franchises/release/{id})
   ───────────────────────────────────────────────────────────── */

public class FranchiseInfo
{
    [JsonPropertyName("id")]   public string Id   { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    [JsonPropertyName("franchise_releases")]
    public List<FranchiseReleaseLink> FranchiseReleases { get; set; } = new();
}

public class FranchiseReleaseLink
{
    [JsonPropertyName("id")]            public string Id { get; set; } = string.Empty;
    [JsonPropertyName("sort_order")]    public int? SortOrder { get; set; }
    [JsonPropertyName("release_id")]    public int ReleaseId { get; set; }
    [JsonPropertyName("franchise_id")]  public string FranchiseId { get; set; } = string.Empty;

    [JsonPropertyName("release")]       public FranchiseReleaseRef? Release { get; set; }
}

public class FranchiseReleaseRef
{
    [JsonPropertyName("id")]    public int Id { get; set; }

    [JsonPropertyName("type")]  public ReleaseType? Type { get; set; }

    // В ответе бывает просто year на верхнем уровне
    [JsonPropertyName("year")]  public int? Year { get; set; }

    [JsonPropertyName("name")]  public NameBlock? Name { get; set; }
}

public class ReleaseType
{
    [JsonPropertyName("value")]       public string Value { get; set; } = string.Empty; // e.g. "TV", "Movie"
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
}
