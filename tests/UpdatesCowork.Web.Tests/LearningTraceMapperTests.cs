using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using UpdatesCowork.Shared;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Web.AgentGateway;
using UpdatesCowork.Web.Chat;
using Xunit;

namespace UpdatesCowork.Web.Tests;

public sealed class LearningTraceMapperTests
{
    private static readonly TraceSpanEvent Span = new("11111111111111111111111111111111", "2222222222222222",
        null, "model.call", DateTimeOffset.Parse("2026-11-14T01:02:03Z", System.Globalization.CultureInfo.InvariantCulture),
        "running", Model: "test-model");

    [Fact]
    public void StandardProgressCarrierMapsTraceButOrdinaryProgressRemainsIgnored()
    {
        var mapper = Create();
        using var standard = JsonDocument.Parse("""{"type":"response.in_progress","response":{"id":"resp_test"}}""");
        Assert.Empty(mapper.Map(new AgentStreamEvent(AgentTraceEnvelope.CarrierEventType, standard.RootElement)));
        var carrier = JsonNode.Parse(standard.RootElement.GetRawText())!;
        carrier[AgentTraceEnvelope.MetadataProperty] = JsonNode.Parse(Envelope(Span));
        using var extended = JsonDocument.Parse(carrier.ToJsonString());
        Assert.Equal(Span, Assert.Single(mapper.Map(new AgentStreamEvent(AgentTraceEnvelope.CarrierEventType, extended.RootElement))));
    }

    [Fact]
    public void ValidMeasuredLifecycleSurvivesMappingAndUnknownFieldsAreNotProjected()
    {
        var mapper = Create();
        var json = JsonSerializer.SerializeToNode(new AgentTraceEnvelope(AgentTraceEnvelope.EventType, 1, "resp_test", Span),
            AucJsonContext.Default.AgentTraceEnvelope)!;
        json["span"]!["privatePrompt"] = "private content";
        var started = Assert.IsType<TraceSpanEvent>(Assert.Single(Map(mapper, json.ToJsonString())));
        Assert.Equal(Span, started);
        var completed = Span with { Status = "completed", EndedAt = Span.StartedAt.AddMilliseconds(25), DurationMs = 25, InputTokens = 12 };
        Assert.Equal(completed, Assert.Single(Map(mapper, Envelope(completed))));
        Assert.DoesNotContain("private", JsonSerializer.Serialize<ChatEvent>(started, AucJsonContext.Default.ChatEvent), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("response")]
    [InlineData("operation")]
    [InlineData("time")]
    [InlineData("negative-usage")]
    [InlineData("unsafe-name")]
    [InlineData("other-trace")]
    [InlineData("source")]
    [InlineData("malformed")]
    public void InvalidTraceIsReportedWithoutBreakingTheAnswer(string invalid)
    {
        var mapper = Create();
        Assert.Single(Map(mapper, Envelope(Span)));
        var bad = invalid switch
        {
            "response" => Envelope(Span, "resp_other"),
            "operation" => Envelope(Span with { Name = "private.prompt" }),
            "time" => Envelope(Span with { Status = "completed", EndedAt = Span.StartedAt.AddSeconds(1), DurationMs = 5 }),
            "negative-usage" => Envelope(Span with { InputTokens = -1 }),
            "unsafe-name" => Envelope(Span with { Model = "private@example.test" }),
            "other-trace" => Envelope(Span with { SpanId = "3333333333333333", TraceId = "44444444444444444444444444444444" }),
            "source" => Envelope(Span with { Source = "web" }),
            _ => """{"type":"response.auc.trace","span":{"durationMs":"invalid"}}""",
        };
        Assert.Equal("invalid", Assert.IsType<TraceStatusEvent>(Assert.Single(Map(mapper, bad))).Status);
        Assert.Empty(Map(mapper, Envelope(Span)));
        using var answer = JsonDocument.Parse("""{"delta":"通常の回答"}""");
        Assert.IsType<TextDeltaEvent>(Assert.Single(mapper.Map(new AgentStreamEvent("response.output_text.delta", answer.RootElement))));
    }

    [Fact]
    public void TerminalSpanCannotBeReopened()
    {
        var mapper = Create();
        Assert.Single(Map(mapper, Envelope(Span)));
        Assert.Single(Map(mapper, Envelope(Span with { Status = "completed", EndedAt = Span.StartedAt, DurationMs = 0 })));
        Assert.Equal("invalid", Assert.IsType<TraceStatusEvent>(Assert.Single(Map(mapper, Envelope(Span)))).Status);
    }

    [Fact]
    public void AgentCannotForgeWebCollectionStatus()
    {
        var mapper = Create();
        var json = JsonSerializer.Serialize(new AgentTraceEnvelope(AgentTraceEnvelope.EventType, 1, "resp_test",
            Status: new TraceStatusEvent("complete", 0, "web")), AucJsonContext.Default.AgentTraceEnvelope);
        Assert.Equal(new TraceStatusEvent("invalid", 0), Assert.Single(Map(mapper, json)));
    }

    [Fact]
    public void AgentWithoutSourceFieldDefaultsToAgent()
    {
        var mapper = Create();
        var json = JsonNode.Parse(Envelope(Span))!;
        json["span"]!.AsObject().Remove("source");
        Assert.Equal(Span, Assert.Single(Map(mapper, json.ToJsonString())));
    }

    [Fact]
    public void CompletionAndLimitNoticesAreExplicit()
    {
        var mapper = Create();
        foreach (var status in new[] { new TraceStatusEvent("limited", 2) })
        {
            var json = JsonSerializer.Serialize(new AgentTraceEnvelope(AgentTraceEnvelope.EventType, 1, "resp_test", Status: status),
                AucJsonContext.Default.AgentTraceEnvelope);
            Assert.Equal(status, Assert.Single(Map(mapper, json)));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompleteNoticeCannotHideMissingEndings(bool withRunningSpan)
    {
        var mapper = Create();
        if (withRunningSpan) Assert.Single(Map(mapper, Envelope(Span)));
        var json = JsonSerializer.Serialize(new AgentTraceEnvelope(AgentTraceEnvelope.EventType, 2, "resp_test",
            Status: new TraceStatusEvent("complete", 0)), AucJsonContext.Default.AgentTraceEnvelope);
        Assert.Equal("invalid", Assert.IsType<TraceStatusEvent>(Assert.Single(Map(mapper, json))).Status);
    }

    private static ChatEventMapper Create()
    {
        var mapper = new ChatEventMapper("th_test", null, null, NullLogger.Instance);
        using var created = JsonDocument.Parse("""{"response":{"id":"resp_test"}}""");
        mapper.Map(new AgentStreamEvent("response.created", created.RootElement));
        var status = JsonSerializer.Serialize(new AgentTraceEnvelope(AgentTraceEnvelope.EventType, 0, "resp_test",
            Status: new TraceStatusEvent("started", 0)), AucJsonContext.Default.AgentTraceEnvelope);
        Assert.IsType<TraceStatusEvent>(Assert.Single(Map(mapper, status)));
        return mapper;
    }
    private static string Envelope(TraceSpanEvent span, string responseId = "resp_test") =>
        JsonSerializer.Serialize(new AgentTraceEnvelope(AgentTraceEnvelope.EventType, 1, responseId, span), AucJsonContext.Default.AgentTraceEnvelope);
    private static IReadOnlyList<ChatEvent> Map(ChatEventMapper mapper, string json)
    {
        using var document = JsonDocument.Parse(json);
        return mapper.Map(new AgentStreamEvent(AgentTraceEnvelope.EventType, document.RootElement));
    }
}
