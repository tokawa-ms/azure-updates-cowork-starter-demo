namespace UpdatesCowork.Office.Tests;

public sealed class OfficeFileNamesTests
{
    [Theory]
    [InlineData("azure-update-559242-summary", "azure-update-559242-summary")]
    [InlineData("azure-update-559242-summary.docx", "azure-update-559242-summary")]
    [InlineData("Deck_v1.2.PPTX", "Deck-v1-2")]
    [InlineData("../../etc/passwd", "etc-passwd")]
    [InlineData("C:\\temp\\report name", "C-temp-report-name")]
    [InlineData("Azure 更新 要約", "Azure")]
    [InlineData("要約レポート", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    [InlineData("con", "file-con")]
    [InlineData("NUL", "file-NUL")]
    public void Sanitize_KeepsOnlySafeCharacters(string? input, string expected) =>
        Assert.Equal(expected, OfficeFileNames.Sanitize(input));

    [Fact]
    public void Sanitize_TruncatesToMaxLength()
    {
        var result = OfficeFileNames.Sanitize(new string('a', 200));

        Assert.Equal(60, result.Length);
    }

    [Theory]
    [InlineData("azure-update-559242-summary", "docx", "azure-update-559242-summary.docx")]
    [InlineData("deck.pptx", ".pptx", "deck.pptx")]
    [InlineData("要約", "docx", "azure-updates-cowork.docx")]
    public void WithExtension_AppendsExtension(string input, string extension, string expected) =>
        Assert.Equal(expected, OfficeFileNames.WithExtension(input, extension));
}
