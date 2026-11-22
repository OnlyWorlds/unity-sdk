// OWText.TrimAndFold and OWText.ByCodePoint: names compared as the profiles compare them (room.md's "trimmed and
// without regard to case, defined once"; ordered by code point). Each case is a place where a language's own function
// answers differently: C#'s Trim() has its own set, ToLowerInvariant() leaves the long s, and StringComparer.Ordinal
// compares UTF-16 units. Ported from @onlyworlds/sdk 4.180.0, test/names-compare.test.mjs.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    public class OWTextTests
    {
        private static string Cp(params int[] points) => string.Concat(points.Select(char.ConvertFromUtf32));

        // The 25 code points of Unicode's White_Space property (PropList.txt).
        private static readonly int[] WhiteSpace =
        {
            0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x20, 0x85, 0xa0, 0x1680,
            0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x200a,
            0x2028, 0x2029, 0x202f, 0x205f, 0x3000,
        };

        [Test]
        public void TrimAndFold_TrimsEachWhiteSpaceCodePointAtEitherEnd_AndNothingInside()
        {
            foreach (var w in WhiteSpace)
                Assert.AreEqual("in design", OWText.TrimAndFold(Cp(w) + "in design" + Cp(w)), $"U+{w:x}");

            var all = Cp(WhiteSpace);
            Assert.AreEqual("locked", OWText.TrimAndFold(all + "Locked" + all));
            Assert.AreEqual("in" + Cp(0x3000) + "design", OWText.TrimAndFold("in" + Cp(0x3000) + "design"));
            Assert.AreEqual("", OWText.TrimAndFold(""));
            Assert.AreEqual("", OWText.TrimAndFold(all));
        }

        [Test]
        public void TrimAndFold_KeepsWhatIsNoWhiteSpace_WhereALanguageTrimTakesIt()
        {
            // JavaScript's trim() strips U+FEFF; Python's strip() strips U+001C to U+001F. None is White_Space.
            foreach (var keep in new[] { 0xfeff, 0x200b, 0x180e, 0x1c, 0x1d, 0x1e, 0x1f })
            {
                Assert.AreEqual(Cp(keep) + "locked", OWText.TrimAndFold(Cp(keep) + "locked"), $"leading U+{keep:x}");
                Assert.AreEqual("locked" + Cp(keep), OWText.TrimAndFold("locked" + Cp(keep)), $"trailing U+{keep:x}");
            }
        }

        [Test]
        public void TrimAndFold_FoldsByTheSimpleRowsOnly_NeverByLowerCasingOrFullFolding()
        {
            Assert.AreEqual("message", OWText.TrimAndFold("ME" + Cp(0x17f, 0x17f) + "AGE")); // the long s folds to s
            Assert.AreNotEqual("message", ("ME" + Cp(0x17f, 0x17f) + "AGE").ToLowerInvariant(), "lower-casing leaves the long s");
            Assert.AreEqual("king", OWText.TrimAndFold(Cp(0x212a) + "ing")); // the Kelvin sign folds to k
            Assert.AreEqual("me" + Cp(0xdf) + "age", OWText.TrimAndFold("Me" + Cp(0xdf) + "age")); // the sharp s has only a full folding
            Assert.AreEqual(Cp(0x131), OWText.TrimAndFold(Cp(0x131))); // the dotless i has no row
            Assert.AreEqual(Cp(0x130), OWText.TrimAndFold(Cp(0x130))); // the dotted capital I has only F and T rows
            Assert.AreEqual("te" + Cp(0xfb06), OWText.TrimAndFold("te" + Cp(0xfb06))); // the st ligature: full folding only
            Assert.AreEqual(Cp(0x10428), OWText.TrimAndFold(Cp(0x10400))); // an astral letter folds by its row
            Assert.AreEqual("locked", OWText.TrimAndFold("LOCKED"));
            Assert.AreEqual("narrative", OWText.TrimAndFold(Cp(0x85) + "NARRATIVE" + Cp(0x3000)));
        }

        [Test]
        public void TrimAndFold_RefusesNull()
        {
            var e = Assert.Throws<ArgumentNullException>(() => OWText.TrimAndFold(null));
            StringAssert.Contains("TrimAndFold takes a string, not null", e.Message);
        }

        [Test]
        public void ByCodePoint_OrdersByCodePoint_WhereUtf16UnitsPartFromIt()
        {
            string astral = Cp(0x10428), fullWidth = Cp(0xff5a);
            Assert.Less(StringComparer.Ordinal.Compare(astral, fullWidth), 0, "UTF-16 units put the astral letter first");
            Assert.Less(OWText.CompareByCodePoint(fullWidth, astral), 0);
            Assert.Greater(OWText.CompareByCodePoint(astral, fullWidth), 0);

            var sorted = new List<string> { astral, fullWidth, "z", "Z" };
            sorted.Sort(OWText.ByCodePoint);
            CollectionAssert.AreEqual(new[] { "Z", "z", fullWidth, astral }, sorted);

            Assert.AreEqual(0, OWText.CompareByCodePoint("", ""));
            Assert.AreEqual(0, OWText.CompareByCodePoint("abc", "abc"));
            Assert.Less(OWText.CompareByCodePoint("ab", "abc"), 0);
            Assert.Greater(OWText.CompareByCodePoint("abc", "ab"), 0);
            Assert.Less(OWText.CompareByCodePoint("Zebra", "apple"), 0, "by code point, never a collation");
            Assert.Less(OWText.CompareByCodePoint(null, ""), 0, "null sorts before any string");
        }

        [Test]
        public void NamesOrderedAsTheProfilesOrderThem_FoldsByCodePoint_TiesById()
        {
            var rows = new[]
            {
                (Id: "b2", Name: " apple"),
                (Id: "a1", Name: Cp(0x2003) + "APPLE"),
                (Id: "c3", Name: Cp(0x10400)),
                (Id: "d4", Name: Cp(0xff3a)),
            };
            var order = rows.ToList();
            order.Sort((x, y) =>
            {
                var byName = OWText.CompareByCodePoint(OWText.TrimAndFold(x.Name), OWText.TrimAndFold(y.Name));
                return byName != 0 ? byName : OWText.CompareByCodePoint(x.Id, y.Id);
            });
            CollectionAssert.AreEqual(new[] { "a1", "b2", "d4", "c3" }, order.Select(r => r.Id));
        }
    }
}
