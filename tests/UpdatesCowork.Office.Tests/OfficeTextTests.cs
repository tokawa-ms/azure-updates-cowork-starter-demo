namespace UpdatesCowork.Office.Tests;

public sealed class OfficeTextTests
{
    [Theory]
    [InlineData("日本語😀", "日本語😀")]
    [InlineData("a\u0000b\u0001c\u001Fd\u007Fe\u0085f", "abcdef")]
    [InlineData("改行\r\n次\r前\n後", "改行\n次\n前\n後")]
    [InlineData("タブ\tは残す", "タブ\tは残す")]
    [InlineData("非文字\uFFFE\uFFFF", "非文字")]
    [InlineData(null, "")]
    public void Clean_RemovesXmlInvalidAndControlChars(string? input, string expected) =>
        Assert.Equal(expected, OfficeText.Clean(input));

    [Fact]
    public void Clean_RemovesLoneSurrogates_AndKeepsPairs()
    {
        // 属性の引数は UTF-8 で保存され孤立サロゲートが置き換わるため、メソッドの中で組み立てる
        var input = "孤立\uD800した\uDC00サロゲート\uD83D\uDE00";

        Assert.Equal("孤立したサロゲート😀", OfficeText.Clean(input));
    }

    [Fact]
    public void Truncate_DoesNotSplitSurrogatePairsOrEmojiSequences()
    {
        var input = string.Concat(Enumerable.Repeat("👍🏽", 10));

        var result = OfficeText.Truncate(input, 5, out var truncated);

        Assert.True(truncated);
        Assert.Equal(string.Concat(Enumerable.Repeat("👍🏽", 4)) + "…", result);
    }

    [Fact]
    public void Truncate_ShortText_ReturnsAsIs()
    {
        var result = OfficeText.Truncate("短い", 120, out var truncated);

        Assert.False(truncated);
        Assert.Equal("短い", result);
    }
}
