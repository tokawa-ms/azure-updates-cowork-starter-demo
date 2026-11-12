using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpdatesCowork.AzureUpdates;

/// <summary>文字列でも数値でも文字列として読む。データ API の形が少し変わっても一覧全体が読めなくならないようにする。</summary>
internal sealed class LenientStringConverter : JsonConverter<string?>
{
    public override bool HandleNull => true;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var number)
                    ? number.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString(CultureInfo.InvariantCulture);
            case JsonTokenType.Null:
                return null;
            default:
                // 想定外の形は読み飛ばして null にする
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}
