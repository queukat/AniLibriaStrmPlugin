using System.Text.Json.Serialization;
using AniLibriaStrmPlugin.Converters;

namespace AniLibriaStrmPlugin.Models;

public class FavoritesResponse
{
    public List<TitleResponse> List { get; set; } = new();
}

public class TitleResponse
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public NameBlock Names { get; set; } = null!;
    public PosterBlock Posters { get; set; } = null!;
    public PlayerBlock Player { get; set; } = null!;
    public string Description { get; set; } = null!;
    public List<FranchiseBlock> Franchises { get; set; } = new();
}

public class FranchiseBlock
{
    public FranchiseItem Franchise { get; set; } = null!;
    public List<ReleaseItem> Releases { get; set; } = new();
}

public class FranchiseItem
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
}

public class ReleaseItem
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public int Ordinal { get; set; }
    public NameBlock Names { get; set; } = null!;
}

public class NameBlock
{
    public string Ru { get; set; } = null!;
    public string En { get; set; } = null!;
    public string Alternative { get; set; } = null!;
}

public class PosterBlock
{
    public PosterUrl Small { get; set; } = null!;
    public PosterUrl Medium { get; set; } = null!;
    public PosterUrl Original { get; set; } = null!;
}

public class PosterUrl
{
    public string Url { get; set; } = null!;
}

public class PlayerBlock
{
    public string Host { get; set; } = null!;
    public bool Is_rutube { get; set; }
    public EpisodesBlock Episodes { get; set; } = null!;
    public Dictionary<string, EpisodeItem> List { get; set; } = new();
}

public class EpisodesBlock
{
    [JsonConverter(typeof(IntNullableConverter))]
    public int? First { get; set; }

    [JsonConverter(typeof(IntNullableConverter))]
    public int? Last { get; set; }

    public string String { get; set; } = null!;
}

public class EpisodeItem
{
    public double Episode { get; set; }
    public string Name { get; set; } = null!;
    public string Uuid { get; set; } = null!;
    public int Created_timestamp { get; set; }
    public string Preview { get; set; } = null!;
    public SkipsBlock Skips { get; set; } = null!;
    public HlsBlock Hls { get; set; } = null!;
}

public class SkipsBlock
{
    public List<int> Opening { get; set; } = new();
    public List<int> Ending { get; set; } = new();
}

public class HlsBlock
{
    public string Fhd { get; set; } = null!;
    public string Hd { get; set; } = null!;
    public string Sd { get; set; } = null!;
}
