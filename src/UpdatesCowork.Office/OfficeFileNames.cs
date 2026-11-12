using System.Text;
using UpdatesCowork.Shared.Contracts;

namespace UpdatesCowork.Office;

/// <summary>成果物のファイル名を安全な形にそろえる。</summary>
public static class OfficeFileNames
{
    /// <summary>ファイル名に使える文字が残らなかったときの名前。</summary>
    public const string DefaultFileName = "azure-updates-cowork";

    private static readonly string[] KnownExtensions = ["." + ArtifactKinds.Docx, "." + ArtifactKinds.Pptx];

    // Windows で予約されているデバイス名。拡張子を付けても使えない。
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// 拡張子を除いたファイル名を英数字とハイフン(<c>[A-Za-z0-9-]</c>)だけにし、
    /// <see cref="OfficeLimits.MaxFileNameLength"/> 文字までに切り詰める(設計書5.3節)。
    /// 末尾の <c>.docx</c> と <c>.pptx</c> は取り除く。使える文字が残らないときは空文字列を返す。
    /// </summary>
    public static string Sanitize(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var name = RemoveKnownExtension(fileName.Trim());
        var builder = new StringBuilder(Math.Min(name.Length, OfficeLimits.MaxFileNameLength * 2));
        foreach (var c in name)
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '-')
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[builder.Length - 1] != '-')
            {
                // 使えない文字はハイフン1つにまとめる
                builder.Append('-');
            }
        }

        var result = TrimSeparators(builder.ToString());
        if (result.Length == 0)
        {
            return result;
        }

        if (ReservedNames.Contains(result))
        {
            result = "file-" + result;
        }

        if (result.Length > OfficeLimits.MaxFileNameLength)
        {
            result = TrimSeparators(result[..OfficeLimits.MaxFileNameLength]);
        }

        return result;
    }

    /// <summary>
    /// <see cref="Sanitize"/> した名前に拡張子を付ける。使える文字が残らないときは <see cref="DefaultFileName"/> を使う。
    /// </summary>
    /// <param name="fileName">モデルが指定したファイル名。</param>
    /// <param name="extension"><see cref="OfficeDocument.Extension"/> の値(<c>docx</c> か <c>.docx</c> の形)。</param>
    public static string WithExtension(string? fileName, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        var name = Sanitize(fileName);
        if (name.Length == 0)
        {
            name = DefaultFileName;
        }

        return string.Concat(name, ".", extension.TrimStart('.'));
    }

    internal static string RemoveKnownExtension(string fileName)
    {
        foreach (var extension in KnownExtensions)
        {
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return fileName[..^extension.Length];
            }
        }

        return fileName;
    }

    private static string TrimSeparators(string value) => value.Trim('-');
}
