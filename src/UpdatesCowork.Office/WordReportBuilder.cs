using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UpdatesCowork.Shared.Contracts;

namespace UpdatesCowork.Office;

/// <summary>Word の報告書を作る(5.4節)。入力は <see cref="OfficeSpecValidator"/> で確かめ済みのものとする。</summary>
internal static class WordReportBuilder
{
    internal const string SourcesHeading = "出典";
    internal const string MetadataSuffix = " / 出典: Azure の更新情報 / この文書は AI が作成した下書きです";

    // 表の幅は 100% (pct の単位は 1/50 %)
    private const string FullTableWidth = "5000";

    // 本文の幅が取れないときの既定値(twip)
    private const int DefaultTextWidth = 9000;

    public static byte[] Build(WordReportSpec spec, DateTimeOffset now)
    {
        using var stream = OfficeTemplates.OpenWordTemplate();
        using (var document = WordprocessingDocument.Open(stream, true))
        {
            var mainPart = document.MainDocumentPart
                ?? throw new InvalidOperationException("Word のテンプレートに本文の部分がありません。");
            var body = mainPart.Document?.Body
                ?? throw new InvalidOperationException("Word のテンプレートに body がありません。");

            // 最後の SectionProperties だけを残して本文を消す
            var sectionProperties = body.Elements<SectionProperties>().LastOrDefault();
            body.RemoveAllChildren();
            var textWidth = GetTextWidth(sectionProperties);

            var title = OfficeText.SingleLine(spec.Title);
            body.Append(CreateParagraph(WordStyleIds.Title, title));
            var subtitle = OfficeText.SingleLine(spec.Subtitle);
            if (subtitle.Length > 0)
            {
                body.Append(CreateParagraph(WordStyleIds.Subtitle, subtitle));
            }

            body.Append(CreateParagraph(
                null,
                string.Concat("作成日: ", now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), MetadataSuffix)));

            foreach (var section in spec.Sections)
            {
                AppendSection(body, section, textWidth);
            }

            AppendSources(body, mainPart, spec.Sources);

            if (sectionProperties is not null)
            {
                body.Append(sectionProperties);
            }

            OfficeMetadata.Apply(document, title, subtitle, ToLanguageTag(spec.Language), now);

            // テンプレートのサムネイルは生成した中身と合わないので消す。
            if (document.ThumbnailPart is { } thumbnail)
            {
                document.DeletePart(thumbnail);
            }
        }

