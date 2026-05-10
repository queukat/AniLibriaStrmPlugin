using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AniLibertyStrmPlugin.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class OutputRootPreflightTests
{
    [Fact]
    public async Task EnsureWritableAsync_CreatesRootAndRemovesProbeFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "aniliberty-preflight-" + Guid.NewGuid().ToString("N"));
        try
        {
            await OutputRootPreflight.EnsureWritableAsync(
                root,
                "All Titles STRM Path",
                NullLogger.Instance,
                CancellationToken.None);

            Assert.True(Directory.Exists(root));
            var stateDir = Path.Combine(root, ManagedLibraryManifest.StateDirectoryName);
            Assert.True(Directory.Exists(stateDir));
            Assert.Empty(Directory.GetFiles(stateDir, ".write-test-*.tmp"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureWritableAsync_ReportsFilePathAsNotWritable()
    {
        var root = Path.Combine(Path.GetTempPath(), "aniliberty-preflight-" + Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(root, "not-a-directory");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(filePath, "occupied");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                OutputRootPreflight.EnsureWritableAsync(
                    filePath,
                    "All Titles STRM Path",
                    NullLogger.Instance,
                    CancellationToken.None));

            Assert.Contains("All Titles STRM Path is not writable", ex.Message, StringComparison.Ordinal);
            Assert.Contains("PUID/PGID permissions", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
