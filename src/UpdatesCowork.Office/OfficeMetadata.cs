using DocumentFormat.OpenXml.Packaging;

namespace UpdatesCowork.Office;

/// <summary>Word と PowerPoint に共通の文書プロパティ。</summary>
internal static class OfficeMetadata
{
    internal const string Creator = "Azure Updates Cowork";
    internal const string JapaneseLanguage = "ja-JP";

    /// <summary>タイトル、作成者、作成日時などを設定する。テンプレートに残っている説明は消す。</summary>
    public static void Apply(OpenXmlPackage package, string title, string? subject, string language, DateTimeOffset now)
    {
        var properties = package.PackageProperties;
        var timestamp = now.UtcDateTime;
        properties.Title = title;
        properties.Subject = string.IsNullOrEmpty(subject) ? null : subject;
        properties.Creator = Creator;
        properties.LastModifiedBy = Creator;
        properties.Created = timestamp;
        properties.Modified = timestamp;
        properties.Description = null;
        properties.Language = language;
        properties.Revision = "1";
    }
}
