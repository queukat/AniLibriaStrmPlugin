using AniLibertyStrmPlugin.Models;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class AniLibertyStrmGeneratorHelperTests
{
    [Theory]
    [InlineData("Demo Season 2", "Demo")]
    [InlineData("Demo 2nd Season", "Demo")]
    [InlineData("Demo Part 3", "Demo")]
    [InlineData("Demo III", "Demo")]
    [InlineData("Demo Ω", "Demo")]
    [InlineData("", "")]
    public void CleanShowName_RemovesKnownSuffixes(string input, string expected)
    {
        Assert.Equal(expected, AniLibertyStrmGenerator.CleanShowName(input));
    }

    [Fact]
    public void NormalizeTitleForFs_ReplacesUnsafeCharactersAndSeparators()
    {
        var normalized = AniLibertyStrmGenerator.NormalizeTitleForFs("Code: Demo × Ω — Final");

        Assert.Equal("Code Demo x Final", normalized);
    }

    [Fact]
    public void DetectSeasonNumber_ReadsEnglishMainAndDefaultsToOne()
    {
        Assert.Equal(3, AniLibertyStrmGenerator.DetectSeasonNumber(
            Release("Demo Season 3", main: "ignored")));
        Assert.Equal(2, AniLibertyStrmGenerator.DetectSeasonNumber(
            Release("", main: "Demo 2")));
        Assert.Equal(1, AniLibertyStrmGenerator.DetectSeasonNumber(
            Release("Demo", main: "Demo")));
    }

    [Fact]
    public void GroupKeyAndFallbackSeasonMap_GroupRelatedTitlesInChronologicalOrder()
    {
        var first = Release("Demo Season 2", id: 20, year: 2025, season: "spring");
        var second = Release("Demo", id: 10, year: 2024, season: "autumn");
        var third = Release("Other", id: 30, year: 2024);

        var map = AniLibertyStrmGenerator.BuildFallbackSeasonMap([first, second, third]);

        Assert.Equal("demo", AniLibertyStrmGenerator.GroupKey(first));
        Assert.Equal(1, map[second.Id]);
        Assert.Equal(2, map[first.Id]);
        Assert.Equal(1, map[third.Id]);
        Assert.Equal(2, AniLibertyStrmGenerator.QuarterIndex("spring"));
        Assert.Equal(99, AniLibertyStrmGenerator.QuarterIndex("unknown"));
    }

    [Fact]
    public void LooksLikeMovie_UsesTypeOrSingleEpisodeMovieTitle()
    {
        Assert.True(AniLibertyStrmGenerator.LooksLikeMovie(
            Release("Plain", type: "MOVIE", episodeCount: 3),
            "Plain"));
        Assert.True(AniLibertyStrmGenerator.LooksLikeMovie(
            Release("Demo The Movie", episodeCount: 1),
            "Demo The Movie"));
        Assert.False(AniLibertyStrmGenerator.LooksLikeMovie(
            Release("Demo Movie", episodeCount: 3, episodesTotal: 3),
            "Demo Movie"));
    }

    [Theory]
    [InlineData("SPECIAL", true)]
    [InlineData("OVA", true)]
    [InlineData("OAD", true)]
    [InlineData("TV", false)]
    public void IsSpecialsType_MatchesSpecialReleaseTypes(string type, bool expected)
    {
        Assert.Equal(expected, AniLibertyStrmGenerator.IsSpecialsType(Release("Demo", type: type)));
    }

    [Fact]
    public void ComputeSeasonFromFranchises_FiltersNonSeasonEntriesAndSortsCandidates()
    {
        var list = new List<FranchiseInfo>
        {
            new()
            {
                Id = "short",
                FranchiseReleases =
                [
                    FranchiseLink(1, "Other", "TV", 2025, 1)
                ]
            },
            new()
            {
                Id = "main",
                FranchiseReleases =
                [
                    FranchiseLink(1, "Demo", "TV", 2024, 1),
                    FranchiseLink(99, "Demo Recap", "TV", 2024, 2),
                    FranchiseLink(2, "Demo Movie", "MOVIE", 2024, 3),
                    FranchiseLink(3, "Demo Season 2", "TV", 2025, 4)
                ]
            }
        };

        Assert.Equal(2, AniLibertyStrmGenerator.ComputeSeasonFromFranchises(list, 3));
        Assert.Equal(0, AniLibertyStrmGenerator.ComputeSeasonFromFranchises(null, 3));
        Assert.Equal(0, AniLibertyStrmGenerator.ComputeSeasonFromFranchises([], 3));
        Assert.Equal(0, AniLibertyStrmGenerator.ComputeSeasonFromFranchises(
            [new FranchiseInfo { FranchiseReleases = [FranchiseLink(99, "Recap", "TV", 2024, 1)] }],
            3));
        Assert.False(AniLibertyStrmGenerator.IsRegularTvSeason(FranchiseLink(4, "Demo Special", "TV", 2024, 1)));
        Assert.False(AniLibertyStrmGenerator.IsRegularTvSeason(FranchiseLink(5, "Demo", "MOVIE", 2024, 1)));
    }

    [Fact]
    public void ChooseHls_UsesResolutionFallbacks()
    {
        var ep = new EpisodeItem
        {
            Hls1080 = "1080",
            Hls720 = "720",
            Hls480 = "480"
        };

        Assert.Equal("1080", AniLibertyStrmGenerator.ChooseHls(ep, "1080"));
        Assert.Equal("720", AniLibertyStrmGenerator.ChooseHls(ep, "720"));
        Assert.Equal("480", AniLibertyStrmGenerator.ChooseHls(ep, "480"));

        ep.Hls720 = null;
        Assert.Equal("1080", AniLibertyStrmGenerator.ChooseHls(ep, "720"));

        ep.Hls1080 = null;
        Assert.Equal("480", AniLibertyStrmGenerator.ChooseHls(ep, "1080"));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("https://cdn.test/a.m3u8", "https://cdn.test/a.m3u8")]
    [InlineData("//cdn.test/a.m3u8", "https://cdn.test/a.m3u8")]
    [InlineData("/public/hls/a.m3u8", "https://api.anilibria.app/public/hls/a.m3u8")]
    [InlineData("public/hls/a.m3u8", "https://api.anilibria.app/public/hls/a.m3u8")]
    public void MakeFullUrl_NormalizesApiRelativeUrls(string input, string expected)
    {
        Assert.Equal(expected, AniLibertyStrmGenerator.MakeFullUrl(input));
    }

    [Fact]
    public void ResolvePlaybackProxyBaseUrl_UsesConfiguredThenPublishedThenNativeLocalUrl()
    {
        Assert.Equal(
            "https://manual.example/jellyfin",
            AniLibertyStrmGenerator.ResolvePlaybackProxyBaseUrl(
                configuredBaseUrl: "https://manual.example/jellyfin/",
                publishedBaseUrl: "https://published.example",
                isContainer: true,
                localBaseUrl: "http://172.17.0.2:8096"));

        Assert.Equal(
            "https://published.example",
            AniLibertyStrmGenerator.ResolvePlaybackProxyBaseUrl(
                configuredBaseUrl: "not-a-url",
                publishedBaseUrl: "https://published.example/",
                isContainer: true,
                localBaseUrl: "http://172.17.0.2:8096"));

        Assert.Equal(
            "http://192.168.1.10:8096",
            AniLibertyStrmGenerator.ResolvePlaybackProxyBaseUrl(
                configuredBaseUrl: null,
                publishedBaseUrl: null,
                isContainer: false,
                localBaseUrl: "http://192.168.1.10:8096/"));
    }

    [Fact]
    public void ResolvePlaybackProxyBaseUrl_DoesNotUseContainerLocalAddressWithoutPublishedUrl()
    {
        var resolved = AniLibertyStrmGenerator.ResolvePlaybackProxyBaseUrl(
            configuredBaseUrl: null,
            publishedBaseUrl: null,
            isContainer: true,
            localBaseUrl: "http://172.17.0.2:8096");

        Assert.Empty(resolved);
    }

    [Theory]
    [InlineData("http://192.168.1.10:8096/", true, "http://192.168.1.10:8096")]
    [InlineData("https://jellyfin.example/base/", true, "https://jellyfin.example/base")]
    [InlineData("ftp://jellyfin.example", false, "")]
    [InlineData("https://jellyfin.example?bad=1", false, "")]
    [InlineData("not-a-url", false, "")]
    public void TryNormalizePlaybackProxyBaseUrl_AcceptsOnlyHttpBaseUrls(
        string candidate,
        bool expectedSuccess,
        string expectedUrl)
    {
        var success = AniLibertyStrmGenerator.TryNormalizePlaybackProxyBaseUrl(candidate, out var normalized);

        Assert.Equal(expectedSuccess, success);
        Assert.Equal(expectedUrl, normalized);
    }

    [Theory]
    [InlineData("true", false, false, null, true)]
    [InlineData("1", false, false, null, true)]
    [InlineData(null, true, false, null, true)]
    [InlineData(null, false, true, null, true)]
    [InlineData(null, false, false, "10.0.0.1", true)]
    [InlineData("false", false, false, null, false)]
    public void IsContainerEnvironment_RecognizesSupportedContainerSignals(
        string? dotnetFlag,
        bool dockerEnvironmentFile,
        bool containerEnvironmentFile,
        string? kubernetesServiceHost,
        bool expected)
    {
        Assert.Equal(
            expected,
            AniLibertyStrmGenerator.IsContainerEnvironment(
                dotnetFlag,
                dockerEnvironmentFile,
                containerEnvironmentFile,
                kubernetesServiceHost));
    }

    [Fact]
    public void ImageHelpers_PickNormalizeAndSanitizeExtensions()
    {
        Assert.Equal(string.Empty, AniLibertyStrmGenerator.PickImageUrl(null));
        Assert.Equal("/src.jpg", AniLibertyStrmGenerator.PickImageUrl(new ImageBlock
        {
            Src = "/src.jpg",
            Preview = "/preview.webp",
            Thumbnail = "/thumb.webp",
            Optimized = new ImageBlock { Preview = "/opt-preview.webp" }
        }));
        Assert.Equal("/preview.webp", AniLibertyStrmGenerator.PickImageUrl(new ImageBlock
        {
            Preview = "/preview.webp",
            Optimized = new ImageBlock { Preview = "/opt-preview.webp" }
        }));

        Assert.Equal("https://cdn.test/poster.jpg?x=1",
            AniLibertyStrmGenerator.NormalizeImageUrlPreferJpg("https://cdn.test/poster.webp?x=1"));
        Assert.Equal("/poster.jpg", AniLibertyStrmGenerator.NormalizeImageUrlPreferJpg("/poster.webp"));
        Assert.Equal("/poster.png", AniLibertyStrmGenerator.NormalizeImageUrlPreferJpg("/poster.png"));

        Assert.Equal(".jpg", AniLibertyStrmGenerator.GetSafeImageExtensionFromUrl(""));
        Assert.Equal(".png", AniLibertyStrmGenerator.GetSafeImageExtensionFromUrl("https://cdn.test/a.PNG?x=1"));
        Assert.Equal(".jpeg", AniLibertyStrmGenerator.GetSafeImageExtensionFromUrl("relative.jpeg"));
        Assert.Equal(".jpg", AniLibertyStrmGenerator.GetSafeImageExtensionFromUrl("relative.webp"));
        Assert.Equal(".jpg", AniLibertyStrmGenerator.GetSafeImageExtensionFromUrl("no-extension"));
    }

    [Fact]
    public void XmlAndTextHelpers_EscapeNormalizeAndMarkGeneratedXml()
    {
        Assert.Equal("&lt;tag attr=&quot;1&quot;&gt;&amp;", AniLibertyStrmGenerator.MakeSafeXml("<tag attr=\"1\">&"));
        Assert.Equal("a\nb", AniLibertyStrmGenerator.NormalizeTextForCompare("a\r\nb\r\n"));
        Assert.True(AniLibertyStrmGenerator.TextMatches("a\r\nb\r\n", "a\nb"));

        var marked = AniLibertyStrmGenerator.AddGeneratedXmlMarker("<?xml version=\"1.0\"?><root />");
        Assert.Contains("generated-by AniLibertyStrmPlugin", marked);
        Assert.StartsWith("<?xml version=\"1.0\"?>", marked);
        Assert.Same(marked, AniLibertyStrmGenerator.AddGeneratedXmlMarker(marked));

        var noDeclaration = AniLibertyStrmGenerator.AddGeneratedXmlMarker("<root />");
        Assert.StartsWith("<!-- generated-by AniLibertyStrmPlugin -->", noDeclaration);
    }

    private static ReleaseResponse Release(
        string english,
        string? main = null,
        string type = "TV",
        int id = 1,
        int year = 2024,
        string? season = null,
        int episodeCount = 1,
        int? episodesTotal = null)
        => new()
        {
            Id = id,
            Alias = $"alias-{id}",
            Type = new ReleaseType { Value = type },
            Year = year,
            Season = season is null ? null : new SeasonBlock { Value = season },
            Name = new NameBlock
            {
                English = english,
                Main = main ?? english,
                Alternative = english + " alt"
            },
            EpisodesTotal = episodesTotal ?? episodeCount,
            Episodes = Enumerable.Range(1, episodeCount)
                .Select(i => new EpisodeItem { Id = Guid.NewGuid().ToString(), Ordinal = i })
                .ToList()
        };

    private static FranchiseReleaseLink FranchiseLink(int releaseId, string title, string type, int year, int sortOrder)
        => new()
        {
            ReleaseId = releaseId,
            SortOrder = sortOrder,
            Release = new FranchiseReleaseRef
            {
                Id = releaseId,
                Type = new ReleaseType { Value = type },
                Year = year,
                Name = new NameBlock
                {
                    English = title,
                    Main = title,
                    Alternative = title
                }
            }
        };
}
