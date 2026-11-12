using System.Text.RegularExpressions;
using PlantTreeIoTServer.Services;
using Xunit;

namespace PlantTreeIoTServer.Tests;

// id chống giao lặp QoS 1: firmware so 39 ký tự đầu và chỉ nhớ id gần nhất (backend-api.md mục 5.4).
public class CommandIdTests
{
    [Fact]
    public void Has_the_recommended_shape()
    {
        var now = new DateTimeOffset(2026, 11, 8, 8, 20, 0, TimeSpan.Zero);
        var id = CommandId.Next(now);

        Assert.Matches(new Regex($"^c-{now.ToUnixTimeMilliseconds()}-\\d+$"), id);
    }

    [Fact]
    public void Fits_the_39_character_limit()
    {
        Assert.True(CommandId.Next(DateTimeOffset.MaxValue).Length <= CommandId.MaxLength);
    }

    [Fact]
    public void Two_ids_in_the_same_millisecond_differ()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.NotEqual(CommandId.Next(now), CommandId.Next(now));
    }
}
