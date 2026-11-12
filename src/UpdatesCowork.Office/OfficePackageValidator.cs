using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace UpdatesCowork.Office;

/// <summary>保存した文書を <see cref="OpenXmlValidator"/> で検証する。</summary>
internal static class OfficePackageValidator
{
    private const int MaxReportedErrors = 20;

    public static string[] ValidateWordprocessing(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
        return Validate(document);
    }

    public static string[] ValidatePresentation(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        return Validate(document);
    }

    private static string[] Validate(OpenXmlPackage package)
    {
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(package)
            .Take(MaxReportedErrors)
            .Select(error => $"{error.Path?.PartUri}: {error.Path?.XPath}: {error.Description}")
            .ToArray();
    }
}
