using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The three proving models against realistic wire payloads.
    /// </summary>
    public class ModelRoundTripTests
    {
        // A character as the API actually sends one: most fields explicit null, a couple set,
        // and another tool's extension field riding along.
        private const string CharacterJson = @"{
            ""id"": ""513daf48-3d89-4e78-8b5a-494e8df2a97c"",
            ""name"": ""Quillon"",
            ""type"": ""character"",
            ""world"": ""5378fde7-7f91-49e6-9b7e-f9ef45b72e97"",
            ""change_seq"": 42,
            ""supertype"": ""Rigger"",
            ""level"": 0,
            ""charisma"": null,
            ""CHA"": null,
            ""hit_points"": 7,
            ""species"": [],
            ""friends"": [""aaaaaaaa-0000-0000-0000-000000000001""],
            ""x_atlas_pinned"": true,
            ""x_magelet_note"": {""seen"": 3}
        }";

        [Test]
        public void Character_DistinguishesUnsetFromZero()
        {
            var c = OWJson.Deserialize<OWCharacter>(CharacterJson);

            Assert.IsTrue(c.Level.HasValue, "level:0 is a deliberate zero.");
            Assert.AreEqual(0, c.Level.Value);

            Assert.IsFalse(c.Charisma.HasValue, "charisma:null means the author never set it.");
            Assert.IsFalse(c.Cha.HasValue, "CHA:null likewise.");

            Assert.IsTrue(c.HitPoints.HasValue);
            Assert.AreEqual(7, c.HitPoints.Value);

            Assert.IsFalse(c.Height.HasValue, "A field absent from the payload lands as unset.");
        }

        [Test]
        public void Character_ReadsBaseAndServerFields()
        {
            var c = OWJson.Deserialize<OWCharacter>(CharacterJson);

            Assert.AreEqual("Quillon", c.Name);
            Assert.AreEqual("Rigger", c.Supertype);
            Assert.AreEqual("character", c.Type);
            Assert.AreEqual(42, c.ChangeSeq);
        }

        [Test]
        public void Character_LinksAreBareUuids()
        {
            var c = OWJson.Deserialize<OWCharacter>(CharacterJson);

            Assert.AreEqual(1, c.Friends.Count);
            Assert.AreEqual("aaaaaaaa-0000-0000-0000-000000000001", c.Friends[0]);
            Assert.AreEqual(0, c.Species.Count, "An empty multi-link is empty, not null.");
        }

        [Test]
        public void ExtensionFields_SurviveARoundTrip()
        {
            // The March corruption, in test form: deserialize, write back, and another tool's
            // x_* state must still be there. MissingMemberHandling.Ignore with nowhere for unknown
            // fields to go silently destroyed exactly this.
            var json = OWJson.Serialize(OWJson.Deserialize<OWCharacter>(CharacterJson));
            var back = JObject.Parse(json);

            Assert.IsTrue(back.ContainsKey("x_atlas_pinned"), "Another tool's extension field was dropped.");
            Assert.AreEqual(true, back["x_atlas_pinned"].Value<bool>());

            Assert.IsTrue(back.ContainsKey("x_magelet_note"), "A structured extension field was dropped.");
            Assert.AreEqual(3, back["x_magelet_note"]["seen"].Value<int>());
        }

        [Test]
        public void ExtensionFields_SurviveUnitySerialization()
        {
            // The OTHER half of the March corruption, and the one that shipped broken until
            // 2026-07-29: extensions survived the JSON path and the cache (raw strings), so every
            // existing test passed while Unity's own serializer silently dropped the bag. Nothing
            // called the flatten/rebuild hooks because OWElement did not implement
            // ISerializationCallbackReceiver. JsonUtility exercises the same path the Inspector,
            // a ScriptableObject field and a domain reload do.
            var original = OWJson.Deserialize<OWCharacter>(CharacterJson);

            var unityJson = UnityEngine.JsonUtility.ToJson(original);
            var revived = UnityEngine.JsonUtility.FromJson<OWCharacter>(unityJson);

            var extensions = revived.Extensions;
            Assert.AreEqual(2, extensions.Count,
                "Both extension fields must survive Unity's serializer, not just Newtonsoft's.");

            var pinned = extensions.First(e => e.Key == "x_atlas_pinned");
            Assert.AreEqual("true", pinned.RawJson);

            var note = extensions.First(e => e.Key == "x_magelet_note");
            StringAssert.Contains("\"seen\":3", note.RawJson,
                "A structured extension must keep its shape, not be flattened to a string.");

            // And the bag must still write back out through JSON afterwards -- a round trip
            // through Unity must not cost the merge-back the bag exists for.
            var backOut = JObject.Parse(OWJson.Serialize(revived));
            Assert.AreEqual(3, backOut["x_magelet_note"]["seen"].Value<int>());
        }

        [Test]
        public void Extensions_AreVisible_WithoutASerializationPass()
        {
            // Extensions used to read from the serialized mirror, which is empty until Unity
            // happens to write the object. An element deserialized from the wire and never
            // serialized reported zero extensions while carrying two -- a property that lied
            // in the most common case there is.
            var c = OWJson.Deserialize<OWCharacter>(CharacterJson);

            Assert.AreEqual(2, c.Extensions.Count,
                "Extensions must be readable straight after deserialization, with no Unity pass.");
        }

        [Test]
        public void UnsetFields_WriteBackAsExplicitNull()
        {
            var json = OWJson.Serialize(OWJson.Deserialize<OWCharacter>(CharacterJson));
            var back = JObject.Parse(json);

            Assert.AreEqual(JTokenType.Null, back["charisma"].Type,
                "Unset must write back as null, never 0 -- PATCH is destructive on sent fields.");
            Assert.AreEqual(0, back["level"].Value<int>(), "A deliberate zero stays 0.");
        }

        // -- Pin: the generic-link case ---------------------------------------

        [Test]
        public void Pin_ReadsTheGenericLinkPair()
        {
            var p = OWJson.Deserialize<OWPin>(
                @"{""name"":""P"",""map"":""m-1"",""element_type"":""character"",""element_id"":""c-1"",""x"":10,""y"":20}");

            Assert.AreEqual("character", p.ElementType);
            Assert.AreEqual("c-1", p.ElementId);
            Assert.IsTrue(p.HasTarget);
            Assert.AreEqual(10, p.X.Value);
            Assert.IsFalse(p.Z.HasValue, "z absent means unset, not 0.");
        }

        [Test]
        public void Pin_MaplessIsLegal()
        {
            // Wire-probed 2026-07-28: the API accepts this (201). Captain ruled mapless pins a
            // legitimate app pattern, so the model must not reject one.
            var p = OWJson.Deserialize<OWPin>(@"{""name"":""floating"",""map"":null,""x"":null,""y"":null}");

            Assert.AreEqual("floating", p.Name);
            Assert.IsNull(p.Map);
            Assert.IsFalse(p.X.HasValue);
            Assert.IsFalse(p.HasTarget);
        }

        // -- Marker: the ordering convention ----------------------------------

        [Test]
        public void Marker_OrderedBeforeUnordered()
        {
            var ordered = OWJson.Deserialize<OWMarker>(@"{""name"":""B"",""order"":1}");
            var unordered = OWJson.Deserialize<OWMarker>(@"{""name"":""A""}");

            Assert.Less(OWMarkerOrdering.Compare(ordered, unordered), 0,
                "An explicitly placed marker leads one that was never ordered.");
        }

        [Test]
        public void Marker_UnorderedFallsBackToCreatedAt()
        {
            var early = OWJson.Deserialize<OWMarker>(@"{""name"":""early"",""created_at"":""2026-01-01T00:00:00Z""}");
            var late = OWJson.Deserialize<OWMarker>(@"{""name"":""late"",""created_at"":""2026-06-01T00:00:00Z""}");

            Assert.Less(OWMarkerOrdering.Compare(early, late), 0,
                "Absent order sorts by created_at -- the ruled convention.");
        }

        [Test]
        public void Marker_OrderZeroIsFirstPoint_NotUnset()
        {
            var m = OWJson.Deserialize<OWMarker>(@"{""name"":""origin"",""order"":0}");

            Assert.IsTrue(m.Order.HasValue, "order:0 is the first point of a polygon, not 'no order'.");
            Assert.AreEqual(0, m.Order.Value);
        }
    }
}
