using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The hidden mark (folder format §4.8, RULED 2026-10-23): any value of <c>hidden</c> but absent,
    /// <c>null</c> and <c>false</c> hides, a doubt counting as the mark, as the npm SDK and the Python package read it; a
    /// typed element reads it from its extensions and writes it back as it came.
    /// </summary>
    public class OWHiddenTests
    {
        // Kills: a narrow reading (a doubt handed on, its card without the word), false or null read as marks, or a null
        // body that throws.
        [Test]
        public void TheMarkOrADoubt_Hides()
        {
            foreach (var json in new[]
                     {
                         @"{""hidden"":true}", @"{""hidden"":""true""}", @"{""hidden"":1}", @"{""hidden"":0}", @"{""hidden"":{}}",
                         @"{""hidden"":""""}",
                     })
                Assert.IsTrue(OWPayload.IsHidden(JObject.Parse(json)), json);
            foreach (var json in new[] { @"{""hidden"":false}", @"{""hidden"":null}", @"{""id"":""c1""}" })
                Assert.IsFalse(OWPayload.IsHidden(JObject.Parse(json)), json);
            Assert.IsFalse(OWPayload.IsHidden(null));
        }

        // Kills: the element reading another key or another rule, or dropping the mark on its way back out.
        [Test]
        public void ATypedElement_ReadsTheMark_AndWritesItBackAsItCame()
        {
            var hidden = OWJson.Deserialize<OWCharacter>(@"{""id"":""c1"",""name"":""Pal"",""hidden"":true}");
            Assert.IsTrue(hidden.IsHidden);
            Assert.AreEqual(JTokenType.Boolean, JObject.Parse(OWJson.Serialize(hidden))["hidden"]?.Type, "kept as it came");

            var doubt = OWJson.Deserialize<OWCharacter>(@"{""id"":""c1"",""name"":""Pal"",""hidden"":""true""}");
            Assert.IsTrue(doubt.IsHidden);
            Assert.AreEqual(JTokenType.String, JObject.Parse(OWJson.Serialize(doubt))["hidden"]?.Type, "a doubt kept as it came");

            Assert.IsFalse(OWJson.Deserialize<OWCharacter>(@"{""id"":""c1"",""name"":""Pal"",""hidden"":false}").IsHidden);
            Assert.IsFalse(OWJson.Deserialize<OWCharacter>(@"{""id"":""c1"",""name"":""Pal""}").IsHidden);
        }
    }
}
