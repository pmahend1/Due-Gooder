using System.Text.Json;

namespace DueGooder.Connectors.Banner9;

/// <summary>Reads optional properties, treating an absent property and JSON <c>null</c> alike.</summary>
internal static class JsonElementReading
{
    #region Methods

    public static string? OptionalString(this JsonElement json, string property) =>
        json.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    public static int? OptionalInt(this JsonElement json, string property) =>
        json.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    public static decimal? OptionalDecimal(this JsonElement json, string property) =>
        json.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : null;

    public static bool? OptionalBool(this JsonElement json, string property) =>
        json.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;

    public static IEnumerable<JsonElement> OptionalArray(this JsonElement json, string property) =>
        json.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    #endregion Methods
}
