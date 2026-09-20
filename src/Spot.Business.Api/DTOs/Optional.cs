using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Distinguishes "this field was absent from the request body" (<see cref="IsSet"/> is false)
/// from "this field was present, possibly with an explicit null value" — a plain nullable
/// property can't tell those apart, but PATCH /business/categories/{categoryId} needs to:
/// omitting a nullable field must leave it untouched, while sending it explicitly as
/// <c>null</c> must clear it (e.g. promoting a subcategory to root via <c>parentCategoryId: null</c>).
/// Same pattern as Spot.Auth.Api's Optional{T} (kept local rather than moved to Spot.Shared,
/// since a cross-service move needs its own flagging — see CLAUDE.md).
/// </summary>
[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly struct Optional<T>
{
    public bool IsSet { get; }
    public T? Value { get; }

    private Optional(bool isSet, T? value)
    {
        IsSet = isSet;
        Value = value;
    }

    /// <summary>The struct's default value — what a property has when the JSON never touched it.</summary>
    public static readonly Optional<T> Unset = new(false, default);

    public static Optional<T> Of(T? value) => new(true, value);
}

/// <summary>
/// System.Text.Json only invokes a converter's <c>Read</c> for a property actually present in
/// the payload. An absent property is left at the field's default, which for <see cref="Optional{T}"/>
/// (a struct) is exactly <see cref="Optional{T}.Unset"/> — that default is what makes the
/// "not present" signal work, with no extra bookkeeping needed here.
/// </summary>
public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(OptionalConverter<>).MakeGenericType(valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class OptionalConverter<T> : JsonConverter<Optional<T>>
    {
        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Optional<T>.Of(JsonSerializer.Deserialize<T>(ref reader, options));

        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Value, options);
    }
}
