using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpdatesCowork.AzureUpdates;

/// <summary>データ API の JSON を読むためのソース生成コンテキスト。</summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AzureUpdate))]
[JsonSerializable(typeof(AzureUpdateListResponse))]
internal sealed partial class AzureUpdatesJsonContext : JsonSerializerContext;
