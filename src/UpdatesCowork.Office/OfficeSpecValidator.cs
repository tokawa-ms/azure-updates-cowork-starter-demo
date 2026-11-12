using System.Diagnostics.CodeAnalysis;
using UpdatesCowork.Shared.Contracts;

namespace UpdatesCowork.Office;

/// <summary>
/// <see cref="WordReportSpec"/> と <see cref="DeckSpec"/> の制約(6.5節)を確かめる。
/// エラーは、モデルが読んで直せる日本語の文で返す。
/// </summary>
public static class OfficeSpecValidator
{
    private static readonly string AllowedPrefixesText = string.Join(" か ", OfficeLimits.AllowedSourcePrefixes);

    /// <summary>Word の入力を確かめる。問題がなければ空のリストを返す。</summary>
    public static IReadOnlyList<string> Validate(WordReportSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var errors = new List<string>();
        ValidateFileName(spec.FileName, errors);
        ValidateTitle(spec.Title, errors);

        var sections = spec.Sections;
        if (sections is null || sections.Count == 0)
        {
            errors.Add("sections に節を 1 件以上入れてください。");
        }
        else
        {
            if (sections.Count > OfficeLimits.MaxSections)
            {
                errors.Add($"sections は {OfficeLimits.MaxSections} 件までです(今は {sections.Count} 件)。節をまとめて減らしてください。");
            }

            for (var i = 0; i < sections.Count; i++)
            {
                ValidateSection(sections[i], $"{i + 1} 番目の節", errors);
            }
        }

        ValidateSources(spec.Sources, "report", errors);
        return errors;
    }

    /// <summary>PowerPoint の入力を確かめる。問題がなければ空のリストを返す。</summary>
    public static IReadOnlyList<string> Validate(DeckSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var errors = new List<string>();
        ValidateFileName(spec.FileName, errors);
        ValidateTitle(spec.Title, errors);

        var slides = spec.Slides;
        if (slides is null || slides.Count == 0)
        {
            errors.Add("slides にスライドを 1 枚以上入れてください。");
        }
        else
        {
            if (slides.Count > OfficeLimits.MaxSlides)
            {
                errors.Add($"slides は {OfficeLimits.MaxSlides} 枚までです(今は {slides.Count} 枚。表紙と出典のスライドは自動で足すので数えません)。スライドをまとめて減らしてください。");
            }

            for (var i = 0; i < slides.Count; i++)
            {
                ValidateSlide(slides[i], $"{i + 1} 枚目のスライド", errors);
            }
        }

        ValidateSources(spec.Sources, "deck", errors);
        return errors;
    }

    /// <summary>
    /// 出典の URL が使えるかどうか。<see cref="OfficeLimits.AllowedSourcePrefixes"/> のどれかで始まる https の URL だけを認める。
    /// </summary>
    public static bool IsAllowedSourceUrl([NotNullWhen(true)] string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || uri.UserInfo.Length > 0
            || !uri.IsDefaultPort)
        {
            return false;
        }

        foreach (var prefix in OfficeLimits.AllowedSourcePrefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(prefix, UriKind.Absolute, out var prefixUri)
                && string.Equals(uri.Host, prefixUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateFileName(string? fileName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            errors.Add("fileName (ファイル名) を指定してください。拡張子は不要です。例: azure-update-559242-summary");
            return;
        }

        var baseName = OfficeFileNames.RemoveKnownExtension(fileName.Trim());
        if (baseName.Length > OfficeLimits.MaxFileNameLength)
        {
            errors.Add($"fileName (ファイル名) は {OfficeLimits.MaxFileNameLength} 文字までです。短くしてください。");
        }
        else if (OfficeFileNames.Sanitize(fileName).Length == 0)
        {
            errors.Add("fileName (ファイル名) には半角英数字とハイフン (-) を使ってください。例: azure-update-559242-summary");
        }
    }

    private static void ValidateTitle(string? title, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(OfficeText.SingleLine(title)))
        {
            errors.Add("title (タイトル) を指定してください。");
        }
    }

