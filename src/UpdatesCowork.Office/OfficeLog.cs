using Microsoft.Extensions.Logging;

namespace UpdatesCowork.Office;

/// <summary>Office の生成で出すログ。文書の中身はログに入れない。</summary>
internal static partial class OfficeLog
{
    [LoggerMessage(EventId = 4101, Level = LogLevel.Warning,
        Message = "{SlideNumber} 枚目のスライドの {BulletNumber} 番目の箇条書きが {MaxLength} 文字を超えたため、切り詰めました。")]
    public static partial void BulletTruncated(ILogger logger, int slideNumber, int bulletNumber, int maxLength);

    [LoggerMessage(EventId = 4102, Level = LogLevel.Warning,
        Message = "{SlideNumber} 枚目のスライドの箇条書きが {MaxBullets} 項目を超えたため、{DroppedCount} 項目を省きました。")]
    public static partial void BulletsDropped(ILogger logger, int slideNumber, int maxBullets, int droppedCount);

    [LoggerMessage(EventId = 4103, Level = LogLevel.Warning,
        Message = "生成した {Kind} の検証で {ErrorCount} 件のエラーが見つかりました。")]
    public static partial void ValidationFailed(ILogger logger, string kind, int errorCount);
}
