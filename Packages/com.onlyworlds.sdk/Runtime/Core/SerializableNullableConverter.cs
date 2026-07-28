using System;
using Newtonsoft.Json;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// Maps <see cref="SerializableNullable{T}"/> to and from the wire's three-state integer fields.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read: JSON <c>null</c> -> unset. A number -> set. A missing property never calls the
    /// converter at all, so the field keeps its default, which is unset. All three wire states land
    /// correctly without special-casing.
    /// </para>
    /// <para>
    /// Write: unset emits explicit <c>null</c>, NEVER omission and NEVER 0. This is the half of the
    /// ruling that matters. PATCH on this API is destructive on the fields it receives, so emitting
    /// 0 for an unset field would overwrite "the author never set this" with a deliberate zero --
    /// silently, on every save, for any field the user never touched.
    /// </para>
    /// <para>
    /// Which fields get sent at all is the dirty-tracker's job, not this converter's. This type
    /// answers only "given that we are sending this field, what does unset look like on the wire":
    /// explicit null.
    /// </para>
    /// </remarks>
    public sealed class SerializableNullableConverter<T> : JsonConverter<SerializableNullable<T>>
        where T : struct
    {
        public override void WriteJson(JsonWriter writer, SerializableNullable<T> value, JsonSerializer serializer)
        {
            if (value.HasValue)
            {
                serializer.Serialize(writer, value.Value);
            }
            else
            {
                // Explicit null. Never omission, never default(T).
                writer.WriteNull();
            }
        }

        public override SerializableNullable<T> ReadJson(
            JsonReader reader,
            Type objectType,
            SerializableNullable<T> existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return SerializableNullable<T>.Unset;
            }

            // Reject silent coercion. Newtonsoft will happily turn 2.7 into 3, true into 1, and
            // "0" into 0 -- each with HasValue=true, each indistinguishable afterwards from a
            // value the author actually wrote. A type built to stop null becoming zero has no
            // business letting a float quietly become a different integer.
            //
            // The schema's nullable scalars are integers, so a non-integer token here means the
            // wire changed or the model is wrong. Both are worth a loud failure.
            if (typeof(T) == typeof(int) && reader.TokenType != JsonToken.Integer)
            {
                throw new JsonSerializationException(
                    $"Expected an integer or null for {typeof(T).Name}, got {reader.TokenType} " +
                    $"({reader.Value}). Refusing to coerce -- a coerced value is indistinguishable " +
                    "from an authored one.");
            }

            var value = serializer.Deserialize<T>(reader);
            return new SerializableNullable<T>(value);
        }
    }
}
