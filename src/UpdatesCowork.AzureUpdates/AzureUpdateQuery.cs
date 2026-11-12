using System.Globalization;
using System.Text.RegularExpressions;

namespace UpdatesCowork.AzureUpdates;

/// <summary>一覧の問い合わせの条件。</summary>
public sealed record AzureUpdateQuery
{
    /// <summary>タイトルと製品名に対する部分一致(大文字小文字を区別しない)。</summary>
    public string? Keyword { get; init; }

    /// <summary>製品名に対する部分一致(大文字小文字を区別しない)。</summary>
    public string? Product { get; init; }

    /// <summary>状態。<see cref="AzureUpdateStatuses"/> のどれか。</summary>
    public string? Status { get; init; }

    /// <summary>この日以降に更新されたものだけにする。</summary>
    public DateOnly? ModifiedSince { get; init; }

    /// <summary>返す件数。</summary>
    public int Top { get; init; } = AzureUpdateQueryRules.DefaultTop;
}

/// <summary>検索で使える状態の値。</summary>
public static class AzureUpdateStatuses
{
    public const string Launched = "Launched";
    public const string InPreview = "In preview";
    public const string InDevelopment = "In development";

    /// <summary>退役。データ API の状態ではなく、タグ <c>Retirements</c> で絞る。</summary>
    public const string Retirement = "Retirement";

    /// <summary>退役を表すデータ API のタグ。</summary>
    public const string RetirementTag = "Retirements";

    public static IReadOnlyList<string> All { get; } = [Launched, InPreview, InDevelopment, Retirement];

    /// <summary>大文字小文字の違いを許して、正しい表記にそろえる。</summary>
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        foreach (var status in All)
        {
            if (string.Equals(status, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                normalized = status;
                return true;
            }
        }

        return false;
    }
}

/// <summary>問い合わせの入力の決まりと検証。エラーはモデルが読んで直せる日本語の文で返す。</summary>
public static partial class AzureUpdateQueryRules
{
    public const int MinTop = 1;
    public const int MaxTop = 20;
    public const int DefaultTop = 10;
    public const int MaxKeywordLength = 100;
    public const int MaxProductLength = 100;
    public const int MaxIdLength = 200;

    /// <summary><see cref="AzureUpdateQuery.Keyword"/> などで絞るときに、新しい順に調べる件数。</summary>
    public const int ScanSize = 200;

    /// <summary><c>modifiedSince</c> の書式。</summary>
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>更新 ID として正しいかどうか(<c>^[A-Za-z0-9-]{1,200}$</c>)。</summary>
    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    /// <summary><c>YYYY-MM-DD</c> の形の日付を読む。</summary>
    public static bool TryParseDate(string? value, out DateOnly date)
    {
        date = default;
        return !string.IsNullOrWhiteSpace(value)
            && DateOnly.TryParseExact(value.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>問い合わせを確かめる。問題がなければ空のリストを返す。</summary>
    public static IReadOnlyList<string> Validate(AzureUpdateQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var errors = new List<string>();
        if (query.Top is < MinTop or > MaxTop)
        {
            errors.Add($"top は {MinTop} から {MaxTop} の範囲で指定してください。");
        }

        ValidateText(query.Keyword, "keyword", MaxKeywordLength, errors);
        ValidateText(query.Product, "product", MaxProductLength, errors);

        if (query.Status is not null && !AzureUpdateStatuses.TryNormalize(query.Status, out _))
        {
            errors.Add($"status は {string.Join("、", AzureUpdateStatuses.All)} のどれかを指定してください。");
        }

        if (query.ModifiedSince is { } since && (since.Year < 2000 || since.Year > 2100))
        {
            errors.Add("modifiedSince は 2000 年から 2100 年までの日付を YYYY-MM-DD の形で指定してください。");
        }

        return errors;
    }

    /// <summary>空白だけの文字列を null にし、前後の空白を取り除く。</summary>
    public static string? NormalizeText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateText(string? value, string name, int maxLength, List<string> errors)
    {
        if (value is null)
        {
            return;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            errors.Add($"{name} は {maxLength} 文字までにしてください。");
        }

        foreach (var c in trimmed)
        {
            if (char.IsControl(c))
            {
                errors.Add($"{name} に制御文字は使えません。");
                break;
            }
        }
    }

    // 末尾の改行を受け付けないよう、$ ではなく \z を使う
    [GeneratedRegex(@"^[A-Za-z0-9-]{1,200}\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
