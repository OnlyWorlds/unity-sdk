using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// A JSON text nested past 200, the text's top value at 1, objects and arrays both counted, is one refusal with one
    /// name, <c>too_deep</c> (the folder format's OW-25): a folder or a <c>.owworld</c> skips that file and serves the
    /// rest, a world.json is refused by that name. A file whose deepest object or array sits at 200 is read whole, a
    /// plain value inside it included. The TypeScript SDK's folder reader skips such a file by the same word.
    /// </summary>
    public class OWTooDeepTests
    {
        private const string WorldId = "0195c3a1-0000-7000-8000-00000000aaaa";
        private const string IdA = "0195c3a1-7b2e-7c4d-9e5f-00000000000a";
        private const string IdB = "0195c3a1-7b2e-7c4d-9e5f-00000000000b";
        private const string IdC = "0195c3a1-7b2e-7c4d-9e5f-00000000000c";

        private string _root;
        private readonly List<OWWorldCache> _caches = new List<OWWorldCache>();

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ow-deep-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var cache in _caches) UnityEngine.Object.DestroyImmediate(cache);
            _caches.Clear();
            if (_root != null && Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        // -- Fixtures ---------------------------------------------------------------------------------------------

        /// <summary>
        /// A value whose deepest object or array sits at <paramref name="deepest"/> when it is a field of the file's top
        /// object (at 1): an array and an object in turn, a plain value inside the last, as the set's OW-25 cases nest.
        /// </summary>
        private static string Nest(int deepest, bool arraysOnly = false)
        {
            var containers = deepest - 1;
            var open = new StringBuilder();
            var close = new StringBuilder();
            for (var k = 0; k < containers; k++)
            {
                var array = arraysOnly || k % 2 == 0;
                open.Append(array ? "[" : "{\"k\":");
                close.Insert(0, array ? "]" : "}");
            }

            return open + "1" + close;
        }

        private static string FileText(string id, string name, string deep = null)
            => "{\n  \"id\": \"" + id + "\",\n  \"type\": \"character\",\n  \"name\": \"" + name + "\""
               + (deep == null ? "" : ",\n  \"x_depth\": " + deep) + "\n}\n";

        /// <summary>A world folder of three characters, the middle one holding <paramref name="deep"/>.</summary>
        private string Folder(string tag, string deep, string worldDeep = null)
        {
            var dir = Path.Combine(_root, tag);
            var characters = Path.Combine(dir, "elements", "character");
            Directory.CreateDirectory(characters);
            File.WriteAllText(Path.Combine(dir, "world.json"),
                "{\n  \"id\": \"" + WorldId + "\",\n  \"name\": \"Deep\"" + (worldDeep == null ? "" : ",\n  \"x_depth\": " + worldDeep) + "\n}\n",
                new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(characters, "a--0000000a.json"), FileText(IdA, "A"), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(characters, "b--0000000b.json"), FileText(IdB, "B", deep), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(characters, "c--0000000c.json"), FileText(IdC, "C"), new UTF8Encoding(false));
            return dir;
        }

        private static string[] Ids(OWFolderReadResult read) => read.Elements.Select(e => e.Id).ToArray();

        /// <summary>The deepest object or array in <paramref name="token"/>, itself at 1.</summary>
        private static int Deepest(JToken token)
        {
            var deepest = 0;
            var stack = new Stack<(JToken, int)>();
            stack.Push((token, 1));
            while (stack.Count > 0)
            {
                var (t, d) = stack.Pop();
                if (!(t is JContainer c)) continue;
                if (t is JProperty p)
                {
                    stack.Push((p.Value, d));
                    continue;
                }

                deepest = Math.Max(deepest, d);
                foreach (var child in c.Children()) stack.Push((child, d + 1));
            }

            return deepest;
        }

        // -- The line -----------------------------------------------------------------------------------------------

        /// <summary>The deepest bracket in a text with no bracket inside a string: a count independent of every parser.</summary>
        private static int DeepestInText(string text)
        {
            int depth = 0, deepest = 0;
            foreach (var ch in text)
            {
                if (ch == '{' || ch == '[') deepest = Math.Max(deepest, ++depth);
                else if (ch == '}' || ch == ']') depth--;
            }

            return deepest;
        }

        [Test]
        public void TheFixture_NestsWhereItSays()
        {
            Assert.AreEqual(200, DeepestInText(FileText(IdB, "B", Nest(200))));
            Assert.AreEqual(201, DeepestInText(FileText(IdB, "B", Nest(201))));
            Assert.AreEqual(100, DeepestInText(Nest(201).Replace("[", "").Replace("]", "")), "half the nest is arrays");
        }

        [Test]
        public void AText_At200_IsRead_AndAtTwoHundredAndOne_IsRefusedByName()
        {
            var at200 = OWJson.ParseObject(FileText(IdB, "B", Nest(200)));
            Assert.AreEqual(200, Deepest(at200));

            var e = Assert.Throws<OWTooDeepException>(() => OWJson.ParseObject(FileText(IdB, "B", Nest(201))));
            Assert.AreEqual(OWJson.TooDeep, e.Reason);
            StringAssert.Contains(OWJson.TooDeep, e.Message);
            Assert.IsInstanceOf<JsonReaderException>(e, "a caller that catches a JsonException today still catches it");
        }

        [Test]
        public void ArraysCount_AsObjectsDo()
        {
            Assert.DoesNotThrow(() => OWJson.ParseObject(FileText(IdB, "B", Nest(200, arraysOnly: true))));
            Assert.Throws<OWTooDeepException>(() => OWJson.ParseObject(FileText(IdB, "B", Nest(201, arraysOnly: true))));
        }

        [Test]
        public void TheTypedReaders_Hold200()
        {
            var text = FileText(IdB, "B", Nest(200));
            Assert.DoesNotThrow(() => OWJson.Deserialize<JObject>(text));
            Assert.DoesNotThrow(() => OWJson.ParseToken(text));
            Assert.Throws<OWTooDeepException>(() => OWJson.Deserialize<JObject>(FileText(IdB, "B", Nest(201))));
        }

        [Test]
        public void AFaultBeforeTheDepth_IsNotThisRefusal()
        {
            var broken = "{\n  \"id\": ,\n  \"x_depth\": " + Nest(300) + "\n}\n";
            var e = Assert.Catch<JsonReaderException>(() => OWJson.ParseObject(broken));
            Assert.IsNotInstanceOf<OWTooDeepException>(e);
        }

        // -- The doors ------------------------------------------------------------------------------------------------

        [Test]
        public void Folder_ReadsTheFileAt200_Whole()
        {
            var read = OWFolderReader.Read(Folder("f200", Nest(200)));
            CollectionAssert.AreEqual(new[] { IdA, IdB, IdC }, Ids(read));
            Assert.AreEqual(0, read.Skipped.Count);
            Assert.AreEqual(200, Deepest(read.Elements.Single(x => x.Id == IdB).Body));
        }

        [Test]
        public void Folder_SkipsTheFileAt201_ByName_AndServesTheRest()
        {
            var read = OWFolderReader.Read(Folder("f201", Nest(201)));
            CollectionAssert.AreEqual(new[] { IdA, IdC }, Ids(read));
            Assert.AreEqual("b--0000000b.json", Path.GetFileName(read.Skipped.Single().Path));
            StringAssert.StartsWith(OWJson.TooDeep, read.Skipped.Single().Reason);
            StringAssert.DoesNotContain("unparseable", read.Skipped.Single().Reason);
        }

        [Test]
        public void Folder_IntoACache_HoldsTheFileAt200()
        {
            var cache = ScriptableObject.CreateInstance<OWWorldCache>();
            cache.Initialize(OWWorldKey.FromApi("w-1"), "Deep");
            _caches.Add(cache);
            var read = OWFolderLoader.LoadInto(cache, Folder("load", Nest(200)));
            Assert.AreEqual(3, cache.Count);
            Assert.AreEqual(0, read.Skipped.Count);
        }

        [Test]
        public void WorldJson_PastTheDepth_RefusesTheWorld_ByName()
        {
            var e = Assert.Throws<OWFolderFormatException>(() => OWFolderReader.Read(Folder("w", null, Nest(201))));
            StringAssert.Contains(OWJson.TooDeep, e.Message);
            Assert.IsInstanceOf<OWTooDeepException>(e.InnerException);
        }

        [Test]
        public void Archive_SkipsTheFileAt201_ByName_AndServesTheRest()
        {
            var archive = Path.Combine(_root, "a.owworld");
            OWWorldArchive.Pack(Folder("a", Nest(201)), archive);
            var read = OWWorldArchive.Read(archive);
            CollectionAssert.AreEqual(new[] { IdA, IdC }, Ids(read));
            Assert.AreEqual("elements/character/b--0000000b.json", read.Skipped.Single().Path);
            StringAssert.StartsWith(OWJson.TooDeep, read.Skipped.Single().Reason);
        }
    }
}
