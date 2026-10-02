using ChatTerror.Plugin.Logic;

namespace ChatTerror.Plugin.Tests;

public class TellTextTests
{
    private static string Chars(params int[] codes) => string.Concat(codes.Select(char.ConvertFromUtf32));

    [Fact]
    public void Clean_StripsMacroBytesControlsAndLineBreaks()
    {
        Assert.Equal("hiforged", TellText.Clean("hi" + Chars(0x02, 0x10, 0x01, 0x03) + "forged", 500));
        Assert.Equal("ab", TellText.Clean("a" + Chars(0x0A, 0x0D, 0x7F, 0x85, 0x2028, 0x2029) + "b", 500));
        Assert.Equal("ok", TellText.Clean("  ok" + Chars(0x09) + " ", 500));
    }

    [Fact]
    public void Clean_KeepsOrdinaryTextAndIcons()
    {
        var text = "h" + Chars(0xE9) + "llo " + Chars(0xE0BB, 0x20, 0x1F600);
        Assert.Equal(text, TellText.Clean(text, 500));
    }

    [Fact]
    public void Clean_CapsUtf8BytesWithoutSplittingCharacters()
    {
        Assert.Equal("abc", TellText.Clean("abcdef", 3));
        Assert.Equal(Chars(0xE9), TellText.Clean(Chars(0xE9, 0xE9), 3));
        Assert.Equal("a", TellText.Clean("a" + Chars(0x1F600), 4));
    }
}
