using UpdatesCowork.Shared.Contracts;

namespace UpdatesCowork.Office;

/// <summary>Word と PowerPoint の生成(5.4節)。</summary>
public interface IOfficeDocumentFactory
{
    OfficeDocument CreateWordReport(WordReportSpec spec);

    OfficeDocument CreateSlideDeck(DeckSpec spec);
}

/// <summary>生成した文書。<see cref="ItemCount"/> は Word では節の数、PowerPoint ではスライド数。</summary>
public sealed record OfficeDocument(
    byte[] Content,
    string ContentType,
    string Extension,
    int ItemCount,
    IReadOnlyList<string> ValidationErrors);
