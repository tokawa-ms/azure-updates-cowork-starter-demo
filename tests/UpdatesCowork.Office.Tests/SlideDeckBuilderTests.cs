using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Microsoft.Extensions.Logging;
using UpdatesCowork.Shared.Contracts;
using A = DocumentFormat.OpenXml.Drawing;

namespace UpdatesCowork.Office.Tests;

public sealed class SlideDeckBuilderTests
{
    [Fact]
    public void CreateSlideDeck_ReturnsPptxWithCoverBodyAndSourcesAndNoValidationErrors()
    {
        var result = TestSpecs.CreateFactory().CreateSlideDeck(TestSpecs.Deck());

        Assert.Empty(result.ValidationErrors);
        Assert.Equal(ArtifactKinds.PptxContentType, result.ContentType);
        Assert.Equal(ArtifactKinds.Pptx, result.Extension);
        Assert.Equal(5, result.ItemCount);

        using var document = Open(result);
        var slides = GetSlides(document);
        Assert.Equal(5, slides.Count);
        Assert.Null(document.ThumbnailPart);
    }

    [Fact]
    public void CreateSlideDeck_WritesTitlesAndLayoutsInOrder()
    {
        var result = TestSpecs.CreateFactory().CreateSlideDeck(TestSpecs.Deck());

        using var document = Open(result);
        var slides = GetSlides(document);

        Assert.Equal(
            ["Azure Container Apps express の一般提供", "何が変わったか", "使い方", "次にすること", "出典"],
            slides.Select(GetTitle));
        Assert.Equal(
            ["Title Slide", "Title and Content", "Title and Content", "Title and Content", "Title and Content"],
            slides.Select(s => s.SlideLayoutPart?.SlideLayout?.CommonSlideData?.Name?.Value));

        var coverPlaceholders = GetPlaceholders(slides[0]);
        Assert.Contains(coverPlaceholders, p => p.Type?.Value == PlaceholderValues.CenteredTitle);
        var subtitle = Assert.Single(slides[0].Slide!.Descendants<Shape>(), s => GetPlaceholder(s)?.Type?.Value == PlaceholderValues.SubTitle);
        Assert.Equal(1U, GetPlaceholder(subtitle)!.Index?.Value);
        Assert.Equal("Azure の更新情報の要約 (2026-11-11)", subtitle.TextBody!.InnerText);

        var body = Assert.Single(slides[1].Slide!.Descendants<Shape>(), s => GetPlaceholder(s)?.Index?.Value == 1U);
        Assert.Equal(["一般提供になった", "SLA が付いた"], body.TextBody!.Elements<A.Paragraph>().Select(p => p.InnerText));
        Assert.All(slides.SelectMany(s => s.Slide!.Descendants<A.RunProperties>()), rp => Assert.Equal("ja-JP", rp.Language?.Value));
    }

