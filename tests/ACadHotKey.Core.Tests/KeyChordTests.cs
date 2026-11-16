using ACadHotKey.Core;
using Xunit;

namespace ACadHotKey.Core.Tests;

public class KeyChordTests
{
    [Theory]
    [InlineData("1", 0x31, KeyModifiers.None)]
    [InlineData("q", 0x51, KeyModifiers.None)]
    [InlineData("Num1", 0x61, KeyModifiers.None)]
    [InlineData("NumPad9", 0x69, KeyModifiers.None)]
    [InlineData("F5", 0x74, KeyModifiers.None)]
    [InlineData("F24", 0x87, KeyModifiers.None)]
    [InlineData("Ctrl+Shift+D", 0x44, KeyModifiers.Ctrl | KeyModifiers.Shift)]
    [InlineData("alt + 2", 0x32, KeyModifiers.Alt)]
    [InlineData("`", 0xC0, KeyModifiers.None)]
    [InlineData("Ctrl+Plus", 0xBB, KeyModifiers.Ctrl)]
    [InlineData("0x31", 0x31, KeyModifiers.None)]
    [InlineData("Space", 0x20, KeyModifiers.None)]
    public void Parses_key_names(string text, int vk, KeyModifiers modifiers)
    {
        var chord = KeyChord.Parse(text);
        Assert.Equal(vk, chord.VirtualKey);
        Assert.Equal(modifiers, chord.Modifiers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+")]
    [InlineData("1+2")]
    [InlineData("F25")]
    [InlineData("Ж")]
    [InlineData("Hyper+1")]
    public void Rejects_invalid_keys(string text)
    {
        Assert.False(KeyChord.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Top_row_digit_and_numpad_digit_are_different_keys()
    {
        Assert.NotEqual(KeyChord.Parse("1"), KeyChord.Parse("Num1"));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("Ctrl+Alt+Shift+F12")]
    [InlineData("Num0")]
    [InlineData("`")]
    public void ToString_round_trips(string text)
    {
        var chord = KeyChord.Parse(text);
        Assert.Equal(chord, KeyChord.Parse(chord.ToString()));
    }
}
