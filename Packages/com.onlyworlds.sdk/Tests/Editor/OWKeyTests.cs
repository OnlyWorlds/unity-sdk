using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// Key classification, mirroring the npm SDK's detectKeyKind contract.
    /// </summary>
    public class OWKeyTests
    {
        [Test]
        public void DetectsAllFourKinds()
        {
            Assert.AreEqual(OWKeyKind.Write, OWKey.DetectKind("ow_w_abc"));
            Assert.AreEqual(OWKeyKind.Read, OWKey.DetectKind("ow_r_abc"));
            Assert.AreEqual(OWKeyKind.Account, OWKey.DetectKind("ow_a_abc"));
            Assert.AreEqual(OWKeyKind.Legacy, OWKey.DetectKind("0000000011"));
            Assert.AreEqual(OWKeyKind.Unknown, OWKey.DetectKind("garbage"));
        }

        [Test]
        public void NullAndEmpty_AreUnknown()
        {
            Assert.AreEqual(OWKeyKind.Unknown, OWKey.DetectKind(null));
            Assert.AreEqual(OWKeyKind.Unknown, OWKey.DetectKind(""));
        }

        [Test]
        public void LegacyRequiresExactlyTenDigits()
        {
            Assert.AreEqual(OWKeyKind.Legacy, OWKey.DetectKind("0123456789"));
            Assert.AreEqual(OWKeyKind.Unknown, OWKey.DetectKind("012345678"), "9 digits is not a legacy key.");
            Assert.AreEqual(OWKeyKind.Unknown, OWKey.DetectKind("01234567890"), "11 digits is not a legacy key.");
            Assert.AreEqual(OWKeyKind.Unknown, OWKey.DetectKind("01234w6789"), "Non-digits are not a legacy key.");
        }

        [Test]
        public void AuthShape_FollowsFromKind()
        {
            Assert.IsTrue(OWKey.UsesBearer(OWKeyKind.Account));
            Assert.IsFalse(OWKey.UsesBearer(OWKeyKind.Write));

            Assert.IsTrue(OWKey.RequiresPin(OWKeyKind.Write));
            Assert.IsTrue(OWKey.RequiresPin(OWKeyKind.Legacy));
            Assert.IsFalse(OWKey.RequiresPin(OWKeyKind.Read), "Read keys are the share primitive -- no PIN.");
            Assert.IsFalse(OWKey.RequiresPin(OWKeyKind.Account));
        }

        [Test]
        public void ReadKeys_CannotWrite()
        {
            Assert.IsFalse(OWKey.CanWrite(OWKeyKind.Read));
            Assert.IsFalse(OWKey.CanWrite(OWKeyKind.Unknown));
            Assert.IsTrue(OWKey.CanWrite(OWKeyKind.Write));
            Assert.IsTrue(OWKey.CanWrite(OWKeyKind.Account));
        }

        [Test]
        public void ReadOnlyFields_MatchTheWireContract()
        {
            // Mirrors READ_ONLY_FIELDS in the npm SDK. A divergence means one client gets 422s
            // the other does not.
            CollectionAssert.AreEquivalent(
                new[] { "world", "type", "created_at", "updated_at", "change_seq" },
                OWPayload.ReadOnlyFields);

            Assert.IsTrue(OWPayload.IsReadOnly("world"));
            Assert.IsFalse(OWPayload.IsReadOnly("name"));
        }
    }
}
