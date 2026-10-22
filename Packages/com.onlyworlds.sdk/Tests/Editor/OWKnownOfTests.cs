using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// Known-of stubs (folder format v0.3.10 §4.4; keel PR 113): written as they are, read as known of, never
    /// pushed or edited, and whole again once they arrive whole.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A port, not a design. The disk and read cases are the onlyworlds Python package's
    /// <c>tests/test_known_of.py</c> (0.10.4, which declares 0.3.12), on its <c>stub_dev</c> and <c>whole_dev</c>
    /// bodies byte for byte; the expected file texts are what its <c>known_of_for_disk</c> and <c>encode</c>
    /// wrote for them (run 2026-10-21). The client cases are the npm SDK's <c>test/v2-client.test.mjs</c> (4.42.3),
    /// its known_of block.
    /// </para>
    /// </remarks>
    public class OWKnownOfTests
    {
        private const string Dev = "c0000000-0000-7000-8000-00000000000d";
        private const string Mara = "c0000000-0000-7000-8000-00000000000a";
        private const string Species = "s0000000-0000-7000-8000-000000000001";
        private const string World = @"{""id"":""w0000000-0000-7000-8000-000000000000"",""name"":""W""}";

        private static readonly string[] Kept =
        {
            "id", "type", "name", "description", "supertype", "subtype", "image_url", "created_at", "updated_at", "change_seq",
        };

        // package: whole_dev() and stub_dev(), json.dumps in their own key order.
        private const string WholeDevJson =
            @"{""id"":""c0000000-0000-7000-8000-00000000000d"",""type"":""character"",""name"":""Dev"",""description"":""Dev desc"","
            + @"""supertype"":""Ferryman"",""subtype"":"""",""image_url"":null,""physicality"":""zzphys"",""location"":""l1"","
            + @"""friends"":[""c0000000-0000-7000-8000-00000000000a""],""STR"":14,""created_at"":""2026-10-20T00:00:00Z"","
            + @"""updated_at"":""2026-10-20T01:00:00Z"",""change_seq"":9,""created_by"":""m-1"",""changed_by"":""m-1"","
            + @"""changed_via"":""api"",""x_secret"":""zzext""}";

        /// <summary>Dev as keel serves him to a player who knows of him: the kept keys, every other field unset.</summary>
        private const string StubDevJson =
            @"{""id"":""c0000000-0000-7000-8000-00000000000d"",""type"":""character"",""name"":""Dev"",""description"":""Dev desc"","
            + @"""supertype"":""Ferryman"",""subtype"":"""",""image_url"":null,""physicality"":"""",""location"":null,""friends"":[],"
            + @"""STR"":null,""created_at"":""2026-10-20T00:00:00Z"",""updated_at"":""2026-10-20T01:00:00Z"",""change_seq"":9,"
            + @"""created_by"":null,""changed_by"":null,""changed_via"":null,""known_of"":true}";

        private const string MaraJson =
            @"{""id"":""c0000000-0000-7000-8000-00000000000a"",""type"":""character"",""name"":""Mara"",""description"":"""","
            + @"""friends"":[""c0000000-0000-7000-8000-00000000000d""]}";

        /// <summary>The package's <c>encode(known_of_for_disk(stub_dev()))</c>.</summary>
        private const string StubDevOnDisk =
            "{\n  \"id\": \"c0000000-0000-7000-8000-00000000000d\",\n  \"type\": \"character\",\n  \"name\": \"Dev\",\n"
            + "  \"description\": \"Dev desc\",\n  \"supertype\": \"Ferryman\",\n  \"subtype\": \"\",\n  \"image_url\": null,\n"
            + "  \"created_at\": \"2026-10-20T00:00:00Z\",\n  \"updated_at\": \"2026-10-20T01:00:00Z\",\n  \"change_seq\": 9,\n"
            + "  \"known_of\": true\n}\n";

        /// <summary>The package's <c>encode(known_of_for_disk(public_stub_dev()))</c>.</summary>
        private const string PublicStubDevOnDisk =
            "{\n  \"id\": \"c0000000-0000-7000-8000-00000000000d\",\n  \"type\": \"character\",\n  \"name\": \"Dev\",\n"
            + "  \"description\": \"Dev desc\",\n  \"supertype\": \"Ferryman\",\n  \"subtype\": \"\",\n  \"image_url\": null,\n"
            + "  \"physicality\": \"Tall\",\n  \"created_at\": \"2026-10-20T00:00:00Z\",\n  \"updated_at\": \"2026-10-20T01:00:00Z\",\n"
            + "  \"change_seq\": 9,\n  \"height\": 180,\n  \"species\": [\n    \"s0000000-0000-7000-8000-000000000001\"\n  ],\n"
            + "  \"known_of\": true\n}\n";

        /// <summary>The package's <c>encode(whole_dev())</c>: a whole element is written as served.</summary>
        private const string WholeDevOnDisk =
            "{\n  \"id\": \"c0000000-0000-7000-8000-00000000000d\",\n  \"type\": \"character\",\n  \"name\": \"Dev\",\n"
            + "  \"description\": \"Dev desc\",\n  \"supertype\": \"Ferryman\",\n  \"subtype\": \"\",\n  \"image_url\": null,\n"
            + "  \"physicality\": \"zzphys\",\n  \"location\": \"l1\",\n  \"friends\": [\n    \"c0000000-0000-7000-8000-00000000000a\"\n  ],\n"
            + "  \"STR\": 14,\n  \"created_at\": \"2026-10-20T00:00:00Z\",\n  \"updated_at\": \"2026-10-20T01:00:00Z\",\n"
            + "  \"change_seq\": 9,\n  \"created_by\": \"m-1\",\n  \"changed_by\": \"m-1\",\n  \"changed_via\": \"api\",\n"
            + "  \"x_secret\": \"zzext\"\n}\n";

        private string _parent;

        [SetUp]
        public void SetUp()
        {
            _parent = Path.Combine(Path.GetTempPath(), "ow-known-of-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_parent);
        }

        [TearDown]
        public void TearDown()
        {
            if (_parent != null && Directory.Exists(_parent)) Directory.Delete(_parent, recursive: true);
        }

        private static JObject WholeDev(params (string Key, JToken Value)[] kw) => With(OWJson.ParseObject(WholeDevJson), kw);
        private static JObject StubDev() => OWJson.ParseObject(StubDevJson);
        private static JObject MaraBody(params (string Key, JToken Value)[] kw) => With(OWJson.ParseObject(MaraJson), kw);

        /// <summary>The package's <c>{**body, **kw}</c>: a key it has keeps its place, a new one goes last.</summary>
        private static JObject With(JObject body, (string Key, JToken Value)[] kw)
        {
            foreach (var (key, value) in kw) body[key] = value;
            return body;
        }

        /// <summary>Dev as keel serves him since PR 117: the public fields in their wire place, every key sent.</summary>
        private static JObject PublicStubDev(params (string Key, JToken Value)[] kw)
        {
            var body = StubDev();
            body.Remove("known_of");
            body["physicality"] = "Tall";
            body["height"] = 180;
            body["weight"] = JValue.CreateNull();
            body["species"] = new JArray(Species);
            body["reputation"] = "";
            With(body, kw);
            body["known_of"] = true;
            return body;
        }

        private static string[] Keys(JObject o) => o.Properties().Select(p => p.Name).ToArray();

        private static OWClient Client(IOWTransport transport) => new OWClient(new OWClientConfig
        {
            ApiKey = "ow_w_test",
            ApiPin = "0000",
            Transport = transport,
            BaseUrl = "https://example.test/api/v2",
        });

        private static string Page(params JObject[] bodies)
            => "{\"data\":[" + string.Join(",", bodies.Select(b => b.ToString(Newtonsoft.Json.Formatting.None)))
               + "],\"has_more\":false,\"next_cursor\":null}";

        /// <summary>The package's <c>write_folder(root, WORLD, {"character": bodies})</c>, through this SDK's export.</summary>
        private async Task<string> Export(string name, params JObject[] characters)
        {
            var folder = Path.Combine(_parent, name);
            var transport = new RouteTransport { World = World }.Type("character", Page(characters));
            await OWFolderExport.ExportAsync(Client(transport), folder, new OWFolderExportOptions { IncludeMedia = false });
            return folder;
        }

        private static string DevPath(string folder)
            => Directory.GetFiles(Path.Combine(folder, "elements", "character"), "dev--*").Single();

        private static string DevText(string folder) => File.ReadAllText(DevPath(folder), new UTF8Encoding(false));

        private static JObject DevFile(string folder) => OWFolderWriter.ReadJsonFile(DevPath(folder));

        // -- The marker ---------------------------------------------------------------------------------

        [Test]
        public void TheKeptKeys_AreKeels_AndTheVersionIs0315()
        {
            CollectionAssert.AreEquivalent(Kept, OWFolderExport.KnownOfKeeps);
            Assert.AreEqual("0.3.15", OWFolderExport.FormatVersion);
        }

        [Test]
        public void IsKnownOf_OnlyForTrue()
        {
            Assert.IsTrue(OWPayload.IsKnownOf(StubDev()));
            foreach (var body in new[]
                     {
                         WholeDev(), WholeDev(("known_of", false)), WholeDev(("known_of", "true")), WholeDev(("known_of", 1)),
                         new JObject(), null,
                     })
            {
                Assert.IsFalse(OWPayload.IsKnownOf(body), body?.ToString() ?? "null");
            }
        }

        // -- Rule 1: a writer writes a stub as it is ----------------------------------------------------

        [Test]
        public async Task AStub_IsWrittenWithKnownOf_AndOnlyItsKeptKeys()
        {
            var folder = await Export("w", StubDev(), MaraBody());

            var onDisk = DevFile(folder);
            CollectionAssert.AreEquivalent(Kept.Concat(new[] { "known_of" }), Keys(onDisk), "no \"\", null or [] for the unset fields");
            Assert.AreEqual("known_of", Keys(onDisk).Last());
            Assert.AreEqual("Ferryman", (string)onDisk["supertype"]);
            Assert.AreEqual("", (string)onDisk["subtype"]);
            Assert.AreEqual(StubDevOnDisk, DevText(folder), "the package's bytes");
        }

        [Test]
        public async Task AWholeElement_IsWrittenUntouched()
        {
            var whole = WholeDev(("location", JValue.CreateNull()), ("friends", new JArray()));
            var folder = await Export("w", whole);

            var onDisk = DevFile(folder);
            Assert.IsTrue(JToken.DeepEquals(whole, onDisk), "its unset fields and extensions kept");
            Assert.IsFalse(onDisk.ContainsKey("known_of"));
        }

        [Test]
        public async Task Export_WritesAServedStubAsAStub_AndTheReaderSaysSo()
        {
            var folder = await Export("out", MaraBody(), StubDev());

            CollectionAssert.AreEquivalent(Kept.Concat(new[] { "known_of" }), Keys(DevFile(folder)));
            var read = OWFolderReader.Read(folder);
            CollectionAssert.AreEqual(new[] { Dev }, read.KnownOf);
            Assert.IsFalse(read.Elements.Single(e => e.Id == Mara).KnownOf);
        }

        [Test]
        public void AStub_KeepsEveryFieldSentWithAValue()
        {
            var sent = PublicStubDev();
            var output = OWFolderExport.KnownOfForDisk(sent);

            Assert.AreEqual("Tall", (string)output["physicality"]);
            Assert.AreEqual(180, (int)output["height"]);
            CollectionAssert.AreEqual(new[] { Species }, output["species"].Values<string>());
            foreach (var unset in new[] { "weight", "reputation", "location", "friends", "STR", "created_by", "changed_via" })
            {
                Assert.IsFalse(output.ContainsKey(unset), $"{unset}: \"\", null, [] never written");
            }

            Assert.AreEqual("", (string)output["subtype"], "the kept keys, even when empty");
            Assert.AreEqual(JTokenType.Null, output["image_url"].Type);
            CollectionAssert.AreEqual(Keys(sent).Where(output.ContainsKey), Keys(output), "received order, known_of last");
            Assert.AreEqual(PublicStubDevOnDisk, OWFolderWriter.Serialize(output), "the package's bytes");
            Assert.IsTrue(JToken.DeepEquals(PublicStubDev(), sent), "the input is not changed");
        }

        [Test]
        public void ZeroAndFalse_AreValues_NullAndEmpty_AreNot()
        {
            var output = OWFolderExport.KnownOfForDisk(PublicStubDev(
                ("height", 0), ("species", new JArray()), ("weight", JValue.CreateNull()), ("x_flag", false)));

            Assert.AreEqual(0, (int)output["height"]);
            Assert.AreEqual(false, (bool)output["x_flag"]);
            Assert.IsFalse(output.ContainsKey("species"));
            Assert.IsFalse(output.ContainsKey("weight"));
        }

        [Test]
        public void TheRule_ReadsTheBody_NotAListOfPublicFields()
        {
            // A field no public list names (a later serial's, or another type's) is kept when it holds a value.
            var output = OWFolderExport.KnownOfForDisk(With(StubDev(), new (string, JToken)[] { ("location", "l1"), ("STR", 3) }));

            Assert.AreEqual("l1", (string)output["location"]);
            Assert.AreEqual(3, (int)output["STR"]);
        }

        [Test]
        public async Task APublicStub_IsWrittenWithItsValues_AndReadBack()
        {
            var folder = await Export("w", PublicStubDev(), MaraBody());

            var onDisk = DevFile(folder);
            CollectionAssert.AreEquivalent(Kept.Concat(new[] { "physicality", "height", "species", "known_of" }), Keys(onDisk));
            Assert.AreEqual(PublicStubDevOnDisk, DevText(folder));

            var dev = OWFolderReader.Read(folder).Elements.Single(e => e.Id == Dev);
            Assert.IsTrue(dev.KnownOf);
            Assert.AreEqual(180, (int)dev.Body["height"]);
            Assert.IsFalse(dev.Body.ContainsKey("reputation"));
        }

        // -- Rule 2: a reader treats it as known of -----------------------------------------------------

        [Test]
        public async Task TheReader_FlagsAStub_AndFillsInNothing()
        {
            var read = OWFolderReader.Read(await Export("w", StubDev(), MaraBody()));

            var dev = read.Elements.Single(e => e.Id == Dev);
            Assert.IsTrue(dev.KnownOf);
            CollectionAssert.AreEqual(new[] { Dev }, read.KnownOf);
            Assert.IsFalse(dev.Body.ContainsKey("physicality"));
            Assert.IsFalse(dev.Body.ContainsKey("friends"));
            Assert.IsFalse(read.Elements.Single(e => e.Id == Mara).KnownOf);
        }

        [Test]
        public async Task TheReader_ReadsAWholeElementAsWhole()
        {
            var read = OWFolderReader.Read(await Export("w",
                WholeDev(), WholeDev(("id", Mara), ("name", "Mara"), ("known_of", false))));

            CollectionAssert.IsEmpty(read.KnownOf);
            Assert.IsTrue(read.Elements.All(e => !e.KnownOf));
        }

        [Test]
        public void ATypedStub_SaysSo_AndAWholeOneDoesNot()
        {
            var stub = OWJson.Deserialize<OWCharacter>(StubDevJson);
            Assert.IsTrue(stub.IsKnownOf);
            Assert.AreEqual("Dev", stub.Name);

            var whole = OWJson.Deserialize<OWCharacter>(WholeDevJson);
            Assert.IsFalse(whole.IsKnownOf);
            Assert.IsFalse(OWJson.Deserialize<OWCharacter>(WholeDev(("known_of", false)).ToString()).IsKnownOf);

            // Not a modelled bool: a whole element serializes with no known_of at all, never false.
            Assert.IsFalse(OWJson.ParseObject(OWJson.Serialize(whole)).ContainsKey("known_of"));
            Assert.IsFalse(OWJson.ParseObject(OWJson.Serialize(new OWCharacter { Name = "New" })).ContainsKey("known_of"));
        }

        // -- Rule 3: a stub is never pushed -------------------------------------------------------------

        // npm's WHOLE_READ and STUB_READ (physicality is one of a Character's public fields: a stub carries its value).
        private static JObject WholeRead() => JObject.Parse(
            @"{""type"":""character"",""id"":""x"",""name"":""Dev"",""description"":""Dev desc"",""physicality"":""tall"","
            + @"""created_at"":""2026-10-20T00:00:00Z"",""updated_at"":""2026-10-20T00:00:00Z"",""change_seq"":9,"
            + @"""created_by"":null,""changed_by"":null,""changed_via"":""api"",""x_note"":""kept""}");

        private static JObject StubRead() => JObject.Parse(
            @"{""type"":""character"",""id"":""x"",""name"":""Dev"",""description"":""Dev desc"",""physicality"":""tall"","
            + @"""location"":null,""friends"":[],""created_at"":""2026-10-20T00:00:00Z"",""updated_at"":""2026-10-20T00:00:00Z"","
            + @"""change_seq"":9,""created_by"":null,""changed_by"":null,""changed_via"":null,""known_of"":true}");

        [Test]
        public async Task AReadBodyEchoedIntoCreatePatchAndBulk_SendsNoKnownOf()
        {
            var t = new FakeTransport { ResponseBody = @"{""id"":""x"",""name"":""Dev""}" };
            var client = Client(t);

            // A whole read body has none; a stray non-true marker is stripped like any read-only key.
            foreach (var body in new[] { WholeRead(), With(WholeRead(), new (string, JToken)[] { ("known_of", false) }) })
            {
                await client.PatchAsync<JObject>("character", "x", body);
                Assert.IsFalse(t.LastBody.ContainsKey("known_of"));
                Assert.AreEqual("tall", (string)t.LastBody["physicality"]);
                Assert.AreEqual("kept", (string)t.LastBody["x_note"]);
                await client.CreateAsync<JObject>("character", body);
                Assert.IsFalse(t.LastBody.ContainsKey("known_of"));
            }

            t.ResponseBody = @"{""errors"":false,""items"":[{""status"":201,""id"":""x""}]}";
            await client.BulkAsync(new[]
            {
                new OWBulkItem("character", With(WholeRead(), new (string, JToken)[] { ("known_of", false) })),
            });
            Assert.IsFalse(((JObject)t.LastBody["items"][0]["element"]).ContainsKey("known_of"));
        }

        [Test]
        public void AKnownOfStub_IsRefusedOnEveryElementWrite_BeforeAnythingIsSent()
        {
            var t = new FakeTransport { ResponseBody = @"{""id"":""x"",""name"":""Dev""}" };
            var client = Client(t);

            var patch = Assert.ThrowsAsync<ArgumentException>(async () => await client.PatchAsync<JObject>("character", "x", StubRead()));
            StringAssert.StartsWith("patch: this body is a known-of stub", patch.Message);
            var create = Assert.ThrowsAsync<ArgumentException>(async () => await client.CreateAsync<JObject>("character", StubRead()));
            StringAssert.StartsWith("create: this body is a known-of stub", create.Message);

            // A typed stub carries the marker in its extensions bag, so it is refused the same way.
            var typed = OWJson.Deserialize<OWCharacter>(StubRead().ToString());
            Assert.ThrowsAsync<ArgumentException>(async () => await client.PatchAsync<JObject>("character", "x", typed));

            var bulk = Assert.ThrowsAsync<ArgumentException>(async () => await client.BulkAsync(new[]
            {
                new OWBulkItem("character", WholeRead()),
                new OWBulkItem("character", StubRead()),
            }));
            StringAssert.StartsWith("bulk: items[1]: this body is a known-of stub", bulk.Message);

            Assert.AreEqual(0, t.Calls.Count, "nothing is sent");
        }

        private static FakeTransport Refusing(int status, string code, string param) => new FakeTransport
        {
            Status = status,
            ResponseBody = "{\"error\":{\"type\":\"invalid_request\",\"code\":\"" + code + "\",\"message\":\"" + code
                           + "\",\"param\":\"" + param + "\",\"doc_url\":\"https://onlyworlds.github.io/api/errors#" + code + "\"}}",
        };

        private static OWApiError Thrown(int status, string code, string param)
            => Assert.ThrowsAsync<OWApiError>(async () => await Client(Refusing(status, code, param)).GetWorldAsync());

        [Test]
        public void A422KnownOfNotWritable_IsKnownOfNotWritable_AnotherCodeOrStatusIsNot()
        {
            var refused = Thrown(422, "known_of_not_writable", "known_of");
            Assert.IsTrue(refused.IsKnownOfNotWritable);
            Assert.AreEqual("known_of", refused.Param);
            Assert.IsTrue(refused.IsValidationError);
            Assert.IsFalse(refused.IsCharacterTaken);
            Assert.IsFalse(refused.IsWorldFull);

            Assert.IsFalse(Thrown(422, "invalid_request", "known_of").IsKnownOfNotWritable, "the generic unknown-field 422 is not it");
            Assert.IsFalse(Thrown(422, "character_taken", "character").IsKnownOfNotWritable);
            Assert.IsFalse(Thrown(400, "known_of_not_writable", "known_of").IsKnownOfNotWritable, "the code on another status is not it");
        }

        [Test]
        public void OWEdit_RefusesAStub_AtBegin()
        {
            // Rule 3 forbids a PATCH made from a known-of file, even one that would send only what changed.
            var stub = OWJson.Deserialize<OWCharacter>(StubDevJson);

            var ex = Assert.Throws<ArgumentException>(() => OWEdit.Begin(stub));
            StringAssert.Contains("known-of stub", ex.Message);
            Assert.DoesNotThrow(() => OWEdit.Begin(OWJson.Deserialize<OWCharacter>(WholeDevJson)));
        }

        // -- Rule 4: a stub that later arrives whole is written whole -----------------------------------

        [Test]
        public async Task AStubThatArrivesWhole_IsWrittenWhole_AndTheOtherWayAStub()
        {
            // A walk: stub, then whole.
            var folder = await Export("w", StubDev(), WholeDev());
            Assert.AreEqual(WholeDevOnDisk, DevText(folder));
            Assert.IsFalse(DevFile(folder).ContainsKey("known_of"));

            // The other way (whole, then a stub: the grant went) is the last body too: a stub.
            folder = await Export("x", WholeDev(), StubDev());
            CollectionAssert.AreEquivalent(Kept.Concat(new[] { "known_of" }), Keys(DevFile(folder)));
        }
    }
}