    private static void ValidateSection(ReportSection? section, string label, List<string> errors)
    {
        if (section is null)
        {
            errors.Add($"{label}が空です。heading (見出し) を持つ節を指定してください。");
            return;
        }

        if (string.IsNullOrWhiteSpace(OfficeText.SingleLine(section.Heading)))
        {
            errors.Add($"{label}の heading (見出し) を指定してください。");
        }

        if (section.Paragraphs is { Count: > OfficeLimits.MaxParagraphsPerSection } paragraphs)
        {
            errors.Add($"{label}の paragraphs (段落) は {OfficeLimits.MaxParagraphsPerSection} 件までです(今は {paragraphs.Count} 件)。段落をまとめて減らしてください。");
        }

        if (section.Bullets is { Count: > OfficeLimits.MaxBulletsPerSection } bullets)
        {
            errors.Add($"{label}の bullets (箇条書き) は {OfficeLimits.MaxBulletsPerSection} 項目までです(今は {bullets.Count} 項目)。項目をまとめて減らしてください。");
        }

        if (section.Table is { } table)
        {
            ValidateTable(table, label, errors);
        }
    }

    private static void ValidateTable(ReportTable table, string label, List<string> errors)
    {
        var columnCount = table.Columns?.Count ?? 0;
        if (columnCount == 0)
        {
            errors.Add($"{label}の表に columns (列の見出し) を 1 つ以上指定してください。表が不要なら table を省いてください。");
            return;
        }

        if (columnCount > OfficeLimits.MaxTableColumns)
        {
            errors.Add($"{label}の表は {OfficeLimits.MaxTableColumns} 列までです(今は {columnCount} 列)。列を減らしてください。");
        }

        var rows = table.Rows;
        if (rows is null)
        {
            return;
        }

        if (rows.Count > OfficeLimits.MaxTableRows)
        {
            errors.Add($"{label}の表は {OfficeLimits.MaxTableRows} 行までです(今は {rows.Count} 行)。行を減らしてください。");
        }

        for (var r = 0; r < rows.Count; r++)
        {
            var cellCount = rows[r]?.Count ?? 0;
            if (cellCount != columnCount)
            {
                errors.Add($"{label}の表の {r + 1} 行目は値が {cellCount} 個ありますが、列は {columnCount} 列です。各行の値の数を列の数に合わせてください。");
                break;
            }
        }
    }

    private static void ValidateSlide(SlideSpec? slide, string label, List<string> errors)
    {
        if (slide is null)
        {
            errors.Add($"{label}が空です。title (タイトル) を持つスライドを指定してください。");
            return;
        }

        if (string.IsNullOrWhiteSpace(OfficeText.SingleLine(slide.Title)))
        {
            errors.Add($"{label}の title (タイトル) を指定してください。");
        }

        if (slide.Bullets is { Count: > OfficeLimits.MaxBulletsPerSlide } bullets)
        {
            errors.Add($"{label}の bullets (箇条書き) は {OfficeLimits.MaxBulletsPerSlide} 項目までです(今は {bullets.Count} 項目)。項目をまとめるか、スライドを分けてください。");
        }

        var notesLength = OfficeText.TextElementLength(slide.Notes);
        if (notesLength > OfficeLimits.MaxNotesLength)
        {
            errors.Add($"{label}の notes (ノート) は {OfficeLimits.MaxNotesLength} 文字までです(今は {notesLength} 文字)。短くしてください。");
        }
    }

    private static void ValidateSources(IReadOnlyList<SourceRef>? sources, string parameter, List<string> errors)
    {
        if (sources is null || sources.Count == 0)
        {
            errors.Add($"{parameter}.sources に出典を 1 件以上入れてください。sources は {parameter} オブジェクトの内側に置き、各出典に title と url を指定してください。");
            return;
        }

        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            if (source is null)
            {
                errors.Add($"{i + 1} 番目の出典が空です。title と url を指定してください。");
                continue;
            }

            if (!IsAllowedSourceUrl(source.Url))
            {
                errors.Add($"{i + 1} 番目の出典の url は {AllowedPrefixesText} で始まるものだけ使えます。表示中の更新情報か Microsoft Learn の URL を指定してください。");
            }
        }
    }
}
