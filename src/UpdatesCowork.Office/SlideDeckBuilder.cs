using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Microsoft.Extensions.Logging;
using UpdatesCowork.Shared.Contracts;
using A = DocumentFormat.OpenXml.Drawing;

namespace UpdatesCowork.Office;

/// <summary>PowerPoint のスライドを作る(5.4節)。入力は <see cref="OfficeSpecValidator"/> で確かめ済みのものとする。</summary>
internal static class SlideDeckBuilder
{
    internal const string TitleSlideLayoutName = "Title Slide";
    internal const string ContentLayoutName = "Title and Content";
    internal const string SourcesSlideTitle = "出典";
    internal const int MaxBulletLength = 120;

    private const string AlternativeLanguage = "en-US";

    // 出典のスライドは項目が多くなりやすいので文字を小さくする(1/100 pt)
    private const int SourceFontSize = 1600;
    private const int SourceUrlFontSize = 1200;

    public static byte[] Build(DeckSpec spec, DateTimeOffset now, ILogger logger)
    {
        using var stream = OfficeTemplates.OpenDeckTemplate();
        using (var document = PresentationDocument.Open(stream, true))
        {
            var presentationPart = document.PresentationPart
                ?? throw new InvalidOperationException("PowerPoint のテンプレートに presentation がありません。");
            var writer = new DeckWriter(presentationPart);
            var titleLayout = FindLayout(presentationPart, TitleSlideLayoutName);
            var contentLayout = FindLayout(presentationPart, ContentLayoutName);

            var title = OfficeText.SingleLine(spec.Title);
            var subtitle = OfficeText.SingleLine(spec.Subtitle);
            AddCoverSlide(writer, titleLayout, title, subtitle);

            for (var i = 0; i < spec.Slides.Count; i++)
            {
                AddBodySlide(writer, contentLayout, spec.Slides[i], i + 1, logger);
            }

            AddSourcesSlide(writer, contentLayout, spec.Sources);

            OfficeMetadata.Apply(document, title, subtitle, OfficeMetadata.JapaneseLanguage, now);

            // テンプレートのサムネイルは生成した中身と合わないので消す。
            if (document.ThumbnailPart is { } thumbnail)
            {
                document.DeletePart(thumbnail);
            }
        }

        return stream.ToArray();
    }

    private static void AddCoverSlide(DeckWriter writer, SlideLayoutPart layout, string title, string subtitle)
    {
        var slidePart = writer.AddSlide(layout);
        var shapeTree = GetShapeTree(slidePart);
        shapeTree.Append(CreatePlaceholder(
            2U, "Title 1", new PlaceholderShape { Type = PlaceholderValues.CenteredTitle }, [CreateParagraph(CreateRun(title))]));
        if (subtitle.Length > 0)
        {
            shapeTree.Append(CreatePlaceholder(
                3U, "Subtitle 2", new PlaceholderShape { Type = PlaceholderValues.SubTitle, Index = 1U }, [CreateParagraph(CreateRun(subtitle))]));
        }
    }

    private static void AddBodySlide(DeckWriter writer, SlideLayoutPart layout, SlideSpec slide, int slideNumber, ILogger logger)
    {
        var slidePart = writer.AddSlide(layout);
        var shapeTree = GetShapeTree(slidePart);
        shapeTree.Append(CreatePlaceholder(
            2U, "Title 1", new PlaceholderShape { Type = PlaceholderValues.Title }, [CreateParagraph(CreateRun(OfficeText.SingleLine(slide.Title)))]));

        var bullets = PrepareBullets(slide.Bullets, slideNumber, logger);
        if (bullets.Count > 0)
        {
            shapeTree.Append(CreatePlaceholder(
                3U, "Content Placeholder 2", new PlaceholderShape { Index = 1U }, bullets.Select(b => CreateParagraph(CreateRun(b)))));
        }

        var notes = OfficeText.Clean(slide.Notes);
        if (!string.IsNullOrWhiteSpace(notes))
        {
            writer.AddNotes(slidePart, notes);
        }
    }

