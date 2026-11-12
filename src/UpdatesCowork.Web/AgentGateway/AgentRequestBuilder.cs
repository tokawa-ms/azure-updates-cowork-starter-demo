using System.Buffers;
using System.Text.Json;
using UpdatesCowork.Shared;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Web.Chat;

namespace UpdatesCowork.Web.AgentGateway;

/// <summary>Responses の要求の本文を組み立てる(6.4節)。</summary>
internal static class AgentRequestBuilder
{
    /// <summary>検証済みのページ情報から developer メッセージの本文を作る。値がなければ <c>null</c>。</summary>
    public static string? BuildPageContextText(ValidatedPageContext? page) =>
        PageContextText.Format(page is null ? null : new PageContext(page.UpstreamUrl, page.UpdateId, page.Title, page.Locale));

    /// <summary><c>POST /responses</c> の本文(JSON)を作る。</summary>
    public static byte[] BuildResponsesBody(AgentTurnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            if (!string.IsNullOrEmpty(request.ConversationId))
            {
                writer.WriteString("conversation", request.ConversationId);
            }
            else if (!string.IsNullOrEmpty(request.PreviousResponseId))
            {
                writer.WriteString("previous_response_id", request.PreviousResponseId);
            }

            writer.WriteBoolean("stream", true);
            writer.WriteStartArray("input");
            if (request.Approval is { } approval)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "mcp_approval_response");
                writer.WriteString("approval_request_id", approval.ApprovalRequestId);
                writer.WriteBoolean("approve", approval.Approve);
                writer.WriteEndObject();
            }
            else
            {
                if (!string.IsNullOrEmpty(request.DeveloperText))
                {
                    WriteMessage(writer, "developer", request.DeveloperText);
                }

                if (!string.IsNullOrEmpty(request.UserText))
                {
                    WriteMessage(writer, "user", request.UserText);
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteMessage(Utf8JsonWriter writer, string role, string text)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "message");
        writer.WriteString("role", role);
        writer.WriteStartArray("content");
        writer.WriteStartObject();
        writer.WriteString("type", "input_text");
        writer.WriteString("text", text);
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
