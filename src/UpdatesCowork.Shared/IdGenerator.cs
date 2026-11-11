using System.Security.Cryptography;

namespace UpdatesCowork.Shared;

/// <summary>ULID を使った ID の生成(例: <c>art_01JCZ7A8B9C0D1E2F3G4H5J6K7</c>)。</summary>
public static class IdGenerator
{
    public const string ArtifactPrefix = "art_";
    public const string ThreadPrefix = "th_";

    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string NewArtifactId(TimeProvider? timeProvider = null) =>
        ArtifactPrefix + NewUlid(timeProvider ?? TimeProvider.System);

    public static string NewThreadId(TimeProvider? timeProvider = null) =>
        ThreadPrefix + NewUlid(timeProvider ?? TimeProvider.System);

    /// <summary>26文字の ULID (48ビットの時刻 + 80ビットの乱数)を返す。</summary>
    public static string NewUlid(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        Span<byte> bytes = stackalloc byte[16];
        var ms = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        for (var i = 5; i >= 0; i--)
        {
            bytes[i] = (byte)(ms & 0xFF);
            ms >>= 8;
        }

        RandomNumberGenerator.Fill(bytes[6..]);
        return EncodeCrockford(bytes);
    }

    /// <summary>接頭辞付きの ULID の形かを確かめる。</summary>
    public static bool IsValid(string? value, string prefix)
    {
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal) || value.Length != prefix.Length + 26)
        {
            return false;
        }

        foreach (var c in value.AsSpan(prefix.Length))
        {
            if (!Crockford.Contains(c, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string EncodeCrockford(ReadOnlySpan<byte> bytes)
    {
        // 128ビットを 5 ビットずつ 26 文字にする(先頭は 3 ビット)。
        var hi = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes);
        var lo = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]);
        var value = ((UInt128)hi << 64) | lo;
        Span<char> chars = stackalloc char[26];
        for (var i = 25; i >= 0; i--)
        {
            chars[i] = Crockford[(int)(value & 0x1F)];
            value >>= 5;
        }

        return new string(chars);
    }
}