        return stream.ToArray();
    }

    private static void AppendSection(Body body, ReportSection section, int textWidth)
    {
        var headingStyle = section.Level >= 2 ? WordStyleIds.Heading2 : WordStyleIds.Heading1;
        body.Append(CreateParagraph(headingStyle, OfficeText.SingleLine(section.Heading)));

        foreach (var paragraph in section.Paragraphs ?? [])
        {
            if (!string.IsNullOrWhiteSpace(paragraph))
            {
                body.Append(CreateParagraph(null, paragraph));
            }
        }

        foreach (var bullet in section.Bullets ?? [])
        {
            if (!string.IsNullOrWhiteSpace(bullet))
            {
                body.Append(CreateParagraph(WordStyleIds.ListBullet, bullet));
            }
        }

        if (section.Table is { Columns.Count: > 0 } table)
        {
            body.Append(CreateTable(table, textWidth));
        }
    }

    private static void AppendSources(Body body, MainDocumentPart mainPart, IReadOnlyList<SourceRef> sources)
    {
        body.Append(CreateParagraph(WordStyleIds.Heading1, SourcesHeading));
        var linkNumber = 0;
        foreach (var source in sources)
        {
            var url = source.Url.Trim();
            // 出力を毎回同じにするため、テンプレートの rIdN と重ならない固定の ID を付ける
            var relationship = mainPart.AddHyperlinkRelationship(
                new Uri(url, UriKind.Absolute), true, string.Create(CultureInfo.InvariantCulture, $"aucLink{++linkNumber}"));
            var title = OfficeText.SingleLine(source.Title);
            if (title.Length == 0)
            {
                title = url;
            }

            var hyperlink = new Hyperlink(
                CreateRun(title, new RunProperties(new RunStyle { Val = WordStyleIds.Hyperlink })))
            {
                Id = relationship.Id,
                History = true,
            };

            var paragraph = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = WordStyleIds.ListBullet }));
            paragraph.Append(hyperlink);
            if (!string.Equals(title, url, StringComparison.Ordinal))
            {
                paragraph.Append(CreateRun(string.Concat(" (", url, ")"), null));
            }

            body.Append(paragraph);
        }
    }

    private static Table CreateTable(ReportTable table, int textWidth)
    {
        var columns = table.Columns;
        var columnCount = columns.Count;
        var columnWidth = (textWidth / columnCount).ToString(CultureInfo.InvariantCulture);

        var result = new Table(
            new TableProperties(
                new TableStyle { Val = WordStyleIds.TableGrid },
                new TableWidth { Width = FullTableWidth, Type = TableWidthUnitValues.Pct }));

        var grid = new TableGrid();
        for (var i = 0; i < columnCount; i++)
        {
            grid.Append(new GridColumn { Width = columnWidth });
        }

        result.Append(grid);

        // 1行目は見出し行。太字にし、ページをまたぐときは繰り返す
        var header = new TableRow(new TableRowProperties(new TableHeader()));
        foreach (var column in columns)
        {
            header.Append(CreateCell(column, columnWidth, bold: true));
        }

        result.Append(header);

        foreach (var row in table.Rows ?? [])
        {
            var tableRow = new TableRow();
            for (var i = 0; i < columnCount; i++)
            {
                var value = row is not null && i < row.Count ? row[i] : null;
                tableRow.Append(CreateCell(value, columnWidth, bold: false));
            }

            result.Append(tableRow);
        }

        return result;
    }

    private static TableCell CreateCell(string? text, string width, bool bold)
    {
        var runProperties = bold ? new RunProperties(new Bold()) : null;
        return new TableCell(
            new TableCellProperties(new TableCellWidth { Width = width, Type = TableWidthUnitValues.Dxa }),
            new Paragraph(CreateRun(text, runProperties)));
    }

    private static Paragraph CreateParagraph(string? styleId, string? text)
    {
        var paragraph = new Paragraph();
        if (styleId is not null)
        {
            paragraph.Append(new ParagraphProperties(new ParagraphStyleId { Val = styleId }));
        }

        paragraph.Append(CreateRun(text, null));
        return paragraph;
    }

    /// <summary>改行は <see cref="Break"/>、タブは <see cref="TabChar"/> にして文字を入れる。</summary>
    private static Run CreateRun(string? text, RunProperties? runProperties)
    {
        var run = new Run();
        if (runProperties is not null)
        {
            run.Append(runProperties);
        }

        var lines = OfficeText.Clean(text).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                run.Append(new Break());
            }

            var segments = lines[i].Split('\t');
            for (var j = 0; j < segments.Length; j++)
            {
                if (j > 0)
                {
                    run.Append(new TabChar());
                }

                if (segments[j].Length > 0)
                {
                    run.Append(new Text(segments[j]) { Space = SpaceProcessingModeValues.Preserve });
                }
            }
        }

        return run;
    }

    private static int GetTextWidth(SectionProperties? sectionProperties)
    {
        var pageWidth = sectionProperties?.GetFirstChild<PageSize>()?.Width?.Value;
        var margin = sectionProperties?.GetFirstChild<PageMargin>();
        var left = margin?.Left?.Value ?? 0;
        var right = margin?.Right?.Value ?? 0;
        if (pageWidth is uint width && width > left + right)
        {
            return (int)(width - left - right);
        }

        return DefaultTextWidth;
    }

    private static string ToLanguageTag(string? language) =>
        language is not null && language.Trim().StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? "en-US"
            : OfficeMetadata.JapaneseLanguage;
}
