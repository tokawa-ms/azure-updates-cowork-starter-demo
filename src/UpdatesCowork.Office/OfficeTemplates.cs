using System.Reflection;

namespace UpdatesCowork.Office;

/// <summary>埋め込みリソースのテンプレート(5.4節)。</summary>
internal static class OfficeTemplates
{
    internal const string WordResourceName = "UpdatesCowork.Office.Templates.report-template.docx";
    internal const string DeckResourceName = "UpdatesCowork.Office.Templates.deck-template.pptx";

    /// <summary>Word のテンプレートが持つべきスタイル ID。</summary>
    internal static readonly string[] RequiredWordStyleIds =
    [
        WordStyleIds.Title,
        WordStyleIds.Subtitle,
        WordStyleIds.Heading1,
        WordStyleIds.Heading2,
        WordStyleIds.Heading3,
        WordStyleIds.ListBullet,
        WordStyleIds.ListBullet2,
        WordStyleIds.ListNumber,
        WordStyleIds.TableGrid,
        WordStyleIds.Hyperlink,
        WordStyleIds.Quote,
        WordStyleIds.Caption,
    ];

    private static readonly Lazy<byte[]> WordTemplate = new(() => Load(WordResourceName));
    private static readonly Lazy<byte[]> DeckTemplate = new(() => Load(DeckResourceName));

    /// <summary>Word のテンプレートを、書き込みと拡張ができる <see cref="MemoryStream"/> に写して返す。</summary>
    public static MemoryStream OpenWordTemplate() => CreateWritableCopy(WordTemplate.Value);

    /// <summary>PowerPoint のテンプレートを、書き込みと拡張ができる <see cref="MemoryStream"/> に写して返す。</summary>
    public static MemoryStream OpenDeckTemplate() => CreateWritableCopy(DeckTemplate.Value);

    private static MemoryStream CreateWritableCopy(byte[] template)
    {
        // new MemoryStream(byte[]) は拡張できないため、空のストリームに書き写す
        var stream = new MemoryStream(template.Length * 2);
        stream.Write(template);
        stream.Position = 0;
        return stream;
    }

    private static byte[] Load(string resourceName)
    {
        using var resource = typeof(OfficeTemplates).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"埋め込みリソース '{resourceName}' が見つかりません。");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        return buffer.ToArray();
    }
}

/// <summary>Word のテンプレートのスタイル ID。</summary>
internal static class WordStyleIds
{
    internal const string Title = "Title";
    internal const string Subtitle = "Subtitle";
    internal const string Heading1 = "Heading1";
    internal const string Heading2 = "Heading2";
    internal const string Heading3 = "Heading3";
    internal const string ListBullet = "ListBullet";
    internal const string ListBullet2 = "ListBullet2";
    internal const string ListNumber = "ListNumber";
    internal const string TableGrid = "TableGrid";
    internal const string Hyperlink = "Hyperlink";
    internal const string Quote = "Quote";
    internal const string Caption = "Caption";
}