    [Fact]
    public void CreateSlideDeck_UsesUniqueSlideIdsFrom256()
    {
        var result = TestSpecs.CreateFactory().CreateSlideDeck(TestSpecs.Deck());

        using var document = Open(result);
        var ids = document.PresentationPart!.Presentation!.SlideIdList!.Elements<SlideId>().Select(s => s.Id!.Value).ToList();

        Assert.Equal(5, ids.Count);
        Assert.All(ids, id => Assert.True(id >= 256U));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void CreateSlideDeck_AddsNotesOnlyToSlidesWithNotes()
    {
        var result = TestSpecs.CreateFactory().CreateSlideDeck(TestSpecs.Deck());

        using var document = Open(result);
        var slides = GetSlides(document);

        Assert.Equal([false, true, false, true, false], slides.Select(s => s.NotesSlidePart is not null));

        var notesPart = slides[1].NotesSlidePart!;
        Assert.NotNull(notesPart.NotesMasterPart);
        Assert.Same(slides[1], notesPart.SlidePart);
        Assert.Equal("話すときの補足", GetNotesText(notesPart));

        var multiLine = GetNotesBody(slides[3].NotesSlidePart!);
        Assert.Equal(["1 行目", "2 行目"], multiLine.TextBody!.Elements<A.Paragraph>().Select(p => p.InnerText));
    }

    [Fact]
    public void CreateSlideDeck_WritesSourcesSlideWithHyperlinks()
    {
        var result = TestSpecs.CreateFactory().CreateSlideDeck(TestSpecs.Deck());

        using var document = Open(result);
        var sourcesSlide = GetSlides(document)[^1];

        var relationships = sourcesSlide.HyperlinkRelationships.ToList();
        Assert.Equal([new Uri(TestSpecs.UpdateUrl), new Uri(TestSpecs.LearnUrl)], relationships.Select(r => r.Uri));
        var linkIds = sourcesSlide.Slide!.Descendants<A.HyperlinkOnClick>().Select(h => h.Id?.Value).Distinct().ToList();
        Assert.Equal(relationships.Select(r => r.Id), linkIds);
        Assert.Contains("Azure の更新情報 (ID 559242)", sourcesSlide.Slide.InnerText, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSlideDeck_TruncatesLongBulletWithEllipsis_AndLogsWarningInsteadOfValidationError()
    {
        var logger = new ListLogger<OfficeDocumentFactory>();
        var longBullet = string.Concat(Enumerable.Repeat("あ😀", 100));
        var spec = TestSpecs.Deck() with { Slides = [new SlideSpec("長い箇条書き", [longBullet, "短い"])] };

        var result = TestSpecs.CreateFactory(logger).CreateSlideDeck(spec);

        Assert.Empty(result.ValidationErrors);
        using var document = Open(result);
        var body = Assert.Single(GetSlides(document)[1].Slide!.Descendants<Shape>(), s => GetPlaceholder(s)?.Index?.Value == 1U);
        var texts = body.TextBody!.Elements<A.Paragraph>().Select(p => p.InnerText).ToList();
        Assert.Equal(120, OfficeText.TextElementLength(texts[0]));
        Assert.EndsWith("…", texts[0], StringComparison.Ordinal);
        Assert.StartsWith("あ😀あ😀", texts[0], StringComparison.Ordinal);
        Assert.Equal("短い", texts[1]);

        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal(4101, warning.EventId.Id);
    }

    [Fact]
    public void CreateSlideDeck_WithJapaneseEmojiAndXmlInvalidChars_DoesNotThrow()
    {
        const string Noisy = "日本語😀👍🏽\u0000\u0001\u000B\uFFFF\uD800末尾\r\n次の行\tタブ";
        var spec = new DeckSpec(
            "deck",
            "タイトル" + Noisy,
            Noisy,
            [new SlideSpec("スライド" + Noisy, [Noisy, Noisy], Noisy)],
            [new SourceRef(Noisy, TestSpecs.LearnUrl)]);

        var result = TestSpecs.CreateFactory().CreateSlideDeck(spec);

        Assert.Empty(result.ValidationErrors);
        using var document = Open(result);
        var slides = GetSlides(document);
        Assert.Equal(3, slides.Count);
        Assert.Equal("スライド日本語😀👍🏽末尾 次の行 タブ", GetTitle(slides[1]));
        Assert.Equal(["日本語😀👍🏽末尾", "次の行\tタブ"], GetNotesBody(slides[1].NotesSlidePart!).TextBody!.Elements<A.Paragraph>().Select(p => p.InnerText));
    }

    [Fact]
    public void CreateSlideDeck_InvalidSpec_ThrowsArgumentException()
    {
        var spec = TestSpecs.Deck() with { Slides = [] };

        var exception = Assert.Throws<ArgumentException>(() => TestSpecs.CreateFactory().CreateSlideDeck(spec));

        Assert.Contains("slides にスライドを 1 枚以上", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSlideDeck_SameSpec_ProducesSamePartsAndRelationships()
    {
        var factory = TestSpecs.CreateFactory();

        using var first = Open(factory.CreateSlideDeck(TestSpecs.Deck()));
        using var second = Open(factory.CreateSlideDeck(TestSpecs.Deck()));

        Assert.Equal(first.PresentationPart!.Presentation!.OuterXml, second.PresentationPart!.Presentation!.OuterXml);
        Assert.Equal(GetSlides(first).Select(Describe), GetSlides(second).Select(Describe));

        static string Describe(SlidePart slide) => string.Concat(
            slide.Slide!.OuterXml,
            slide.NotesSlidePart?.NotesSlide?.OuterXml,
            string.Join(",", slide.Parts.Select(p => p.RelationshipId)));
    }

    private static PresentationDocument Open(OfficeDocument result) =>
        PresentationDocument.Open(new MemoryStream(result.Content, writable: false), false);

    private static List<SlidePart> GetSlides(PresentationDocument document)
    {
        var presentationPart = document.PresentationPart!;
        return presentationPart.Presentation!.SlideIdList!.Elements<SlideId>()
            .Select(id => (SlidePart)presentationPart.GetPartById(id.RelationshipId!.Value!))
            .ToList();
    }

    private static PlaceholderShape? GetPlaceholder(Shape shape) =>
        shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape;

    private static List<PlaceholderShape> GetPlaceholders(SlidePart slide) =>
        slide.Slide!.Descendants<Shape>().Select(GetPlaceholder).OfType<PlaceholderShape>().ToList();

    private static string GetTitle(SlidePart slide) =>
        slide.Slide!.Descendants<Shape>()
            .Single(s => GetPlaceholder(s)?.Type?.Value is { } type && (type == PlaceholderValues.Title || type == PlaceholderValues.CenteredTitle))
            .TextBody!.InnerText;

    private static Shape GetNotesBody(NotesSlidePart notesPart) =>
        notesPart.NotesSlide!.Descendants<Shape>().Single(s => GetPlaceholder(s)?.Type?.Value == PlaceholderValues.Body);

    private static string GetNotesText(NotesSlidePart notesPart) => GetNotesBody(notesPart).TextBody!.InnerText;
}
