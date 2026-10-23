using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The hidden mark (folder format §4.8, RULED 2026-10-23; keel #421, #587): only the JSON boolean <c>true</c> hides,
    /// and a typed element reads it from its extensions and writes it back as it came.
    /// </summary>
    public class OWHiddenTests
    {
        // Kills: a truthy reading (the string "true", a 1), or a null body that throws.
        [Test]
        public void OnlyTheBooleanTrue_Hides()
        {
            Assert.IsTrue(OWPayload.IsHidden(JObject.Parse(@"{""id"":""c1"",""hidden"":true}")));
            foreach (var json in new[]
                     {
                         @"{""id"":""c1"",""hidden"":false}", @"{""id"":""c1"",""hidden"":""true""}", @"{""id"":""c1"",""hidden"":1}",
                         @"{""id"":""c1"",""hidden"":null}", @"{""id"":""c1""}",
                     })
                Assert.IsFalse(OWPayload.IsHidden(JObject.Parse(json)), json);
            Assert.IsFalse(OWPayload.IsHidden(null));
        }

        // Kills: the element reading another key, or dropping the mark on its way back out.
        [Test]
        public void ATypedElement_ReadsTheMark_AndWritesItBackAsItCame()
        {
            var hidden = OWJson.Deserialize<OWCharacter>(@"{""id"":""c1"",""name"":""Pal"",""hidden"":true}");
            Assert.IsTrue(hidden.IsHidden);
            Assert.AreEqual(JTokenType.Boolean, JObject.Parse(OWJson.Serialize(hidden))["hidden"]?.Type, "kept as it came");

            Assert.IsFalse(OWJson.Deserialize<OWCharacter>(@"{""id"":""c1"",""name"":""Pal"",""hidden"":false}").IsHidden);
            Assert.IsFalse(OWJson.Deserialize<OWCharacter>(@"{""id"":""c1"",""name"":""Pal""}").IsHidden);
        }
    }
}
