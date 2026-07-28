using System;
using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// Resolves the right closed <see cref="SerializableNullableConverter{T}"/> for any
    /// <see cref="SerializableNullable{T}"/> encountered during (de)serialization.
    /// </summary>
    /// <remarks>
    /// Newtonsoft does not match an open generic converter to a closed generic type on its own, so
    /// registering <c>SerializableNullableConverter&lt;int&gt;</c> would only cover ints. This
    /// factory closes the converter over whatever T shows up. Register this once, in
    /// <see cref="OWJson"/>.
    /// </remarks>
    public sealed class SerializableNullableConverterFactory : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType.IsGenericType
                   && objectType.GetGenericTypeDefinition() == typeof(SerializableNullable<>);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            Resolve(value.GetType()).WriteJson(writer, value, serializer);
        }

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            return Resolve(objectType).ReadJson(reader, objectType, existingValue, serializer);
        }

        private static readonly ConcurrentDictionary<Type, JsonConverter> Cache =
            new ConcurrentDictionary<Type, JsonConverter>();

        private static JsonConverter Resolve(Type nullableType)
        {
            return Cache.GetOrAdd(nullableType, static t =>
            {
                var valueType = t.GetGenericArguments()[0];
                var closed = typeof(SerializableNullableConverter<>).MakeGenericType(valueType);
                return (JsonConverter)Activator.CreateInstance(closed);
            });
        }

        // AOT note (UNVALIDATED against a real IL2CPP build): MakeGenericType over a value type can
        // be stripped on AOT platforms if nothing statically references the closed form. The schema
        // only needs int today, and OWJson.AotHint() holds a static reference for exactly that
        // reason. Validate on a real IL2CPP target before shipping to mobile/WebGL.
    }
}
