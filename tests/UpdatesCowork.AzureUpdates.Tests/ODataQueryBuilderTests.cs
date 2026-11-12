namespace UpdatesCowork.AzureUpdates.Tests;

public sealed class ODataQueryBuilderTests
{
    [Fact]
    public void BuildListQuery_EncodesDollarNamesAndOrdersByModified()
    {
        var query = ODataQueryBuilder.BuildListQuery(new AzureUpdateQuery(), 10);

        Assert.Equal("?%24count=true&%24top=10&%24orderby=modified%20desc", query);
    }

    [Fact]
    public void BuildFilter_CombinesStatusAndModifiedSince()
    {
        var filter = ODataQueryBuilder.BuildFilter(new AzureUpdateQuery
        {
            Status = "in preview",
            ModifiedSince = new DateOnly(2026, 11, 1),
        });

        Assert.Equal("status eq 'In preview' and modified ge 2026-11-01", filter);
    }

    [Fact]
    public void BuildFilter_RetirementUsesTag()
    {
        var filter = ODataQueryBuilder.BuildFilter(new AzureUpdateQuery { Status = "Retirement" });

        Assert.Equal("tags/any(t:t eq 'Retirements')", filter);
    }

    [Fact]
    public void BuildFilter_NoConditions_ReturnsNull()
    {
        Assert.Null(ODataQueryBuilder.BuildFilter(new AzureUpdateQuery { Keyword = "ignored" }));
    }

    [Fact]
    public void BuildFilter_UnknownStatus_IsNotPassedThrough()
    {
        // 検証を通らない状態は $filter に入れない
        var filter = ODataQueryBuilder.BuildFilter(new AzureUpdateQuery { Status = "x' or 1 eq 1 or status eq 'y" });

        Assert.Null(filter);
    }

    [Fact]
    public void BuildListQuery_EncodesFilterValue()
    {
        var query = ODataQueryBuilder.BuildListQuery(new AzureUpdateQuery { Status = "In development" }, 200);

        Assert.Contains("%24top=200", query, StringComparison.Ordinal);
        Assert.Contains("%24filter=status%20eq%20%27In%20development%27", query, StringComparison.Ordinal);
    }

    [Fact]
    public void EscapeLiteral_DoublesSingleQuotes()
    {
        Assert.Equal("O''Brien''s", ODataQueryBuilder.EscapeLiteral("O'Brien's"));
    }
}
