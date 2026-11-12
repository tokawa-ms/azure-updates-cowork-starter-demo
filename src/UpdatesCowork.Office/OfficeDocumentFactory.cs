using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UpdatesCowork.Shared.Contracts;

namespace UpdatesCowork.Office;

/// <summary>Word と PowerPoint を作る(5.4節)。</summary>
/// <remarks>
/// 入力が 6.5 節の制約を満たさないときは <see cref="ArgumentException"/> を投げる。
/// ツールからは先に <see cref="OfficeSpecValidator"/> で確かめ、モデルが直せる文を返すこと。
/// 生成した文書は保存後に OpenXmlValidator で検証し、エラーを <see cref="OfficeDocument.ValidationErrors"/> に入れる。
/// </remarks>
public sealed class OfficeDocumentFactory : IOfficeDocumentFactory
{
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;

    public OfficeDocumentFactory(ILogger<OfficeDocumentFactory>? logger = null, TimeProvider? timeProvider = null)
    {
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public OfficeDocument CreateWordReport(WordReportSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ThrowIfInvalid(OfficeSpecValidator.Validate(spec), nameof(spec));

        var content = WordReportBuilder.Build(spec, _timeProvider.GetLocalNow());
        var errors = OfficePackageValidator.ValidateWordprocessing(content);
        if (errors.Length > 0)
        {
            OfficeLog.ValidationFailed(_logger, ArtifactKinds.Docx, errors.Length);
        }

        return new OfficeDocument(content, ArtifactKinds.DocxContentType, ArtifactKinds.Docx, spec.Sections.Count, errors);
    }

    public OfficeDocument CreateSlideDeck(DeckSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ThrowIfInvalid(OfficeSpecValidator.Validate(spec), nameof(spec));

        var content = SlideDeckBuilder.Build(spec, _timeProvider.GetLocalNow(), _logger);
        var errors = OfficePackageValidator.ValidatePresentation(content);
        if (errors.Length > 0)
        {
            OfficeLog.ValidationFailed(_logger, ArtifactKinds.Pptx, errors.Length);
        }

        // 表紙 + 本文 + 出典
        var slideCount = spec.Slides.Count + 2;
        return new OfficeDocument(content, ArtifactKinds.PptxContentType, ArtifactKinds.Pptx, slideCount, errors);
    }

    private static void ThrowIfInvalid(IReadOnlyList<string> errors, string paramName)
    {
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), paramName);
        }
    }
}
