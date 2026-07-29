using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    public class OWWorldKeyTests
    {
        [Test]
        public void SameWorldId_DifferentSource_AreDistinct()
        {
            // The bug this key exists to prevent, in its simplest form.
            var live = OWWorldKey.FromApi("w-1", "https://www.onlyworlds.com/api/v2");
            var snapshot = OWWorldKey.FromFolder("w-1", "C:/exports/ch01");

            Assert.AreNotEqual(live, snapshot);
            Assert.AreNotEqual(live.ToAssetKey(), snapshot.ToAssetKey());
        }

        [Test]
        public void SameWorldId_DifferentFolders_AreDistinct()
        {
            // Temper's demonstrated failure: two chapter snapshots sharing a world id collided in
            // a world-id-keyed registry, and the tool opened the wrong one with no warning.
            var ch01 = OWWorldKey.FromFolder("w-1", "C:/exports/probe-world");
            var ch12 = OWWorldKey.FromFolder("w-1", "C:/exports/probe-world-ch12");

            Assert.AreNotEqual(ch01, ch12, "Two snapshots of one world must not share a slot.");
            Assert.AreNotEqual(ch01.ToAssetKey(), ch12.ToAssetKey());
        }

        [Test]
        public void IdenticalKeys_AreEqual_AndHashTogether()
        {
            var a = OWWorldKey.FromApi("w-1", "https://x");
            var b = OWWorldKey.FromApi("w-1", "https://x");

            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());

            var set = new HashSet<OWWorldKey> { a, b };
            Assert.AreEqual(1, set.Count, "Equal keys must collapse in a set.");
        }

        [Test]
        public void AssetKey_IsFilesystemSafe()
        {
            var key = OWWorldKey.FromApi("w-1", "https://www.onlyworlds.com/api/v2");
            foreach (var c in key.ToAssetKey())
            {
                Assert.IsTrue(char.IsLetterOrDigit(c) || c == '-' || c == '_',
                    $"'{c}' is not safe in an asset name.");
            }
        }

        [Test]
        public void AssetKey_StaysWithinNameLimits()
        {
            var key = OWWorldKey.FromFolder("w-1", "C:/" + new string('a', 400) + "/deep/path/snapshot");
            Assert.Less(key.ToAssetKey().Length, 160, "A long origin must not blow past name limits.");
        }

        [Test]
        public void EmptyWorldId_IsInvalid()
        {
            Assert.IsFalse(new OWWorldKey(OWSourceKind.Api, "").IsValid);
            Assert.IsTrue(OWWorldKey.FromApi("w-1").IsValid);
        }
    }

    public class OWWorldCacheTests
    {
        private OWWorldCache _cache;

        [SetUp]
        public void SetUp()
        {
            _cache = ScriptableObject.CreateInstance<OWWorldCache>();
            _cache.Initialize(OWWorldKey.FromApi("w-1"), "Test World");
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_cache);

        private void Add(string id, string type, string name)
            => _cache.Upsert(id, type, $"{{\"id\":\"{id}\",\"type\":\"{type}\",\"name\":\"{name}\"}}");

        [Test]
        public void GetById_ReturnsTypedElement()
        {
            Add("c1", "character", "Quillon");

            var c = _cache.Get<OWCharacter>("c1");
            Assert.IsNotNull(c);
            Assert.AreEqual("Quillon", c.Name);
        }

        [Test]
        public void MissingId_ReturnsNull_NotAnException()
        {
            Assert.IsNull(_cache.Get<OWCharacter>("nope"));
            Assert.IsNull(_cache.Get<OWCharacter>(null));
            Assert.IsFalse(_cache.Contains("nope"));
        }

        [Test]
        public void Upsert_ReplacesRatherThanDuplicates()
        {
            Add("c1", "character", "First");
            Add("c1", "character", "Second");

            Assert.AreEqual(1, _cache.Count);
            Assert.AreEqual("Second", _cache.Get<OWCharacter>("c1").Name);
        }

        [Test]
        public void Upsert_MovesAnElementWhenItsTypeChanges()
        {
            // Latent until the folder reader: a file sitting in elements/character/ whose body
            // declares type "location" is an explicit case in the conformance fixture, and the
            // body wins. Replacing in place without touching the type index leaves the element
            // listed under the old type forever -- All(new) misses it, All(old) returns it, and
            // deserializing it as the old model yields the wrong object or an error.
            Add("x1", "character", "Was a character");
            Assert.AreEqual(1, _cache.All<OWCharacter>("character").Count);

            Add("x1", "location", "Now a location");

            Assert.AreEqual(1, _cache.Count, "It is still one element, not two.");
            Assert.AreEqual(0, _cache.AllRaw("character").Count,
                "The old type bucket must let go of it.");
            Assert.AreEqual(1, _cache.AllRaw("location").Count,
                "The new type bucket must pick it up.");
        }

        [Test]
        public void All_FiltersByType()
        {
            Add("c1", "character", "A");
            Add("c2", "character", "B");
            Add("p1", "pin", "P");

            Assert.AreEqual(2, _cache.All<OWCharacter>("character").Count);
            Assert.AreEqual(1, _cache.All<OWPin>("pin").Count);
            Assert.AreEqual(0, _cache.All<OWElement>("location").Count);
        }

        [Test]
        public void Resolve_SkipsDanglingLinks()
        {
            Add("c1", "character", "A");
            Add("c2", "character", "B");

            var resolved = _cache.Resolve<OWCharacter>(new[] { "c1", "missing", "c2" });

            Assert.AreEqual(2, resolved.Count, "A dangling link is normal, not an error.");
        }

        [Test]
        public void Remove_RebuildsTheIndexCorrectly()
        {
            Add("c1", "character", "A");
            Add("c2", "character", "B");
            Add("c3", "character", "C");

            Assert.IsTrue(_cache.Remove("c2"));

            // The real risk: indices after the removed slot shift, so a patched index would
            // silently return the wrong element rather than fail.
            Assert.AreEqual(2, _cache.Count);
            Assert.IsFalse(_cache.Contains("c2"));
            Assert.AreEqual("A", _cache.Get<OWCharacter>("c1").Name);
            Assert.AreEqual("C", _cache.Get<OWCharacter>("c3").Name, "Index shifted after removal.");
            Assert.AreEqual(2, _cache.All<OWCharacter>("character").Count);
        }

        [Test]
        public void ExtensionFields_SurviveCaching()
        {
            // Raw-JSON storage means nothing is re-emitted through a model that might not know a
            // field -- extensions survive by construction rather than by care.
            _cache.Upsert("c1", "character",
                @"{""id"":""c1"",""name"":""K"",""x_atlas_pinned"":true}");

            var c = _cache.Get<OWCharacter>("c1");
            var json = OWJson.Serialize(c);
            StringAssert.Contains("x_atlas_pinned", json);
        }

        [Test]
        public void NullableState_SurvivesCaching()
        {
            _cache.Upsert("c1", "character", @"{""id"":""c1"",""name"":""K"",""level"":0,""charisma"":null}");

            var c = _cache.Get<OWCharacter>("c1");
            Assert.IsTrue(c.Level.HasValue, "A deliberate zero survived.");
            Assert.IsFalse(c.Charisma.HasValue, "An unset field stayed unset.");
        }

        [Test]
        public void IndexRebuilds_AfterDeserialization()
        {
            Add("c1", "character", "A");

            // Simulates the domain reload: Unity restores the serialized list and drops the
            // runtime index, which must rebuild on next access rather than return nothing.
            _cache.OnAfterDeserialize();

            Assert.IsTrue(_cache.Contains("c1"));
            Assert.AreEqual("A", _cache.Get<OWCharacter>("c1").Name);
        }

        [Test]
        public void ReadOnlyFlag_IsCarried()
        {
            var snapshot = ScriptableObject.CreateInstance<OWWorldCache>();
            snapshot.Initialize(OWWorldKey.FromFolder("w-1", "C:/ch01"), "Chapter 1", readOnly: true);

            Assert.IsTrue(snapshot.ReadOnly, "A frozen snapshot must say so -- consumers MUST honour it.");
            Object.DestroyImmediate(snapshot);
        }

        [Test]
        public void AllRaw_ReturnsWireBodies_IncludingUnmodeledFields()
        {
            // The browser's detail panel renders raw bodies precisely so extension fields stay
            // visible -- typed access would silently hide the fields most worth seeing.
            _cache.Upsert("c1", "character", @"{""id"":""c1"",""name"":""K"",""x_atlas_pinned"":true}");
            Add("c2", "character", "B");
            Add("p1", "pin", "P");

            var raw = _cache.AllRaw("character");

            Assert.AreEqual(2, raw.Count);
            Assert.IsTrue(raw.Exists(r => r.Contains("x_atlas_pinned")));
            Assert.AreEqual(0, _cache.AllRaw("location").Count);
        }

        [Test]
        public void Cursor_RoundTrips()
        {
            _cache.SetCursor(202, "2026-07-28T18:00:00Z");
            Assert.AreEqual(202, _cache.Cursor);
            Assert.AreEqual("2026-07-28T18:00:00Z", _cache.SyncedAt);
        }
    }
}
