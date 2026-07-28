using System;
using UnityEngine;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// A nullable value type that Unity can serialize.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unity's serializer cannot handle <see cref="System.Nullable{T}"/>. The OnlyWorlds schema has
    /// 70 integer fields that are nullable on the wire, and the API sends explicit <c>null</c> --
    /// not omission. So three states are distinct and all reachable:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>null</c>  -- the author never set this</description></item>
    ///   <item><description><c>0</c>     -- the author deliberately set zero</description></item>
    ///   <item><description>absent       -- the field was not in the payload at all</description></item>
    /// </list>
    /// <para>
    /// Collapsing null to 0 silently overwrites "the author never set this", which is a different
    /// claim from "zero". A level-0 character and a level-unknown character are not the same
    /// character. That data loss is invisible at write time and unrecoverable afterwards.
    /// </para>
    /// <para>
    /// Reading requires acknowledging the maybe: there is an implicit conversion FROM <typeparamref name="T"/>
    /// but deliberately none back TO it. Use <see cref="HasValue"/>, <see cref="GetValueOrDefault()"/>,
    /// or <see cref="TryGetValue"/>.
    /// </para>
    /// <para>
    /// Schema note: nullability is NOT declared per-field in the OnlyWorlds YAML. Only <c>name</c> is
    /// required; every other field is optional and nullable on the wire. This is a convention the
    /// shared schema walk carries in its ruling table, not something the emitter derives.
    /// </para>
    /// </remarks>
    [Serializable]
    public struct SerializableNullable<T> : IEquatable<SerializableNullable<T>>
        where T : struct
    {
        [SerializeField] private T _value;
        [SerializeField] private bool _hasValue;

        /// <summary>True when a value is set. False means "unset", never "zero".</summary>
        public bool HasValue => _hasValue;

        /// <summary>
        /// The value. Throws when unset -- use <see cref="GetValueOrDefault()"/> or
        /// <see cref="TryGetValue"/> when unset is a legitimate case.
        /// </summary>
        /// <exception cref="InvalidOperationException">The value is unset.</exception>
        public T Value
        {
            get
            {
                if (!_hasValue)
                {
                    throw new InvalidOperationException(
                        $"SerializableNullable<{typeof(T).Name}> has no value. " +
                        "Check HasValue, or use GetValueOrDefault()/TryGetValue().");
                }

                return _value;
            }
        }

        public SerializableNullable(T value)
        {
            _value = value;
            _hasValue = true;
        }

        /// <summary>An explicitly unset value. Distinct from <c>default(T)</c>.</summary>
        public static SerializableNullable<T> Unset => default;

        public T GetValueOrDefault() => _hasValue ? _value : default;

        public T GetValueOrDefault(T fallback) => _hasValue ? _value : fallback;

        public bool TryGetValue(out T value)
        {
            value = _value;
            return _hasValue;
        }

        /// <summary>
        /// Implicit FROM T only. There is deliberately no implicit conversion back to
        /// <typeparamref name="T"/> -- that would let unset silently read as zero, which is the
        /// exact data loss this type exists to prevent.
        /// </summary>
        public static implicit operator SerializableNullable<T>(T value) => new SerializableNullable<T>(value);

        public bool Equals(SerializableNullable<T> other)
        {
            if (!_hasValue) return !other._hasValue;
            return other._hasValue && _value.Equals(other._value);
        }

        public override bool Equals(object obj) => obj is SerializableNullable<T> other && Equals(other);

        public override int GetHashCode() => _hasValue ? _value.GetHashCode() : 0;

        public static bool operator ==(SerializableNullable<T> a, SerializableNullable<T> b) => a.Equals(b);

        public static bool operator !=(SerializableNullable<T> a, SerializableNullable<T> b) => !a.Equals(b);

        public override string ToString() => _hasValue ? _value.ToString() : "unset";
    }
}
