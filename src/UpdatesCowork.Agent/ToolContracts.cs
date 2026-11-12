using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpdatesCowork.Agent;

internal sealed record ToolError(bool Ok, string Error);
internal sealed record PlanResult(bool Ok, int Count);
internal sealed record SafetyResult(bool Scanned, bool AttackDetected, bool Blocked);
internal sealed record UpdateResult(
    bool Found, string Id, string? Title = null, string? Status = null,
    IReadOnlyList<string>? Products = null, IReadOnlyList<string>? ProductCategories = null,
    IReadOnlyList<string>? Tags = null, IReadOnlyList<AvailabilityResult>? Availabilities = null,
    string? GeneralAvailabilityDate = null, string? PreviewAvailabilityDate = null,
    DateTimeOffset? Created = null, DateTimeOffset? Modified = null,
    string? Url = null, string? Content = null, SafetyResult? Safety = null, string? UserNotice = null);
internal sealed record AvailabilityResult(string Ring, int? Year, string Month);
internal sealed record SearchItem(string Id, string Title, string? Status,
    IReadOnlyList<string>? Products, IReadOnlyList<string>? Tags, DateTimeOffset? Modified, string Url);
internal sealed record SearchResult(IReadOnlyList<SearchItem> Items, int Scanned);
internal sealed record ShieldRequest(string UserPrompt, IReadOnlyList<string> Documents);
internal sealed record ShieldAnalysis
{
    public required bool AttackDetected { get; init; }
}
internal sealed record ShieldResponse
{
    public required IReadOnlyList<ShieldAnalysis> DocumentsAnalysis { get; init; }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ToolError))]
[JsonSerializable(typeof(PlanResult))]
[JsonSerializable(typeof(UpdateResult))]
[JsonSerializable(typeof(SearchResult))]
[JsonSerializable(typeof(ShieldRequest))]
[JsonSerializable(typeof(ShieldResponse))]
internal sealed partial class AgentJsonContext : JsonSerializerContext;
