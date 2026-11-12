using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UpdatesCowork.Shared.Contracts;

namespace UpdatesCowork.Office.Tests;

public sealed class WordReportBuilderTests
{
    [Fact]
    public void CreateWordReport_ReturnsDocxWithSectionCountAndNoValidationErrors()
    {
        var result = TestSpecs.CreateFactory().CreateWordReport(TestSpecs.Word());

        Assert.Empty(result.ValidationErrors);
        Assert.Equal(ArtifactKinds.DocxContentType, result.ContentType);
        Assert.Equal(ArtifactKinds.Docx, result.Extension);
        Assert.Equal(3, result.ItemCount);
        Assert.NotEmpty(result.Content);
    }

    [Fact]
    public void CreateWordReport_WritesTitleMetadataHeadingsAndBullets()
    {
        var result = TestSpecs.CreateFactory().CreateWordReport(TestSpecs.Word());

        using var document = Open(result);
        var body = document.MainDocumentPart!.Document!.Body!;
        var paragraphs = body.Elements<Paragraph>().ToList();

        Assert.Equal(WordStyleIds.Title, StyleOf(paragraphs[0]));
        Assert.Equal("Azure Container Apps express の一般提供", paragraphs[0].InnerText);
        Assert.Equal(WordStyleIds.Subtitle, StyleOf(paragraphs[1]));
        Assert.Null(StyleOf(paragraphs[2]));
        Assert.Equal("作成日: 2026-11-11 / 出典: Azure の更新情報 / この文書は AI が作成した下書きです", paragraphs[2].InnerText);

        // 節の見出し(Level 1 は Heading1、Level 2 は Heading2)と出典の見出し
        var headings = paragraphs
            .Where(p => StyleOf(p) is WordStyleIds.Heading1 or WordStyleIds.Heading2)
            .Select(p => (StyleOf(p), p.InnerText))
            .ToList();
        Assert.Equal(
            [
                (WordStyleIds.Heading1, "概要"),
                (WordStyleIds.Heading2, "詳細"),
                (WordStyleIds.Heading1, "次にすること"),
                (WordStyleIds.Heading1, "出典"),
            ],
            headings);

        // 箇条書き3項目と出典2件
        Assert.Equal(5, paragraphs.Count(p => StyleOf(p) == WordStyleIds.ListBullet));
        Assert.IsType<SectionProperties>(body.LastChild);
    }

    [Fact]
    public void CreateWordReport_WritesTableWithBoldHeaderAtFullWidth()
    {
        var result = TestSpecs.CreateFactory().CreateWordReport(TestSpecs.Word());

        using var document = Open(result);
        var table = Assert.Single(document.MainDocumentPart!.Document!.Body!.Elements<Table>());
        var properties = table.GetFirstChild<TableProperties>()!;
        Assert.Equal(WordStyleIds.TableGrid, properties.TableStyle?.Val?.Value);
        Assert.Equal(TableWidthUnitValues.Pct, properties.TableWidth?.Type?.Value);
        Assert.Equal("5000", properties.TableWidth?.Width?.Value);
        Assert.Equal(2, table.GetFirstChild<TableGrid>()!.Elements<GridColumn>().Count());

        var rows = table.Elements<TableRow>().ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal(["項目", "内容"], rows[0].Elements<TableCell>().Select(c => c.InnerText));
        Assert.All(rows[0].Descendants<Run>(), run => Assert.NotNull(run.RunProperties?.Bold));
        Assert.Equal(["状態", "一般提供"], rows[1].Elements<TableCell>().Select(c => c.InnerText));
        Assert.All(rows[1].Descendants<Run>(), run => Assert.Null(run.RunProperties?.Bold));
    }

