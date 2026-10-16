using AniLibertyStrmPlugin.Utils;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin.Media;

public sealed class AniLibertyMediaSegmentProvider : IMediaSegmentProvider
{
    public const string ProviderName = "AniLiberty skip timings";

    private readonly ILibraryManager _libraryManager;
    private readonly AniLibertyMediaSegmentIndex _mediaSegmentIndex;
    private readonly ILogger<AniLibertyMediaSegmentProvider> _logger;

    public AniLibertyMediaSegmentProvider(
        ILibraryManager libraryManager,
        AniLibertyMediaSegmentIndex mediaSegmentIndex,
        ILogger<AniLibertyMediaSegmentProvider> logger)
    {
        _libraryManager = libraryManager;
        _mediaSegmentIndex = mediaSegmentIndex;
        _logger = logger;
    }

    public string Name => ProviderName;

    public ValueTask<bool> Supports(BaseItem item)
    {
        var supported =
            item != null &&
            !string.IsNullOrWhiteSpace(item.Path) &&
            item.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase) &&
            AniLibertyMediaSegmentState.IsConfiguredAniLibertyPath(item.Path);

        return ValueTask.FromResult(supported);
    }

    public async Task<IReadOnlyList<MediaSegmentDto>> GetMediaSegments(
        MediaSegmentGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(request.ItemId);
        if (item == null || string.IsNullOrWhiteSpace(item.Path))
            return Array.Empty<MediaSegmentDto>();

        try
        {
            var snapshot = await _mediaSegmentIndex
                .GetSnapshotForPathAsync(item.Path, cancellationToken)
                .ConfigureAwait(false);
            if (snapshot is null)
                throw new IOException("AniLiberty media segment state is unavailable; existing segments must be preserved.");

            if (!snapshot.TryGetEntry(item.Path, out var entry) || entry.Segments.Length == 0)
                return Array.Empty<MediaSegmentDto>();

            // Jellyfin replaces this provider's set; it does not append a delta.
            return BuildSegments(request.ItemId, entry.Segments);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Failed to resolve AniLiberty media segments. itemId={ItemId}", request.ItemId);
            throw;
        }
    }

    public Task CleanupExtractedData(Guid itemId, CancellationToken cancellationToken)
    {
        // AniLiberty timings are upstream metadata, not item-specific analysis data owned by Jellyfin.
        return Task.CompletedTask;
    }

    private static IReadOnlyList<MediaSegmentDto> BuildSegments(
        Guid itemId,
        IReadOnlyCollection<AniLibertyMediaSegment> segments)
    {
        if (segments.Count == 0)
            return Array.Empty<MediaSegmentDto>();

        var result = new List<MediaSegmentDto>(segments.Count);
        foreach (var segment in segments)
        {
            if (segment.StartTicks < 0 || segment.EndTicks <= segment.StartTicks)
                continue;

            if (!TryParseMediaSegmentType(segment.Type, out var type))
                continue;

            result.Add(new MediaSegmentDto
            {
                Id = Guid.NewGuid(),
                ItemId = itemId,
                Type = type,
                StartTicks = segment.StartTicks,
                EndTicks = segment.EndTicks
            });
        }

        return result;
    }

    private static bool TryParseMediaSegmentType(string? value, out MediaSegmentType type)
    {
        if (string.Equals(value, "Intro", StringComparison.OrdinalIgnoreCase))
        {
            type = MediaSegmentType.Intro;
            return true;
        }

        if (string.Equals(value, "Outro", StringComparison.OrdinalIgnoreCase))
        {
            type = MediaSegmentType.Outro;
            return true;
        }

        type = default;
        return false;
    }
}
