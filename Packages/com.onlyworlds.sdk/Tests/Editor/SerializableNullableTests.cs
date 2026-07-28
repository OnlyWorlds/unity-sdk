using System;
using NUnit.Framework;
using UnityEngine;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The three-state contract: unset, zero, and value are distinct and survive a round trip.
    /// </summary>
    public class SerializableNullableTests
    {
        [Test]
        public void Default_IsUnset()
        {
            var n = default(SerializableNullable<int>);
            Assert.IsFalse(n.HasValue);
            Assert.AreEqual(0, n.GetValueOrDefault());
        }

        [Test]
        public void Zero_IsSet_AndDistinctFromUnset()
        {
            var zero = new SerializableNullable<int>(0);
            var unset = SerializableNullable<int>.Unset;

            Assert.IsTrue(zero.HasValue, "0 must be a set value, not unset.");
            Assert.IsFalse(unset.HasValue);
            Assert.AreNotEqual(zero, unset, "A level-0 character is not a level-unknown character.");
        }

        [Test]
        public void Value_Throws_WhenUnset()
        {
            var n = SerializableNullable<int>.Unset;
            Assert.Throws<InvalidOperationException>(() => { _ = n.Value; });
        }

        [Test]
        public void ImplicitConversion_FromT_Sets()
        {
            SerializableNullable<int> n = 42;
            Assert.IsTrue(n.HasValue);
            Assert.AreEqual(42, n.Value);
        }

        [Test]
        public void TryGetValue_ReportsUnset()
        {
            Assert.IsFalse(SerializableNullable<int>.Unset.TryGetValue(out _));
            Assert.IsTrue(new SerializableNullable<int>(7).TryGetValue(out var v));
            Assert.AreEqual(7, v);
        }

        // --- Wire contract: the half that matters ---

        [Serializable]
        private class Probe
        {
            public SerializableNullable<int> level;
        }

        [Test]
        public void Unset_SerializesAs_ExplicitNull_NeverZero_NeverOmitted()
        {
            var json = OWJson.Serialize(new Probe { level = SerializableNullable<int>.Unset });
            var compact = json.Replace(" ", "").Replace("\n", "").Replace("\r", "");

            // Parse rather than substring-match. The earlier version asserted Contains("level"),
            // which was strictly implied by the null assertion above it -- three assertions that
            // collapsed to one, and all of which passed on a wrong struct-shaped payload.
            var parsed = Newtonsoft.Json.Linq.JObject.Parse(json);

            Assert.IsTrue(parsed.ContainsKey("level"),
                "Unset must never be omitted -- PATCH is destructive on the fields it receives.");
            Assert.AreEqual(
                Newtonsoft.Json.Linq.JTokenType.Null,
                parsed["level"].Type,
                "Unset must serialize as explicit JSON null, not as a nested object or 0.");
            Assert.IsFalse(compact.Contains("\"hasValue\""),
                "The struct's internals must not leak onto the wire.");
        }

        [Test]
        public void DefaultValueHandling_CannotOmitUnsetFields()
        {
            // Guards the knob that NullValueHandling does not cover. DefaultValueHandling.Ignore
            // would omit the field entirely -- the converter never runs -- and omission on a
            // destructive PATCH silently drops the user's deliberate clear.
            var json = OWJson.Serialize(new Probe { level = SerializableNullable<int>.Unset });
            Assert.IsTrue(Newtonsoft.Json.Linq.JObject.Parse(json).ContainsKey("level"));
        }

        [Test]
        public void Reader_RefusesToCoerce_NonIntegerTokens()
        {
            // A coerced value is indistinguishable from an authored one. 2.7 must not become 3.
            foreach (var body in new[] { "{\"level\":2.7}", "{\"level\":true}", "{\"level\":\"0\"}" })
            {
                Assert.Throws<Newtonsoft.Json.JsonSerializationException>(
                    () => OWJson.Deserialize<Probe>(body),
                    $"Silent coercion accepted for {body}.");
            }
        }

        [Test]
        public void Settings_AreNotSharedMutableState()
        {
            // A shared static with a mutable Converters collection lets any consumer insert a
            // converter at index 0 and win, turning unset into {"level":0} process-wide.
            Assert.AreNotSame(OWJson.Settings, OWJson.Settings);
        }

        [Test]
        public void Zero_SerializesAs_Zero()
        {
            var json = OWJson.Serialize(new Probe { level = new SerializableNullable<int>(0) });
            StringAssert.Contains("\"level\":0", json.Replace(" ", ""));
        }

        [Test]
        public void ExplicitNull_Deserializes_AsUnset()
        {
            var p = OWJson.Deserialize<Probe>("{\"level\":null}");
            Assert.IsFalse(p.level.HasValue);
        }

        [Test]
        public void Zero_Deserializes_AsSetZero()
        {
            var p = OWJson.Deserialize<Probe>("{\"level\":0}");
            Assert.IsTrue(p.level.HasValue, "Wire 0 is a deliberate zero, not unset.");
            Assert.AreEqual(0, p.level.Value);
        }

        [Test]
        public void MissingProperty_Deserializes_AsUnset()
        {
            var p = OWJson.Deserialize<Probe>("{}");
            Assert.IsFalse(p.level.HasValue, "Absent is the third state and lands as unset.");
        }

        [Test]
        public void RoundTrip_PreservesAllThreeStates()
        {
            foreach (var original in new[]
                     {
                         SerializableNullable<int>.Unset,
                         new SerializableNullable<int>(0),
                         new SerializableNullable<int>(99),
                     })
            {
                var back = OWJson.Deserialize<Probe>(OWJson.Serialize(new Probe { level = original })).level;
                Assert.AreEqual(original, back, $"Round trip lost state for {original}.");
            }
        }

        // --- Unity serialization: the reason this type exists at all ---

        [Test]
        public void UnitySerialization_ActuallySerializesTheStruct()
        {
            // POSITIVE DIRECTION FIRST. Asserting only that unset survives is vacuous: unset IS
            // default(T), so that assertion passes identically whether Unity serializes the struct
            // or ignores it completely. Proving a SET value round-trips is what proves the
            // serializer sees the type at all -- the load-bearing assumption under this whole design.
            var so = ScriptableObject.CreateInstance<ProbeAsset>();
            so.level = new SerializableNullable<int>(99);

            var json = JsonUtility.ToJson(so);
            StringAssert.Contains("99", json, "Unity did not serialize the generic struct's payload.");

            var restored = ScriptableObject.CreateInstance<ProbeAsset>();
            JsonUtility.FromJsonOverwrite(json, restored);

            Assert.IsTrue(restored.level.HasValue, "Set state lost through Unity serialization.");
            Assert.AreEqual(99, restored.level.Value);

            UnityEngine.Object.DestroyImmediate(so);
            UnityEngine.Object.DestroyImmediate(restored);
        }

        [Test]
        public void UnitySerialization_PreservesUnset_AndDistinguishesItFromZero()
        {
            var unsetSo = ScriptableObject.CreateInstance<ProbeAsset>();
            unsetSo.level = SerializableNullable<int>.Unset;

            var zeroSo = ScriptableObject.CreateInstance<ProbeAsset>();
            zeroSo.level = new SerializableNullable<int>(0);

            var unsetBack = ScriptableObject.CreateInstance<ProbeAsset>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(unsetSo), unsetBack);

            var zeroBack = ScriptableObject.CreateInstance<ProbeAsset>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(zeroSo), zeroBack);

            Assert.IsFalse(unsetBack.level.HasValue, "Unity serialization collapsed unset.");
            Assert.IsTrue(zeroBack.level.HasValue, "Unity serialization collapsed a deliberate zero.");
            Assert.AreNotEqual(unsetBack.level, zeroBack.level,
                "The two states must remain distinct across an asset round trip.");

            foreach (var o in new[] { unsetSo, zeroSo, unsetBack, zeroBack })
            {
                UnityEngine.Object.DestroyImmediate(o);
            }
        }

        // Public, not private: Unity's serializer has requirements about type accessibility, and a
        // nested private ScriptableObject is exactly the kind of thing that would fail quietly and
        // make the test above pass for the wrong reason.
        public class ProbeAsset : ScriptableObject
        {
            public SerializableNullable<int> level;
        }
    }
}
