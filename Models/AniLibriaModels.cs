using System.Text.Json.Serialization;
using AniLibriaStrmPlugin.Converters;

namespace AniLibriaStrmPlugin.Models;

public class FavoritesResponse
{
    public List<TitleResponse> List { get; set; }
}

public class TitleResponse
{
    public int Id { get; set; }
    public string Code { get; set; }

    //     "names": {...}
    public NameBlock Names { get; set; }

    // "posters": {...}
    public PosterBlock Posters { get; set; }

    // "player": {...}
    public PlayerBlock Player { get; set; }

    // "description": "..."
    public string Description { get; set; }

    //   :
    // "franchises":[{ "franchise":{...}, "releases":[{id=.., ordinal=..},...] }, ... ]
    public List<FranchiseBlock> Franchises { get; set; }
}

// ===  franchsies ===
public class FranchiseBlock
{
    // "franchise":{"id":"2cdaa1..","name":" ..."}
    public FranchiseItem Franchise { get; set; }

    // "releases":[ {id=..., code=..., ordinal=..., names=...}, ... ]
    public List<ReleaseItem> Releases { get; set; }
}

public class FranchiseItem
{
    public string Id { get; set; }
    public string Name { get; set; }
}

// "id": 8875, "code": "...", "ordinal":1, "names": {...}
public class ReleaseItem
{
    // : "id":9555, "code":"kusuriya-no-hitorigoto", "ordinal":1, "names":{...}
    public int Id { get; set; }
    public string Code { get; set; }
    public int Ordinal { get; set; }
    public NameBlock Names { get; set; }
}

// ===   ===

public class NameBlock
{
    public string Ru { get; set; }
    public string En { get; set; }
    public string Alternative { get; set; }
}

public class PosterBlock
{
    public PosterUrl Small { get; set; }
    public PosterUrl Medium { get; set; }
    public PosterUrl Original { get; set; }
}

public class PosterUrl
{
    public string Url { get; set; }
}

public class PlayerBlock
{
    public string Host { get; set; }
    public bool Is_rutube { get; set; }
    public EpisodesBlock Episodes { get; set; }

    // "list": { "1": {episode=1, ...}, "2": {...} }
    public Dictionary<string, EpisodeItem> List { get; set; }
}

public class EpisodesBlock
{
    [JsonConverter(typeof(IntNullableConverter))]
    public int? First { get; set; }

    [JsonConverter(typeof(IntNullableConverter))]
    public int? Last { get; set; }

    public string String { get; set; }
}

public class EpisodeItem
{
    public double Episode { get; set; }
    public string Name { get; set; }
    public string Uuid { get; set; }
    public int Created_timestamp { get; set; }

    public string Preview { get; set; }

    public SkipsBlock Skips { get; set; }
    public HlsBlock Hls { get; set; }
}

public class SkipsBlock
{
    public List<int> Opening { get; set; }
    public List<int> Ending { get; set; }
}

public class HlsBlock
{
    public string Fhd { get; set; }
    public string Hd { get; set; }
    public string Sd { get; set; }
}