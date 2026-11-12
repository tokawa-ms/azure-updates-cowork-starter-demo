using System.Text.RegularExpressions;
using UpdatesCowork.Shared.Contracts;

namespace UpdatesCowork.Office.Tests;

public sealed partial class OfficeSpecValidatorTests
{
    public static TheoryData<string, string> InvalidWordSpecs => new()
    {
        { "no-sections", "sections に節を 1 件以上" },
        { "too-many-sections", "sections は 12 件まで" },
        { "too-many-paragraphs", "1 番目の節の paragraphs (段落) は 20 件まで" },
        { "too-many-bullets", "1 番目の節の bullets (箇条書き) は 20 項目まで" },
        { "too-many-columns", "1 番目の節の表は 10 列までです(今は 11 列)。列を減らしてください。" },
        { "too-many-rows", "1 番目の節の表は 30 行まで" },
        { "row-width-mismatch", "1 番目の節の表の 2 行目は値が 1 個ありますが、列は 2 列です。" },
        { "no-columns", "1 番目の節の表に columns" },
        { "blank-heading", "1 番目の節の heading (見出し) を指定" },
        { "no-sources", "report.sources に出典を 1 件以上" },
        { "disallowed-source", "1 番目の出典の url は https://azure.microsoft.com/ か https://learn.microsoft.com/ で始まるものだけ" },
        { "blank-title", "title (タイトル) を指定" },
        { "blank-file-name", "fileName (ファイル名) を指定" },
        { "long-file-name", "fileName (ファイル名) は 60 文字まで" },
        { "unusable-file-name", "fileName (ファイル名) には半角英数字" },
    };

    public static TheoryData<string, string> InvalidDeckSpecs => new()
    {
        { "no-slides", "slides にスライドを 1 枚以上" },
        { "too-many-slides", "slides は 12 枚まで" },
        { "too-many-bullets", "1 枚目のスライドの bullets (箇条書き) は 6 項目まで" },
        { "long-notes", "1 枚目のスライドの notes (ノート) は 1500 文字まで" },
        { "blank-slide-title", "1 枚目のスライドの title (タイトル) を指定" },
        { "no-sources", "deck.sources に出典を 1 件以上" },
        { "disallowed-source", "1 番目の出典の url は" },
        { "blank-file-name", "fileName (ファイル名) を指定" },
    };

    [Fact]
    public void Validate_ValidSpecs_ReturnsNoErrors()
    {
        Assert.Empty(OfficeSpecValidator.Validate(TestSpecs.Word()));
        Assert.Empty(OfficeSpecValidator.Validate(TestSpecs.Deck()));
    }

    [Fact]
    public void Validate_SpecsAtUpperLimits_ReturnsNoErrors()
    {
        var section = new ReportSection(
            "見出し",
            Paragraphs: Repeat("段落", OfficeLimits.MaxParagraphsPerSection),
            Bullets: Repeat("項目", OfficeLimits.MaxBulletsPerSection),
            Table: new ReportTable(
                Repeat("列", OfficeLimits.MaxTableColumns),
                Enumerable.Range(0, OfficeLimits.MaxTableRows).Select(_ => Repeat("値", OfficeLimits.MaxTableColumns)).ToList()));
        var word = TestSpecs.Word() with
        {
            FileName = new string('a', OfficeLimits.MaxFileNameLength) + ".docx",
            Sections = Enumerable.Repeat(section, OfficeLimits.MaxSections).ToList(),
        };
        var slide = new SlideSpec("タイトル", Repeat("項目", OfficeLimits.MaxBulletsPerSlide), new string('注', OfficeLimits.MaxNotesLength));
        var deck = TestSpecs.Deck() with { Slides = Enumerable.Repeat(slide, OfficeLimits.MaxSlides).ToList() };

        Assert.Empty(OfficeSpecValidator.Validate(word));
        Assert.Empty(OfficeSpecValidator.Validate(deck));
        Assert.Empty(TestSpecs.CreateFactory().CreateWordReport(word).ValidationErrors);
        Assert.Empty(TestSpecs.CreateFactory().CreateSlideDeck(deck).ValidationErrors);
    }

    [Theory]
    [MemberData(nameof(InvalidWordSpecs))]
    public void Validate_InvalidWordSpec_ReturnsJapaneseMessage(string caseName, string expected)
    {
        var errors = OfficeSpecValidator.Validate(CreateInvalidWordSpec(caseName));

        var error = Assert.Single(errors);
        Assert.Contains(expected, error, StringComparison.Ordinal);
        Assert.Matches(JapanesePattern(), error);
    }

