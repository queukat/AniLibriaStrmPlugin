using System.Text.Json;
using AniLibertyStrmPlugin.Converters;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class JsonConverterTests
{
    private static readonly JsonSerializerOptions IntOptions = new()
    {
        Converters = { new IntNullableConverter() }
    };

    private static readonly JsonSerializerOptions DoubleOptions = new()
    {
        Converters = { new DoubleNullableConverter() }
    };

    [Theory]
    [InlineData("42", 42)]
    [InlineData("42.9", 42)]
    [InlineData("\"17\"", 17)]
    [InlineData("\"17.9\"", 17)]
    [InlineData("\"bad\"", null)]
    [InlineData("\"\"", null)]
    [InlineData("null", null)]
    [InlineData("true", null)]
    public void IntNullableConverter_ReadsLenientValues(string json, int? expected)
    {
        var actual = JsonSerializer.Deserialize<int?>(json, IntOptions);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IntNullableConverter_WritesNumberOrNull()
    {
        Assert.Equal("12", JsonSerializer.Serialize<int?>(12, IntOptions));
        Assert.Equal("null", JsonSerializer.Serialize<int?>(null, IntOptions));
    }

    [Theory]
    [InlineData("42", 42.0)]
    [InlineData("42.5", 42.5)]
    [InlineData("\"17.25\"", 17.25)]
    [InlineData("\"\"", null)]
    [InlineData("\"bad\"", null)]
    [InlineData("null", null)]
    [InlineData("false", null)]
    public void DoubleNullableConverter_ReadsLenientValues(string json, double? expected)
    {
        var actual = JsonSerializer.Deserialize<double?>(json, DoubleOptions);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DoubleNullableConverter_WritesNumberOrNull()
    {
        Assert.Equal("12.5", JsonSerializer.Serialize<double?>(12.5, DoubleOptions));
        Assert.Equal("null", JsonSerializer.Serialize<double?>(null, DoubleOptions));
    }
}
