using System.Globalization;
using System.Text;
using System.Xml;

namespace UpdatesCowork.Office;

/// <summary>Office に入れる文字列の後始末。</summary>
internal static class OfficeText
{
    internal const string Ellipsis = "…";

    /// <summary>
    /// XML で使えない文字と制御文字(タブと改行以外)を取り除く。改行は LF にそろえる。
    /// 対になっていないサロゲートも取り除く。
    /// </summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var first = IndexOfCharToFix(value);
        if (first < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        builder.Append(value, 0, first);
        for (var i = first; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\r')
            {
                if (i + 1 < value.Length && value[i + 1] == '\n')
                {
                    continue;
                }

                builder.Append('\n');
                continue;
            }

            if (c is '\n' or '\t')
            {
                builder.Append(c);
                continue;
            }

            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    builder.Append(c).Append(value[i + 1]);
                    i++;
                }

                continue;
            }

            if (char.IsLowSurrogate(c) || char.IsControl(c) || !XmlConvert.IsXmlChar(c))
            {
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary><see cref="Clean"/> のあと、改行とタブを空白にして1行にする。</summary>
    public static string SingleLine(string? value) =>
        Clean(value).Replace('\n', ' ').Replace('\t', ' ').Trim();

    /// <summary>見た目の1文字(書記素)の数。</summary>
    public static int TextElementLength(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        return new StringInfo(value).LengthInTextElements;
    }

    /// <summary>
    /// 見た目の1文字(書記素)で <paramref name="maxLength"/> 文字を超えるとき、末尾を「…」にして切り詰める。
    /// サロゲートペアや結合文字の途中では切らない。
    /// </summary>
    public static string Truncate(string value, int maxLength, out bool truncated)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);
        if (value.Length <= maxLength)
        {
            truncated = false;
            return value;
        }

        var info = new StringInfo(value);
        if (info.LengthInTextElements <= maxLength)
        {
            truncated = false;
            return value;
        }

        truncated = true;
        return string.Concat(info.SubstringByTextElements(0, maxLength - 1), Ellipsis);
    }

    private static int IndexOfCharToFix(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is '\n' or '\t')
            {
                continue;
            }

            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                    continue;
                }

                return i;
            }

            if (c == '\r' || char.IsLowSurrogate(c) || char.IsControl(c) || !XmlConvert.IsXmlChar(c))
            {
                return i;
            }
        }

        return -1;
    }
}