    [Theory]
    [MemberData(nameof(InvalidDeckSpecs))]
    public void Validate_InvalidDeckSpec_ReturnsJapaneseMessage(string caseName, string expected)
    {
        var errors = OfficeSpecValidator.Validate(CreateInvalidDeckSpec(caseName));

        var error = Assert.Single(errors);
        Assert.Contains(expected, error, StringComparison.Ordinal);
        Assert.Matches(JapanesePattern(), error);
    }

    [Theory]
    [InlineData("https://azure.microsoft.com/updates?id=559242", true)]
    [InlineData("https://learn.microsoft.com/ja-jp/azure/", true)]
    [InlineData("  https://AZURE.microsoft.com/ja-jp/updates  ", true)]
    [InlineData("http://azure.microsoft.com/updates", false)]
    [InlineData("https://azure.microsoft.com.example.com/", false)]
    [InlineData("https://user@azure.microsoft.com/updates", false)]
    [InlineData("https://azure.microsoft.com:8443/updates", false)]
    [InlineData("https://example.com/?u=https://azure.microsoft.com/", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAllowedSourceUrl_AllowsOnlyListedPrefixes(string? url, bool expected) =>
        Assert.Equal(expected, OfficeSpecValidator.IsAllowedSourceUrl(url));

    private static WordReportSpec CreateInvalidWordSpec(string caseName)
    {
        var valid = TestSpecs.Word();
        var section = new ReportSection("見出し");
        return caseName switch
        {
            "no-sections" => valid with { Sections = [] },
            "too-many-sections" => valid with { Sections = Enumerable.Repeat(section, OfficeLimits.MaxSections + 1).ToList() },
            "too-many-paragraphs" => valid with { Sections = [section with { Paragraphs = Repeat("段落", OfficeLimits.MaxParagraphsPerSection + 1) }] },
            "too-many-bullets" => valid with { Sections = [section with { Bullets = Repeat("項目", OfficeLimits.MaxBulletsPerSection + 1) }] },
            "too-many-columns" => valid with { Sections = [section with { Table = new ReportTable(Repeat("列", 11), [Repeat("値", 11)]) }] },
            "too-many-rows" => valid with
            {
                Sections = [section with { Table = new ReportTable(["列"], Enumerable.Range(0, OfficeLimits.MaxTableRows + 1).Select(_ => Repeat("値", 1)).ToList()) }],
            },
            "row-width-mismatch" => valid with { Sections = [section with { Table = new ReportTable(["列1", "列2"], [["a", "b"], ["c"]]) }] },
            "no-columns" => valid with { Sections = [section with { Table = new ReportTable([], []) }] },
            "blank-heading" => valid with { Sections = [section with { Heading = " \u0001 " }] },
            "no-sources" => valid with { Sources = [] },
            "disallowed-source" => valid with { Sources = [new SourceRef("外部", "https://example.com/")] },
            "blank-title" => valid with { Title = "   " },
            "blank-file-name" => valid with { FileName = "" },
            "long-file-name" => valid with { FileName = new string('a', OfficeLimits.MaxFileNameLength + 1) },
            "unusable-file-name" => valid with { FileName = "要約レポート" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null),
        };
    }

    private static DeckSpec CreateInvalidDeckSpec(string caseName)
    {
        var valid = TestSpecs.Deck();
        var slide = new SlideSpec("タイトル");
        return caseName switch
        {
            "no-slides" => valid with { Slides = [] },
            "too-many-slides" => valid with { Slides = Enumerable.Repeat(slide, OfficeLimits.MaxSlides + 1).ToList() },
            "too-many-bullets" => valid with { Slides = [slide with { Bullets = Repeat("項目", OfficeLimits.MaxBulletsPerSlide + 1) }] },
            "long-notes" => valid with { Slides = [slide with { Notes = new string('注', OfficeLimits.MaxNotesLength + 1) }] },
            "blank-slide-title" => valid with { Slides = [slide with { Title = "" }] },
            "no-sources" => valid with { Sources = [] },
            "disallowed-source" => valid with { Sources = [new SourceRef("外部", "https://azure.microsoft.com.evil.example/")] },
            "blank-file-name" => valid with { FileName = " " },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null),
        };
    }

    private static List<string> Repeat(string value, int count) => Enumerable.Repeat(value, count).ToList();

    [GeneratedRegex(@"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]")]
    private static partial Regex JapanesePattern();
}