    [Fact]
    public void CreateWordReport_WritesSourcesAsHyperlinks()
    {
        var result = TestSpecs.CreateFactory().CreateWordReport(TestSpecs.Word());

        using var document = Open(result);
        var mainPart = document.MainDocumentPart!;
        var relationships = mainPart.HyperlinkRelationships.ToList();
        Assert.Equal(2, relationships.Count);
        Assert.All(relationships, r => Assert.True(r.IsExternal));
        Assert.Equal(
            [new Uri(TestSpecs.UpdateUrl), new Uri(TestSpecs.LearnUrl)],
            relationships.Select(r => r.Uri));

        var hyperlinks = mainPart.Document!.Body!.Descendants<Hyperlink>().ToList();
        Assert.Equal(2, hyperlinks.Count);
        Assert.Equal(relationships.Select(r => r.Id), hyperlinks.Select(h => h.Id?.Value));
        Assert.Equal("Azure の更新情報 (ID 559242)", hyperlinks[0].InnerText);
        Assert.All(
            hyperlinks.SelectMany(h => h.Descendants<Run>()),
            run => Assert.Equal(WordStyleIds.Hyperlink, run.RunProperties?.RunStyle?.Val?.Value));
    }

    [Fact]
    public void CreateWordReport_SetsPackageProperties()
    {
        var result = TestSpecs.CreateFactory().CreateWordReport(TestSpecs.Word());

        using var document = Open(result);
        var properties = document.PackageProperties;
        Assert.Equal("Azure Container Apps express の一般提供", properties.Title);
        Assert.Equal("Azure Updates Cowork", properties.Creator);
        Assert.Equal(TestSpecs.UtcNow.UtcDateTime, properties.Created?.ToUniversalTime());
        Assert.Equal("ja-JP", properties.Language);
        Assert.Null(document.ThumbnailPart);
    }

    [Fact]
    public void CreateWordReport_WithJapaneseEmojiAndXmlInvalidChars_DoesNotThrow()
    {
        const string Noisy = "日本語😀👍🏽\u0000\u0001\u000B\u001F\uFFFE\uFFFF\uD800\uDC00\uDFFF末尾\r\n次の行\tタブ\u007F\u0085";
        var spec = TestSpecs.Word() with
        {
            Title = "タイトル" + Noisy,
            Subtitle = Noisy,
            Sections =
            [
                new("見出し" + Noisy, 1,
                    Paragraphs: [Noisy],
                    Bullets: [Noisy],
                    Table: new ReportTable([Noisy, "列"], [[Noisy, "値"]])),
            ],
            Sources = [new("出典" + Noisy, TestSpecs.UpdateUrl)],
        };

        var result = TestSpecs.CreateFactory().CreateWordReport(spec);

        Assert.Empty(result.ValidationErrors);
        using var document = Open(result);
        var text = document.MainDocumentPart!.Document!.Body!.InnerText;
        Assert.Contains("日本語😀👍🏽\U00010000末尾", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\u0001', text);
        Assert.DoesNotContain('\uFFFF', text);
        Assert.DoesNotContain(document.MainDocumentPart.Document.Body.Descendants<Text>(), t => t.Text.Contains('\uDFFF', StringComparison.Ordinal));
        Assert.NotEmpty(document.MainDocumentPart.Document.Body.Descendants<Break>());
        Assert.NotEmpty(document.MainDocumentPart.Document.Body.Descendants<TabChar>());
    }

    [Fact]
    public void CreateWordReport_InvalidSpec_ThrowsArgumentExceptionWithJapaneseMessage()
    {
        var spec = TestSpecs.Word() with { Sources = [] };

        var exception = Assert.Throws<ArgumentException>(() => TestSpecs.CreateFactory().CreateWordReport(spec));

        Assert.Contains("sources に出典を 1 件以上", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateWordReport_SameSpec_ProducesSameDocumentXml()
    {
        var factory = TestSpecs.CreateFactory();

        using var first = Open(factory.CreateWordReport(TestSpecs.Word()));
        using var second = Open(factory.CreateWordReport(TestSpecs.Word()));

        Assert.Equal(first.MainDocumentPart!.Document!.OuterXml, second.MainDocumentPart!.Document!.OuterXml);
    }

    private static WordprocessingDocument Open(OfficeDocument result) =>
        WordprocessingDocument.Open(new MemoryStream(result.Content, writable: false), false);

    private static string? StyleOf(Paragraph paragraph) => paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
}
