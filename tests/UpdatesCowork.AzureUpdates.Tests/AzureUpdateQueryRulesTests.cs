namespace UpdatesCowork.AzureUpdates.Tests;

public sealed class AzureUpdateQueryRulesTests
{
    [Theory]
    [InlineData("559242", true)]
    [InlineData("abc-DEF-123", true)]
    [InlineData("", false)]
    [InlineData("../etc/passwd", false)]
    [InlineData("559242\n", false)]
    [InlineData("5592 42", false)]
    [InlineData("559242?x=1", false)]
    [InlineData("テスト", false)]
    public void IsValidId(string id, bool expected)
    {
        Assert.Equal(expected, AzureUpdateQueryRules.IsValidId(id));
    }

    [Fact]
    public void IsValidId_RejectsTooLong()
    {
        Assert.True(AzureUpdateQueryRules.IsValidId(new string('a', 200)));
        Assert.False(AzureUpdateQueryRules.IsValidId(new string('a', 201)));
        Assert.False(AzureUpdateQueryRules.IsValidId(null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Validate_RejectsTopOutOfRange(int top)
    {
        var errors = AzureUpdateQueryRules.Validate(new AzureUpdateQuery { Top = top });

        Assert.Single(errors);
        Assert.Contains("top", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsUnknownStatusLongKeywordAndControlChars()
    {
        var errors = AzureUpdateQueryRules.Validate(new AzureUpdateQuery
        {
            Status = "Deprecated",
            Keyword = new string('k', 101),
            Product = "Azure\u0007",
        });

        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public void Validate_AcceptsValidQuery()
    {
        var errors = AzureUpdateQueryRules.Validate(new AzureUpdateQuery
        {
            Keyword = "Container Apps",
            Product = "Azure",
            Status = "launched",
            ModifiedSince = new DateOnly(2026, 11, 1),
            Top = 20,
        });

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("2026-11-01", true)]
    [InlineData("2026/11/01", false)]
    [InlineData("2026-13-01", false)]
    [InlineData("20261101", false)]
    [InlineData(null, false)]
    public void TryParseDate(string? value, bool expected)
    {
        Assert.Equal(expected, AzureUpdateQueryRules.TryParseDate(value, out _));
    }

    [Theory]
    [InlineData("in PREVIEW", "In preview")]
    [InlineData(" Launched ", "Launched")]
    [InlineData("retirement", "Retirement")]
    public void Statuses_TryNormalize(string value, string expected)
    {
        Assert.True(AzureUpdateStatuses.TryNormalize(value, out var normalized));
        Assert.Equal(expected, normalized);
    }
}
