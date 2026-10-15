using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests
{
    /// <summary>
    /// <see cref="OWId"/> mints RFC 9562 UUIDv7, the layout keel's <c>core/ids.py</c>, the npm SDK and the
    /// Python package mint.
    /// </summary>
    public class OWIdTests
    {
        static readonly Regex V7 = new Regex("^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$");

        static long TimestampOf(string id) => Convert.ToInt64(id.Replace("-", "").Substring(0, 12), 16);

        [Test]
        public void NewId_IsAVersion7VariantOneUuid()
        {
            for (var i = 0; i < 200; i++)
            {
                var id = OWId.NewId();
                StringAssert.IsMatch(V7.ToString(), id);
                Assert.IsTrue(Guid.TryParse(id, out _), id);
            }
        }

        [Test]
        public void NewId_CarriesTheClockInItsFirst48Bits()
        {
            var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var id = OWId.NewId();
            var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var ms = TimestampOf(id);
            Assert.That(ms, Is.InRange(before, after), id);
        }

        [Test]
        public void NewId_WritesEvery48BitTimestampBigEndian()
        {
            // 2026's milliseconds pass 2^32; the npm port floored its high bytes by division for that reason.
            foreach (var ms in new[] { 0L, 1L, 0xFFFFFFFFL, 0x100000000L, 1792108800000L, 0xFFFFFFFFFFFFL })
            {
                var id = OWId.NewId(ms);
                Assert.AreEqual(ms, TimestampOf(id), id);
                StringAssert.IsMatch(V7.ToString(), id);
            }

            StringAssert.StartsWith("0000000000017", OWId.NewId(1).Replace("-", ""));
            StringAssert.StartsWith("01a1", OWId.NewId(1792108800000L), "2026's ids read 019 or 01a, as keel's do.");
        }

        [Test]
        public void NewId_SortsByCreationAcrossMilliseconds()
        {
            var earlier = OWId.NewId(1792108800000L);
            var later = OWId.NewId(1792108800001L);
            Assert.Less(string.CompareOrdinal(earlier, later), 0, earlier + " / " + later);
        }

        [Test]
        public void NewId_InOneMillisecondStillDiffers()
        {
            var seen = new HashSet<string>();
            for (var i = 0; i < 1000; i++)
            {
                Assert.IsTrue(seen.Add(OWId.NewId(1792108800000L)), "74 random bits should not repeat in 1000 ids.");
            }
        }
    }
}
