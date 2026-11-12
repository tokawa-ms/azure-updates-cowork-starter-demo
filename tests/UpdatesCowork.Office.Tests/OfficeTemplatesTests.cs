using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace UpdatesCowork.Office.Tests;

public sealed class OfficeTemplatesTests
{
    [Fact]
    public void WordTemplate_Loads_And_HasAllRequiredStyleIds()
    {
        using var stream = OfficeTemplates.OpenWordTemplate();
        using var document = WordprocessingDocument.Open(stream, false);

        var styles = document.MainDocumentPart?.StyleDefinitionsPart?.Styles;
        Assert.NotNull(styles);
        var styleIds = styles.Elements<Style>()
            .Select(s => s.StyleId?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(OfficeTemplates.RequiredWordStyleIds, id => Assert.Contains(id, styleIds));
        Assert.NotNull(document.MainDocumentPart?.Document?.Body?.GetFirstChild<SectionProperties>());
    }

    [Fact]
    public void DeckTemplate_Loads_And_HasLayoutsAndNotesMaster()
    {
        using var stream = OfficeTemplates.OpenDeckTemplate();
        using var document = PresentationDocument.Open(stream, false);

        var presentationPart = document.PresentationPart;
        Assert.NotNull(presentationPart);
        Assert.NotNull(presentationPart.NotesMasterPart);

        var layoutNames = presentationPart.SlideMasterParts
            .SelectMany(m => m.SlideLayoutParts)
            .Select(l => l.SlideLayout?.CommonSlideData?.Name?.Value)
            .ToList();
        Assert.Contains(SlideDeckBuilder.TitleSlideLayoutName, layoutNames);
        Assert.Contains(SlideDeckBuilder.ContentLayoutName, layoutNames);

        var slideSize = presentationPart.Presentation?.SlideSize;
        Assert.Equal(12_192_000, slideSize?.Cx?.Value);
        Assert.Equal(6_858_000, slideSize?.Cy?.Value);
        Assert.Empty(presentationPart.SlideParts);
    }

    [Fact]
    public void OpenTemplate_ReturnsExpandableCopy()
    {
        using var first = OfficeTemplates.OpenWordTemplate();
        var length = first.Length;
        first.SetLength(length + 10);
        first.WriteByte(1);

        using var second = OfficeTemplates.OpenWordTemplate();
        Assert.Equal(length, second.Length);
        Assert.Equal(0, second.Position);
    }
}