    private static void AddSourcesSlide(DeckWriter writer, SlideLayoutPart layout, IReadOnlyList<SourceRef> sources)
    {
        var slidePart = writer.AddSlide(layout);
        var shapeTree = GetShapeTree(slidePart);
        shapeTree.Append(CreatePlaceholder(
            2U, "Title 1", new PlaceholderShape { Type = PlaceholderValues.Title }, [CreateParagraph(CreateRun(SourcesSlideTitle))]));

        var paragraphs = new List<A.Paragraph>(sources.Count);
        var linkNumber = 0;
        foreach (var source in sources)
        {
            var url = source.Url.Trim();
            var relationshipId = slidePart.AddHyperlinkRelationship(
                new Uri(url, UriKind.Absolute), true, string.Create(CultureInfo.InvariantCulture, $"aucLink{++linkNumber}")).Id;
            var title = OfficeText.SingleLine(source.Title);
            if (title.Length == 0 || string.Equals(title, url, StringComparison.Ordinal))
            {
                paragraphs.Add(CreateParagraph(CreateRun(url, SourceFontSize, relationshipId)));
            }
            else
            {
                paragraphs.Add(CreateParagraph(
                    CreateRun(title, SourceFontSize, relationshipId),
                    CreateRun(string.Concat(" (", url, ")"), SourceUrlFontSize, relationshipId)));
            }
        }

        shapeTree.Append(CreatePlaceholder(
            3U, "Content Placeholder 2", new PlaceholderShape { Index = 1U }, paragraphs, new A.BodyProperties(new A.NormalAutoFit())));
    }

    /// <summary>箇条書きを1行にし、1枚6項目、1項目120文字までにそろえる。超えた分は警告としてログに残す。</summary>
    private static List<string> PrepareBullets(IReadOnlyList<string>? bullets, int slideNumber, ILogger logger)
    {
        var result = new List<string>();
        if (bullets is null)
        {
            return result;
        }

        for (var i = 0; i < bullets.Count; i++)
        {
            var text = OfficeText.SingleLine(bullets[i]);
            if (text.Length == 0)
            {
                continue;
            }

            if (result.Count == OfficeLimits.MaxBulletsPerSlide)
            {
                OfficeLog.BulletsDropped(logger, slideNumber, OfficeLimits.MaxBulletsPerSlide, bullets.Count - i);
                break;
            }

            text = OfficeText.Truncate(text, MaxBulletLength, out var truncated);
            if (truncated)
            {
                OfficeLog.BulletTruncated(logger, slideNumber, i + 1, MaxBulletLength);
            }

            result.Add(text);
        }

        return result;
    }

