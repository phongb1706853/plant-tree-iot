using PlantTreeIoTServer.Services;
using Xunit;

namespace PlantTreeIoTServer.Tests;

// Màn TFT chỉ có font ASCII, firmware lưu ≤ 95 ký tự (backend-api.md mục 5.9): server bỏ dấu, bỏ emoji, cắt 90.
public class DeviceTextTests
{
    [Theory]
    [InlineData("Cảm ơn bạn đã tưới cho tôi!", "Cam on ban da tuoi cho toi!")]
    [InlineData("ĐẤT KHÔ QUÁ", "DAT KHO QUA")]
    [InlineData("Ướt rồi, nghỉ thôi", "Uot roi, nghi thoi")]
    [InlineData("Plain ASCII stays", "Plain ASCII stays")]
    public void Removes_vietnamese_marks(string input, string expected)
    {
        Assert.Equal(expected, DeviceText.ToScreenText(input));
    }

    [Fact]
    public void Drops_emoji_and_collapses_spaces()
    {
        Assert.Equal("Cay vui qua", DeviceText.ToScreenText("Cây 🌱  vui 😀 quá 🎉"));
    }

    [Fact]
    public void Newlines_and_tabs_become_single_spaces()
    {
        Assert.Equal("Line 1 Line 2", DeviceText.ToScreenText("  Line 1\n\tLine 2  "));
    }

    [Fact]
    public void Cuts_to_90_characters()
    {
        var result = DeviceText.ToScreenText(new string('a', 120));
        Assert.Equal(DeviceText.MaxScreenLength, result.Length);
    }

    [Fact]
    public void Null_and_empty_give_empty()
    {
        Assert.Equal("", DeviceText.ToScreenText(null));
        Assert.Equal("", DeviceText.ToScreenText(""));
    }

    [Fact]
    public void Only_emoji_gives_empty()
    {
        Assert.Equal("", DeviceText.ToScreenText("🌱🌱"));
    }
}
