using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The typed write path: change a few fields, send exactly those.
    /// </summary>
    /// <remarks>
    /// The design decision this implements was ruled on 2026-07-28 and then went unbuilt for a day,
    /// while a converter comment cheerfully deferred to "the dirty-tracker's job". Writing without
    /// it meant hand-building a JObject and hoping.
    /// </remarks>
    public class OWEditTests
    {
        private FakeTransport _transport;

        [SetUp]
        public void SetUp() => _transport = new FakeTransport();

        private OWClient Client() => new OWClient(new OWClientConfig
        {
            ApiKey = "ow_w_test",
            ApiPin = "1234",
            Transport = _transport,
            BaseUrl = "https://example.test/api/v2",
        });

        private const string CharacterJson = @"{
            ""id"": ""11111111-2222-4333-8444-555555555555"",
            ""name"": ""Quillon"",
            ""type"": ""character"",
            ""world"": ""w-1"",
            ""change_seq"": 42,
            ""supertype"": ""Rigger"",
            ""height"": 180,
            ""weight"": null,
            ""charisma"": null,
            ""x_atlas_pinned"": true
        }";

        private static OWCharacter Load() => OWJson.Deserialize<OWCharacter>(CharacterJson);

        // -- The core promise -------------------------------------------------

        [Test]
        public void UntouchedElement_ProducesAnEmptyPatch()
        {
            var edit = OWEdit.Begin(Load());

            Assert.IsFalse(edit.HasChanges);
            Assert.AreEqual(0, edit.BuildPatch().Count,
                "Nothing changed, so nothing may be sent -- a no-op PATCH still bumps updated_at "
                + "and shows up as a false edit in everyone else's change feed.");
        }

        [Test]
        public void OnlyChangedFields_AreSent()
        {
            var character = Load();
            var edit = OWEdit.Begin(character);

            character.Name = "Quillon the Rigger";

            var patch = edit.BuildPatch();

            Assert.AreEqual(1, patch.Count, "One field changed, so exactly one field is sent.");
            Assert.AreEqual("Quillon the Rigger", patch["name"].ToString());
            Assert.IsNull(patch["height"],
                "An unchanged field must NOT ride along. PATCH is destructive on what it receives, "
                + "so resending a stale local value silently overwrites whatever the server holds.");
        }

        [Test]
        public void ClearingAField_SendsExplicitNull_NotOmission()
        {
            // The case the whole SerializableNullable design exists for, on the write side.
            var character = Load();
            var edit = OWEdit.Begin(character);

            character.Height = default;   // unset

            var patch = edit.BuildPatch();

            Assert.IsTrue(patch.ContainsKey("height"), "Clearing a field IS a change.");
            Assert.AreEqual(JTokenType.Null, patch["height"].Type,
                "It must go as explicit null. Omitting it means 'no opinion' and the clear is lost.");
        }

        [Test]
        public void SettingAFieldToZero_IsDistinctFromClearingIt()
        {
            var character = Load();
            var edit = OWEdit.Begin(character);

            character.Height = 0;   // deliberate zero, not unset

            var patch = edit.BuildPatch();

            Assert.AreEqual(JTokenType.Integer, patch["height"].Type);
            Assert.AreEqual(0, patch["height"].Value<int>(),
                "A deliberate 0 and an unset field are different claims all the way to the wire.");
        }

        [Test]
        public void SettingAnAlreadyNullFieldToNull_IsNotAChange()
        {
            var character = Load();
            var edit = OWEdit.Begin(character);

            character.Weight = default;   // it was already null

            Assert.IsFalse(edit.HasChanges,
                "Assigning the value something already had is not an edit.");
        }

        [Test]
        public void ServerOwnedFields_AreNeverInThePatch()
        {
            var character = Load();
            var edit = OWEdit.Begin(character);
            character.Name = "changed";

            var patch = edit.BuildPatch();

            foreach (var field in OWPayload.ReadOnlyFields)
            {
                Assert.IsNull(patch[field], $"{field} is server-owned and must never be written.");
            }
        }

        [Test]
        public void ExtensionFields_AreNotResent_WhenUnchanged()
        {
            var character = Load();
            var edit = OWEdit.Begin(character);
            character.Name = "changed";

            var patch = edit.BuildPatch();

            Assert.IsNull(patch["x_atlas_pinned"],
                "An untouched extension field is not a change. It survives on the server by not "
                + "being mentioned -- and PATCH only destroys what it receives.");
        }

        [Test]
        public void ChangedFields_AreNameable()
        {
            var character = Load();
            var edit = OWEdit.Begin(character);

            character.Name = "a";
            character.Height = 190;

            CollectionAssert.AreEquivalent(new[] { "name", "height" }, edit.ChangedFields().ToList());
        }

        // -- Commit -----------------------------------------------------------

        [Test]
        public async Task Commit_SendsAPatchWithOnlyTheChangedFields()
        {
            var character = Load();
            var edit = OWEdit.Begin(character);
            character.Name = "Renamed";

            _transport.Bodies.Enqueue(CharacterJson);
            await edit.CommitAsync(Client(), "character");

            Assert.AreEqual("PATCH", _transport.Last.Method);
            StringAssert.Contains("/character/11111111-2222-4333-8444-555555555555/", _transport.Last.Url);

            var sent = _transport.LastBody;
            Assert.AreEqual(1, sent.Count);
            Assert.AreEqual("Renamed", sent["name"].ToString());
        }

        [Test]
        public async Task Commit_WithNothingChanged_SendsNothingAtAll()
        {
            var edit = OWEdit.Begin(Load());

            await edit.CommitAsync(Client(), "character");

            Assert.AreEqual(0, _transport.Calls.Count,
                "The cheapest request is the one never sent -- and it is also the one that cannot "
                + "produce a phantom edit.");
        }

        [Test]
        public async Task Commit_RebaselinesSoASecondCommitIsClean()
        {
            var character = Load();
            var edit = OWEdit.Begin(character);
            character.Name = "First";

            _transport.Bodies.Enqueue(CharacterJson);
            await edit.CommitAsync(Client(), "character");

            Assert.IsFalse(edit.HasChanges, "After committing, the session starts fresh.");

            character.Name = "Second";
            Assert.IsTrue(edit.HasChanges);
            CollectionAssert.AreEquivalent(new[] { "name" }, edit.ChangedFields().ToList());
        }

        [Test]
        public void Commit_WithoutAnId_Refuses()
        {
            var orphan = OWJson.Deserialize<OWCharacter>(@"{""name"":""No id""}");
            var edit = OWEdit.Begin(orphan);
            orphan.Name = "still no id";

            Assert.ThrowsAsync<System.InvalidOperationException>(
                async () => await edit.CommitAsync(Client(), "character"));
        }

        [Test]
        public void Rebase_DiscardsPendingChanges()
        {
            var character = Load();
            var edit = OWEdit.Begin(character);
            character.Name = "changed";

            edit.Rebase();

            Assert.IsFalse(edit.HasChanges);
        }
    }
}
