using System.Text.Json;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class OtpPayloadBuilderTests
{
    [Fact]
    public void TryCreateAcceptPayload_UsesNumericCode()
    {
        var ok = OtpPayloadBuilder.TryCreateAcceptPayload("058701", out var json);

        Assert.True(ok);

        using var doc = JsonDocument.Parse(json);
        var codeEl = doc.RootElement.GetProperty("code");

        Assert.Equal(JsonValueKind.Number, codeEl.ValueKind);
        Assert.Equal(58701, codeEl.GetInt32());
    }

    [Fact]
    public void TryCreateLoginPayload_IncludesDeviceId()
    {
        var ok = OtpPayloadBuilder.TryCreateLoginPayload("54233", "device-1", out var json);

        Assert.True(ok);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(54233, doc.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("device-1", doc.RootElement.GetProperty("device_id").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12a34")]
    [InlineData("-7")]
    public void TryCreateAcceptPayload_RejectsInvalidCode(string? code)
    {
        var ok = OtpPayloadBuilder.TryCreateAcceptPayload(code, out var json);

        Assert.False(ok);
        Assert.Equal(string.Empty, json);
    }
}
