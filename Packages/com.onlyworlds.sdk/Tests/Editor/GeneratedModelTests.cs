using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The generated models, as a population rather than one at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hand-written proving models had their own tests and those still stand -- what changed
    /// is that there are now 22 types instead of 3, and a defect in the EMITTER lands in all of
    /// them at once. So these tests assert over every type in the registry rather than over a
    /// chosen few: a per-type test suite that covers three types would have found none of the
    /// three real bugs this class exists to catch.
    /// </para>
    /// <para>
    /// The rule they encode: <b>every claim is checked against the schema-derived registry, never
    /// against a list retyped into the test.</b> A test carrying its own copy of the 22 type names
    /// agrees with the emitter on the day it is written and drifts silently afterwards.
    /// </para>
    /// </remarks>
    public class GeneratedModelTests
    {
        // -- The registry -----------------------------------------------------

        [Test]
        public void Registry_HoldsTwentyTwoTypes()
        {
            Assert.AreEqual(22, OWElementTypes.Slugs.Length,
                "The standard has 22 element types. A different count means the emitter read a "
                + "schema this package was not pinned to.");
        }

        [Test]
        public void Registry_ResolvesEverySlugToAModelType()
        {
            foreach (var slug in OWElementTypes.Slugs)
            {
                var type = OWElementTypes.TypeFor(slug);
                Assert.IsNotNull(type, $"'{slug}' resolves to no C# type.");
                Assert.IsTrue(typeof(OWElement).IsAssignableFrom(type),
                    $"'{slug}' resolves to {type.Name}, which does not derive from OWElement.");
            }
        }

        [Test]
        public void Registry_RoundTripsSlugAndTypeInBothDirections()
        {
            foreach (var slug in OWElementTypes.Slugs)
            {
                var type = OWElementTypes.TypeFor(slug);
                Assert.AreEqual(slug, OWElementTypes.SlugFor(type),
                    $"slug -> type -> slug lost its way for '{slug}'.");
            }
        }

        [Test]
        public void Registry_MatchesTheSyncTypeList()
        {
            // OWSync carries its own array of type slugs and drives the baseline walk with it.
            // Two lists of the same 22 names in one package is exactly the drift the registry
            // exists to end -- so assert they agree TODAY, and the failure message says which
            // one to delete rather than which one to edit.
            CollectionAssert.AreEquivalent(OWSync.ElementTypes, OWElementTypes.Slugs,
                "OWSync.ElementTypes and the generated registry disagree. The generated one is "
                + "derived from the pinned schema; the hand-maintained one should reference it.");
        }

        [Test]
        public void Registry_UnknownSlugReturnsNullRatherThanThrowing()
        {
            // A world written by a NEWER standard is a thing a reader should report and skip,
            // not crash on.
            Assert.IsNull(OWElementTypes.TypeFor("archipelago"));
            Assert.IsNull(OWElementTypes.TypeFor(""));
            Assert.IsNull(OWElementTypes.TypeFor(null));
            Assert.IsFalse(OWElementTypes.IsKnown("archipelago"));
        }

        [Test]
        public void Registry_SlugLookupIsCaseInsensitive()
        {
            Assert.AreEqual(typeof(OWCharacter), OWElementTypes.TypeFor("CHARACTER"));
        }

        // -- Emitter conventions, across every type ---------------------------

        [Test]
        public void EveryModel_CarriesOptInSerialization()
        {
            // The bug this catches shipped once already and survived a day of green tests plus a
            // real 1,087-element world: Newtonsoft's default is OptOut, so without this attribute
            // a model sends every field to the wire TWICE ("name" AND "Name"). The server ignores
            // the PascalCase copies, which is precisely why nothing failed. And Newtonsoft does
            // not INHERIT [JsonObject], so it has to be on each of the 22 generated classes --
            // one missing line, one silently duplicated type.
            foreach (var slug in OWElementTypes.Slugs)
            {
                var type = OWElementTypes.TypeFor(slug);
                var attr = type.GetCustomAttribute<JsonObjectAttribute>(inherit: false);

                Assert.IsNotNull(attr, $"{type.Name} has no [JsonObject] of its own.");
                Assert.AreEqual(MemberSerialization.OptIn, attr.MemberSerialization,
                    $"{type.Name} is not OptIn, so its public properties will duplicate every "
                    + "field on the wire.");
            }
        }

        [Test]
        public void EveryModel_IsUnitySerializable()
        {
            foreach (var slug in OWElementTypes.Slugs)
            {
                var type = OWElementTypes.TypeFor(slug);
                Assert.IsNotNull(type.GetCustomAttribute<SerializableAttribute>(inherit: false),
                    $"{type.Name} lacks [Serializable]; Unity cannot serialize it into a cache asset.");
            }
        }

        [Test]
        public void EveryModel_DeclaresEveryFieldTheRegistrySaysItHas()
        {
            // FieldNames is generated beside the models from the same walk, so this compares two
            // emissions of one source. It catches an emitter that writes the doc table and the
            // class from different data -- which is how a model ends up missing a field that
            // every other artifact says it has.
            foreach (var slug in OWElementTypes.Slugs)
            {
                var type = OWElementTypes.TypeFor(slug);
                var declared = JsonPropertyNames(type);

                foreach (var expected in OWElementTypes.FieldNames[slug])
                {
                    Assert.Contains(expected, declared,
                        $"{type.Name} declares no [JsonProperty(\"{expected}\")], so that field "
                        + "cannot reach the wire in either direction.");
                }
            }
        }

        [Test]
        public void EveryModel_HasNoDuplicateJsonPropertyNames()
        {
            foreach (var slug in OWElementTypes.Slugs)
            {
                var type = OWElementTypes.TypeFor(slug);
                var names = JsonPropertyNames(type);
                var duplicates = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key);

                CollectionAssert.IsEmpty(duplicates,
                    $"{type.Name} maps two members onto one JSON key.");
            }
        }

        [Test]
        public void EveryModel_ShadowsNoBaseField()
        {
            // A generated field named `name` or `type` would hide the base one and split the
            // element's identity across two members that disagree.
            foreach (var slug in OWElementTypes.Slugs)
            {
                var type = OWElementTypes.TypeFor(slug);
                var own = JsonPropertyNames(type);

                foreach (var baseName in OWElementTypes.BaseFieldNames)
                {
                    Assert.IsFalse(own.Contains(baseName),
                        $"{type.Name} redeclares the base field '{baseName}'.");
                }
            }
        }

        [Test]
        public void EveryIntegerField_IsNullable_NeverBareInt()
        {
            // rulings.yaml: nullable-by-default. A level-0 character and a level-unknown character
            // are different claims, and a bare int cannot hold the difference -- it reports 0 for
            // both. 70 scalar ints ride on this across the standard.
            foreach (var slug in OWElementTypes.Slugs)
            {
                var type = OWElementTypes.TypeFor(slug);

                foreach (var field in SerializedFields(type))
                {
                    Assert.AreNotEqual(typeof(int), field.FieldType,
                        $"{type.Name}.{field.Name} is a bare int. Unset would collapse to 0.");
                    Assert.AreNotEqual(typeof(int?), field.FieldType,
                        $"{type.Name}.{field.Name} is int?, which Unity cannot serialize. "
                        + "SerializableNullable<int> is the ruled representation.");
                }
            }
        }

        [Test]
        public void EveryMultiLink_IsInitialized_SoAnEmptyLinkIsEmptyNotNull()
        {
            foreach (var slug in OWElementTypes.Slugs)
            {
                var type = OWElementTypes.TypeFor(slug);
                var instance = (OWElement)Activator.CreateInstance(type);

                foreach (var field in SerializedFields(type)
                             .Where(f => f.FieldType == typeof(List<string>)))
                {
                    Assert.IsNotNull(field.GetValue(instance),
                        $"{type.Name}.{field.Name} is null on a fresh instance. Callers must be "
                        + "able to Add to a link list without null-checking first.");
                }
            }
        }

        // -- Round trips, every type ------------------------------------------

        [Test]
        public void EveryType_RoundTripsASampleWithoutLosingAKnownField()
        {
            // Built from the registry, so the sample covers exactly what the schema says the type
            // has -- including fields nobody thought to test. Each value is chosen to be a
            // distinguishable claim: 0 for ints (the number a null would be mistaken for), a
            // non-empty string, a one-element link list.
            foreach (var slug in OWElementTypes.Slugs)
            {
                var type = OWElementTypes.TypeFor(slug);
                var sample = SampleFor(slug, type);

                var element = (OWElement)JsonConvert.DeserializeObject(
                    sample.ToString(), type, OWJson.Settings);
                var back = JObject.Parse(OWJson.Serialize(element));

                foreach (var property in sample.Properties())
                {
                    Assert.IsNotNull(back[property.Name],
                        $"{type.Name} lost '{property.Name}' in a round trip.");
                    Assert.AreEqual(property.Value.ToString(Formatting.None),
                        back[property.Name].ToString(Formatting.None),
                        $"{type.Name} changed the value of '{property.Name}' in a round trip.");
                }
            }
        }

        [Test]
        public void EveryType_KeepsUnsetDistinctFromZero()
        {
            // The whole reason SerializableNullable exists, asserted across all 22 rather than on
            // the one type that happened to get a hand-written test.
            foreach (var slug in OWElementTypes.Slugs)
            {
                var type = OWElementTypes.TypeFor(slug);
                var intFields = SerializedFields(type)
                    .Where(f => f.FieldType == typeof(SerializableNullable<int>))
                    .ToList();
                if (intFields.Count == 0) continue;

                var name = JsonNameOf(intFields[0]);

                var unset = JsonConvert.DeserializeObject(
                    $@"{{""name"":""u"",""{name}"":null}}", type, OWJson.Settings);
                var zero = JsonConvert.DeserializeObject(
                    $@"{{""name"":""z"",""{name}"":0}}", type, OWJson.Settings);

                var unsetValue = (SerializableNullable<int>)intFields[0].GetValue(unset);
                var zeroValue = (SerializableNullable<int>)intFields[0].GetValue(zero);

                Assert.IsFalse(unsetValue.HasValue, $"{type.Name}.{name}: null must mean unset.");
                Assert.IsTrue(zeroValue.HasValue, $"{type.Name}.{name}: 0 is a deliberate value.");
                Assert.AreEqual(0, zeroValue.Value);

                // And unset must write back as explicit null, never as 0 and never omitted --
                // PATCH is destructive on the fields it carries.
                var back = JObject.Parse(OWJson.Serialize(unset));
                Assert.AreEqual(JTokenType.Null, back[name].Type,
                    $"{type.Name}.{name}: unset wrote back as something other than null.");
            }
        }

        [Test]
        public void EveryType_PreservesAnotherToolsExtensionField()
        {
            // rulings.yaml: extension-passthrough. Inherited from OWElement, but "inherited"
            // is a claim about 22 classes and this is the test that makes it one.
            foreach (var slug in OWElementTypes.Slugs)
            {
                var type = OWElementTypes.TypeFor(slug);
                var element = (OWElement)JsonConvert.DeserializeObject(
                    @"{""name"":""x"",""x_atlas_pinned"":true,""x_magelet_note"":{""seen"":3}}",
                    type, OWJson.Settings);

                var back = JObject.Parse(OWJson.Serialize(element));
                Assert.IsTrue(back.ContainsKey("x_atlas_pinned"),
                    $"{type.Name} dropped another tool's extension field.");
                Assert.AreEqual(3, back["x_magelet_note"]["seen"].Value<int>(),
                    $"{type.Name} flattened a structured extension field.");
            }
        }

        [Test]
        public void EveryType_SendsEachFieldExactlyOnce()
        {
            // The OptOut duplication bug, caught at the wire rather than at the attribute: no
            // serialized key may look like a C# property name. Asserting on ABSENT keys, which
            // is the asymmetry that let this ship the first time -- every round-trip test checks
            // that expected keys are present and none checked that unexpected ones are not.
            foreach (var slug in OWElementTypes.Slugs)
            {
                var type = OWElementTypes.TypeFor(slug);
                var element = (OWElement)JsonConvert.DeserializeObject(
                    @"{""name"":""x""}", type, OWJson.Settings);
                var json = JObject.Parse(OWJson.Serialize(element));

                foreach (var property in json.Properties())
                {
                    // The schema has genuinely all-caps field names (STR, DEX, CON, INT, WIS,
                    // CHA), so the test is "PascalCase", not "not uppercase". A blanket casing
                    // rule asserts something false about the wire.
                    Assert.IsFalse(
                        property.Name.Length > 1
                        && char.IsUpper(property.Name[0])
                        && char.IsLower(property.Name[1]),
                        $"{type.Name} put '{property.Name}' on the wire -- a C# property name "
                        + "leaking beside its snake_case field.");
                }
            }
        }

        [Test]
        public void EveryType_SurvivesUnitySerializationWithItsExtensions()
        {
            // JsonUtility exercises the same path the Inspector, a ScriptableObject field and a
            // domain reload use. Extensions survived the Newtonsoft path and the cache while
            // Unity's own serializer silently dropped them, and every test passed throughout.
            foreach (var slug in OWElementTypes.Slugs)
            {
                var type = OWElementTypes.TypeFor(slug);
                var element = (OWElement)JsonConvert.DeserializeObject(
                    @"{""name"":""x"",""x_probe"":7}", type, OWJson.Settings);

                var unityJson = UnityEngine.JsonUtility.ToJson(element);
                var revived = (OWElement)UnityEngine.JsonUtility.FromJson(unityJson, type);

                Assert.AreEqual(1, revived.Extensions.Count,
                    $"{type.Name} lost its extension bag through Unity's serializer.");
                Assert.AreEqual("x_probe", revived.Extensions[0].Key);
            }
        }

        // -- The generic link -------------------------------------------------

        [Test]
        public void Pin_IsTheOnlyGenericLink_AndResolvesThroughTheRegistry()
        {
            var pin = OWJson.Deserialize<OWPin>(
                @"{""name"":""P"",""element_type"":""character"",""element_id"":""c-1""}");

            Assert.IsTrue(pin.HasTarget);
            Assert.AreEqual(typeof(OWCharacter), OWElementTypes.TypeFor(pin.ElementType),
                "The generic link's whole point is that the slug becomes a type.");

            // There is no field named `element` anywhere -- the wire carries the pair.
            CollectionAssert.DoesNotContain(OWElementTypes.FieldNames["pin"], "element");
            CollectionAssert.Contains(OWElementTypes.FieldNames["pin"], "element_type");
            CollectionAssert.Contains(OWElementTypes.FieldNames["pin"], "element_id");
        }

        // -- Helpers ----------------------------------------------------------

        /// <summary>Serialized backing fields declared by this type, base fields excluded.</summary>
        private static IEnumerable<FieldInfo> SerializedFields(Type type) =>
            type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => f.GetCustomAttribute<JsonPropertyAttribute>() != null);

        private static string JsonNameOf(FieldInfo field) =>
            field.GetCustomAttribute<JsonPropertyAttribute>().PropertyName;

        private static List<string> JsonPropertyNames(Type type) =>
            SerializedFields(type).Select(JsonNameOf).ToList();

        /// <summary>
        /// A sample body for a type, built from the registry rather than typed out.
        /// </summary>
        private static JObject SampleFor(string slug, Type type)
        {
            var sample = new JObject { ["name"] = $"sample {slug}" };

            foreach (var field in SerializedFields(type))
            {
                var name = JsonNameOf(field);
                if (field.FieldType == typeof(SerializableNullable<int>))
                {
                    // 0, deliberately: the value a null-collapsing model is indistinguishable from.
                    sample[name] = 0;
                }
                else if (field.FieldType == typeof(List<string>))
                {
                    sample[name] = new JArray($"{slug}-link-1");
                }
                else
                {
                    sample[name] = $"{name}-value";
                }
            }

            return sample;
        }
    }
}
