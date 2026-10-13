using System.Security.Cryptography;
using System.Text.Json;

namespace Aigenterra.Infrastructure.Executions.Postgres;

public static class RuntimeCanonicalJson
{
    public static string Serialize<T>(T value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(value));
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) Write(document.RootElement, writer);
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
    public static string Hash(string canonicalJson) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonicalJson)));
    private static void Write(JsonElement value, Utf8JsonWriter writer)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Name); Write(property.Value, writer); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Write(item, writer); writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }
}
