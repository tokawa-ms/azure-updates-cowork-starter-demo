namespace UpdatesCowork.Shared.Contracts;

/// <summary>成果物の情報(6.5節の <c>artifact</c>)。</summary>
public sealed record ArtifactInfo(
    string ArtifactId,
    string FileName,
    string ContentType,
    string Kind,
    long SizeBytes,
    int ItemCount,
    string Storage,
    string Location,
    string? SessionId);

/// <summary>成果物ツールの戻り値。失敗時は <see cref="Error"/> にモデルが直せる文を入れる。</summary>
public sealed record ArtifactResult(bool Ok, ArtifactInfo? Artifact = null, string? Error = null)
{
    public static ArtifactResult Success(ArtifactInfo artifact) => new(true, artifact);

    public static ArtifactResult Failure(string error) => new(false, null, error);
}

/// <summary>成果物の種類と保存先の値。</summary>
public static class ArtifactKinds
{
    public const string Docx = "docx";
    public const string Pptx = "pptx";

    public const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string PptxContentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
}

public static class ArtifactStorages
{
    public const string Local = "local";
    public const string Session = "session";
}

/// <summary>出典。</summary>
public sealed record SourceRef(string Title, string Url);

/// <summary>Word の表。</summary>
public sealed record ReportTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>Word の節。</summary>
public sealed record ReportSection(
    string Heading,
    int Level = 1,
    IReadOnlyList<string>? Paragraphs = null,
    IReadOnlyList<string>? Bullets = null,
    ReportTable? Table = null);

/// <summary><c>create_word_report</c> の入力(6.5節)。</summary>
public sealed record WordReportSpec(
    string FileName,
    string Title,
    string? Subtitle,
    string? Language,
    IReadOnlyList<ReportSection> Sections,
    IReadOnlyList<SourceRef> Sources);

/// <summary>スライド1枚。</summary>
public sealed record SlideSpec(string Title, IReadOnlyList<string>? Bullets = null, string? Notes = null);

/// <summary><c>create_powerpoint_deck</c> の入力(6.5節)。</summary>
public sealed record DeckSpec(
    string FileName,
    string Title,
    string? Subtitle,
    IReadOnlyList<SlideSpec> Slides,
    IReadOnlyList<SourceRef> Sources);

/// <summary>Office の入力の上限(6.5節)。</summary>
public static class OfficeLimits
{
    public const int MaxSections = 12;
    public const int MaxParagraphsPerSection = 20;
    public const int MaxBulletsPerSection = 20;
    public const int MaxTableColumns = 10;
    public const int MaxTableRows = 30;
    public const int MaxSlides = 12;
    public const int MaxBulletsPerSlide = 6;
    public const int MaxNotesLength = 1500;
    public const int MaxFileNameLength = 60;

    public static IReadOnlyList<string> AllowedSourcePrefixes { get; } =
        ["https://azure.microsoft.com/", "https://learn.microsoft.com/"];
}
