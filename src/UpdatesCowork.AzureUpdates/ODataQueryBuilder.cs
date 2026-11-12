using System.Globalization;
using System.Text;

namespace UpdatesCowork.AzureUpdates;

/// <summary>データ API の OData の問い合わせ文字列を組み立てる。</summary>
internal static class ODataQueryBuilder
{
    /// <summary>
    /// 一覧の問い合わせ文字列(先頭の <c>?</c> を含む)を作る。<c>$</c> を含む名前と値は URL エンコードする。
    /// </summary>
    /// <param name="query">検証済みの条件。</param>
    /// <param name="top">データ API に求める件数。</param>
    public static string BuildListQuery(AzureUpdateQuery query, int top)
    {
        ArgumentNullException.ThrowIfNull(query);
        var builder = new StringBuilder("?");
        Append(builder, "$count", "true");
        Append(builder, "$top", top.ToString(CultureInfo.InvariantCulture));
        Append(builder, "$orderby", "modified desc");

        var filter = BuildFilter(query);
        if (filter is not null)
        {
            Append(builder, "$filter", filter);
        }

        return builder.ToString();
    }

    /// <summary>状態、タグ、更新日の <c>$filter</c> を作る。条件がなければ null。</summary>
    public static string? BuildFilter(AzureUpdateQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var clauses = new List<string>();
        if (AzureUpdateStatuses.TryNormalize(query.Status, out var status))
        {
            clauses.Add(status == AzureUpdateStatuses.Retirement
                ? $"tags/any(t:t eq '{EscapeLiteral(AzureUpdateStatuses.RetirementTag)}')"
                : $"status eq '{EscapeLiteral(status)}'");
        }

        if (query.ModifiedSince is { } since)
        {
            clauses.Add("modified ge " + since.ToString(AzureUpdateQueryRules.DateFormat, CultureInfo.InvariantCulture));
        }

        return clauses.Count == 0 ? null : string.Join(" and ", clauses);
    }

    /// <summary>OData の文字列リテラルの中で使えるよう、単一引用符を 2 つ重ねる。</summary>
    public static string EscapeLiteral(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static void Append(StringBuilder builder, string name, string value)
    {
        if (builder.Length > 1)
        {
            builder.Append('&');
        }

        builder.Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
    }
}
