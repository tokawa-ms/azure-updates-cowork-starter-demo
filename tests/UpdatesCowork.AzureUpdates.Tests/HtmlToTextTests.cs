namespace UpdatesCowork.AzureUpdates.Tests;

public sealed class HtmlToTextTests
{
    [Fact]
    public void Convert_ParagraphsBecomeBlankLineSeparated()
    {
        var text = HtmlToText.Convert("<p>First   paragraph.</p><p>Second\n paragraph.</p>");

        Assert.Equal("First paragraph.\n\nSecond paragraph.", text);
    }

    [Fact]
    public void Convert_DropsScriptAndStyle()
    {
        var text = HtmlToText.Convert("<p>Visible</p><script>alert('x')</script><style>p{color:red}</style>");

        Assert.Equal("Visible", text);
    }

    [Fact]
    public void Convert_ListItemsBecomeBullets()
    {
        var text = HtmlToText.Convert("<p>Intro</p><ul><li>One</li><li><p>Two</p></li></ul><p>After</p>");

        Assert.Equal("Intro\n\n- One\n- Two\n\nAfter", text);
    }

    [Fact]
    public void Convert_NestedListsAreIndented()
    {
        var text = HtmlToText.Convert("<ul><li>Parent<ul><li>Child</li></ul></li></ul>");

        Assert.Equal("- Parent\n  - Child", text);
    }

    [Fact]
    public void Convert_LinksKeepUrl()
    {
        var text = HtmlToText.Convert("<p>See <a href=\"https://learn.microsoft.com/azure/\">the docs</a> now.</p>");

        Assert.Equal("See the docs(https://learn.microsoft.com/azure/) now.", text);
    }

    [Fact]
    public void Convert_IgnoresNonWebLinks()
    {
        var text = HtmlToText.Convert("<p><a href=\"javascript:alert(1)\">click</a> <a href=\"/relative\">rel</a></p>");

        Assert.Equal("click rel", text);
    }

    [Fact]
    public void Convert_BrBecomesNewLine()
    {
        Assert.Equal("Line one\nLine two", HtmlToText.Convert("<p>Line one<br>Line two</p>"));
    }

    [Fact]
    public void Convert_DecodesEntities()
    {
        Assert.Equal("A & B <c>", HtmlToText.Convert("<p>A &amp; B &lt;c&gt;</p>"));
    }

    [Fact]
    public void Convert_TableCellsAreSeparated()
    {
        var text = HtmlToText.Convert("<table><tr><th>Name</th><th>Status</th></tr><tr><td>A</td><td>GA</td></tr></table>");

        Assert.Equal("Name | Status\nA | GA", text);
    }

    [Fact]
    public void Convert_TruncatesLongText()
    {
        var html = "<p>" + new string('a', 20_000) + "</p>";

        var text = HtmlToText.Convert(html);

        Assert.Equal(HtmlToText.DefaultMaxLength, text.Length);
        Assert.EndsWith(HtmlToText.TruncatedSuffix, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_DoesNotSplitSurrogatePairs()
    {
        var html = "<p>" + string.Concat(Enumerable.Repeat("\U0001F600", 100)) + "</p>";

        var text = HtmlToText.Convert(html, maxLength: 50);

        Assert.True(text.Length <= 50);
        var body = text[..^HtmlToText.TruncatedSuffix.Length];
        Assert.False(char.IsHighSurrogate(body[^1]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Convert_EmptyInput_ReturnsEmpty(string? html)
    {
        Assert.Equal(string.Empty, HtmlToText.Convert(html));
    }

    [Fact]
    public void Convert_PlainTextIsKept()
    {
        Assert.Equal("Just text", HtmlToText.Convert("Just   text"));
    }
}
