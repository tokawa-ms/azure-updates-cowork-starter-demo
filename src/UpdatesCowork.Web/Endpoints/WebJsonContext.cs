using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpdatesCowork.Web.Endpoints;

/// <summary>SSE 以外の API のエラー応答。<c>message</c> には利用者向けの短い文だけを入れる。</summary>
internal sealed record ApiError(string Code, string Message);

/// <summary><c>GET /api/me</c> の応答(Entra ID モードの表示用)。</summary>
internal sealed record MeResponse(string AuthMode, bool SignedIn, string? DisplayName);

/// <summary>web だけで使う JSON のソース生成。</summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ApiError))]
[JsonSerializable(typeof(MeResponse))]
[JsonSerializable(typeof(EndThreadRequest))]
[JsonSerializable(typeof(UpdateListResponse))]
[JsonSerializable(typeof(UpdateDetail))]
internal sealed partial class WebJsonContext : JsonSerializerContext;