    private static SlideLayoutPart FindLayout(PresentationPart presentationPart, string name) =>
        presentationPart.SlideMasterParts
            .SelectMany(master => master.SlideLayoutParts)
            .FirstOrDefault(layout => string.Equals(layout.SlideLayout?.CommonSlideData?.Name?.Value, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"PowerPoint のテンプレートにレイアウト '{name}' がありません。");

    private static ShapeTree GetShapeTree(SlidePart slidePart) =>
        slidePart.Slide?.CommonSlideData?.ShapeTree
        ?? throw new InvalidOperationException("スライドに ShapeTree がありません。");

    private static ShapeTree CreateShapeTree() =>
        new(
            new NonVisualGroupShapeProperties(
                new NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
                new NonVisualGroupShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new GroupShapeProperties(new A.TransformGroup()));

    /// <summary>プレースホルダーの図形を作る。位置はレイアウトから引き継ぐので ShapeProperties は空にする。</summary>
    private static Shape CreatePlaceholder(
        uint id,
        string name,
        PlaceholderShape placeholder,
        IEnumerable<A.Paragraph> paragraphs,
        A.BodyProperties? bodyProperties = null)
    {
        var textBody = new TextBody(bodyProperties ?? new A.BodyProperties(), new A.ListStyle());
        textBody.Append(paragraphs);
        if (textBody.GetFirstChild<A.Paragraph>() is null)
        {
            textBody.Append(new A.Paragraph(CreateEndParagraphRunProperties()));
        }

        return new Shape(
            new NonVisualShapeProperties(
                new NonVisualDrawingProperties { Id = id, Name = name },
                new NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new ApplicationNonVisualDrawingProperties(placeholder)),
            new ShapeProperties(),
            textBody);
    }

    private static A.Paragraph CreateParagraph(params A.Run[] runs)
    {
        var paragraph = new A.Paragraph();
        paragraph.Append(runs);
        paragraph.Append(CreateEndParagraphRunProperties());
        return paragraph;
    }

    private static A.Run CreateRun(string text, int? fontSize = null, string? hyperlinkRelationshipId = null)
    {
        var runProperties = new A.RunProperties
        {
            Language = OfficeMetadata.JapaneseLanguage,
            AlternativeLanguage = AlternativeLanguage,
        };
        if (fontSize is int size)
        {
            runProperties.FontSize = size;
        }

        if (hyperlinkRelationshipId is not null)
        {
            runProperties.Append(new A.HyperlinkOnClick { Id = hyperlinkRelationshipId });
        }

        return new A.Run(runProperties, new A.Text(text));
    }

    private static A.EndParagraphRunProperties CreateEndParagraphRunProperties() =>
        new() { Language = OfficeMetadata.JapaneseLanguage, AlternativeLanguage = AlternativeLanguage };

    /// <summary>スライドの部分、スライド ID、ノートを足していく。</summary>
    private sealed class DeckWriter
    {
        // スライド ID は 256 以上で重複しない値にする
        private const uint FirstSlideId = 256U;

        private readonly PresentationPart _presentationPart;
        private readonly SlideIdList _slideIdList;
        private readonly NotesMasterPart _notesMasterPart;
        private uint _nextSlideId;

        public DeckWriter(PresentationPart presentationPart)
        {
            _presentationPart = presentationPart;
            var presentation = presentationPart.Presentation
                ?? throw new InvalidOperationException("PowerPoint のテンプレートに presentation がありません。");
            _slideIdList = presentation.SlideIdList ??= new SlideIdList();
            _notesMasterPart = presentationPart.NotesMasterPart
                ?? throw new InvalidOperationException("PowerPoint のテンプレートにノートマスターがありません。");

            var maxId = _slideIdList.Elements<SlideId>().Select(s => s.Id?.Value ?? 0U).DefaultIfEmpty(0U).Max();
            _nextSlideId = Math.Max(maxId + 1U, FirstSlideId);
        }

        public SlidePart AddSlide(SlideLayoutPart layoutPart)
        {
            // 出力を毎回同じにするため、テンプレートの rIdN と重ならない固定の関係 ID を付ける
            var slideNumber = _slideIdList.ChildElements.Count + 1;
            var slidePart = _presentationPart.AddNewPart<SlidePart>(
                string.Create(CultureInfo.InvariantCulture, $"aucSlide{slideNumber}"));
            slidePart.AddPart(layoutPart, "aucLayout");
            slidePart.Slide = new Slide(
                new CommonSlideData(CreateShapeTree()),
                new ColorMapOverride(new A.MasterColorMapping()));

            _slideIdList.Append(new SlideId
            {
                Id = _nextSlideId++,
                RelationshipId = _presentationPart.GetIdOfPart(slidePart),
            });
            return slidePart;
        }

        /// <summary>ノートの部分を作り、ノートマスターとスライドの両方に結びつける。</summary>
        public void AddNotes(SlidePart slidePart, string notes)
        {
            var notesPart = slidePart.AddNewPart<NotesSlidePart>("aucNotes");
            var shapeTree = CreateShapeTree();
            shapeTree.Append(new Shape(
                new NonVisualShapeProperties(
                    new NonVisualDrawingProperties { Id = 2U, Name = "Slide Image Placeholder 1" },
                    new NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true, NoRotation = true, NoChangeAspect = true }),
                    new ApplicationNonVisualDrawingProperties(new PlaceholderShape { Type = PlaceholderValues.SlideImage })),
                new ShapeProperties()));
            shapeTree.Append(CreatePlaceholder(
                3U,
                "Notes Placeholder 2",
                new PlaceholderShape { Type = PlaceholderValues.Body, Index = 1U },
                notes.Split('\n').Select(line => CreateParagraph(CreateRun(line)))));

            notesPart.NotesSlide = new NotesSlide(
                new CommonSlideData(shapeTree),
                new ColorMapOverride(new A.MasterColorMapping()));
            notesPart.AddPart(_notesMasterPart, "aucNotesMaster");
            notesPart.AddPart(slidePart, "aucSlide");
        }
    }
}
