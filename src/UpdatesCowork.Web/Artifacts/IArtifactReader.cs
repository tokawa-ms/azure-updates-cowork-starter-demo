using System.Text.RegularExpressions;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Web.Registry;

namespace UpdatesCowork.Web.Artifacts;

/// <summary>台帳に登録した成果物の中身を読み出す。</summary>
internal interface IArtifactReader
{
    /// <summary>成果物を開く。見つからない、または保存先の形が正しくないときは <c>null</c>。</summary>
    Task<Stream?> OpenReadAsync(ArtifactRecord artifact, CancellationToken ct);
}

/// <summary>保存先ごとの <c>location</c> の形(6.6節)。台帳への登録前と読み出し前の両方で確かめる。</summary>
internal static partial class ArtifactLocationValidator
{
    public static bool IsValid(string? storage, string? location)
    {
        if (string.IsNullOrEmpty(location))
        {
            return false;
        }

        return storage switch
        {
            ArtifactStorages.Local => LocalLocation().IsMatch(location) || DatedLocalLocation().IsMatch(location),
            ArtifactStorages.Session => SessionLocation().IsMatch(location),
            _ => false,
        };
    }

    /// <summary>ファイル名に <c>..</c> だけの名前などが来ても通さないよう、先頭の点も禁じる。</summary>
    [GeneratedRegex(@"^art_[0-9A-HJKMNP-TV-Z]{26}/(?!\.)[A-Za-z0-9._-]{1,120}\z", RegexOptions.CultureInvariant)]
    private static partial Regex LocalLocation();

    [GeneratedRegex(@"^\d{4}/\d{2}/\d{2}/art_[0-9A-HJKMNP-TV-Z]{26}/(?!\.)[A-Za-z0-9._-]{1,120}\z", RegexOptions.CultureInvariant)]
    private static partial Regex DatedLocalLocation();

    [GeneratedRegex(@"^artifacts/art_[0-9A-HJKMNP-TV-Z]{26}/(?!\.)[A-Za-z0-9._-]{1,120}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SessionLocation();
}

/// <summary>成果物の保存先が読み出しに対応していないときに使う(常に見つからない扱い)。</summary>
internal sealed class UnsupportedArtifactReader : IArtifactReader
{
    public Task<Stream?> OpenReadAsync(ArtifactRecord artifact, CancellationToken ct) => Task.FromResult<Stream?>(null);
}
