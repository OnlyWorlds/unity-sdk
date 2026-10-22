using System;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The simple case fold every <c>.owworld</c> reader uses (folder spec §4.7, OW-18): Unicode's CaseFolding.txt,
    /// statuses C and S, generated into <c>OWCaseFold</c> by <c>tools/gen_casefold.py</c>.
    /// </summary>
    /// <remarks>
    /// The fold is internal to the runtime assembly, which exposes no internals, so it is reached by reflection here:
    /// the archive's own refusals test it through the public <see cref="OWWorldArchive.Read"/>.
    /// </remarks>
    public class OWCaseFoldTests
    {
        private static readonly Type FoldType = typeof(OWWorldArchive).Assembly.GetType("OnlyWorlds.Sdk.OWCaseFold", true);

        private static readonly Func<string, string> Fold = (Func<string, string>)Delegate.CreateDelegate(
            typeof(Func<string, string>),
            FoldType.GetMethod("Fold", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(string) }, null));

        private static T Constant<T>(string name)
            => (T)FoldType.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        [Test]
        public void EveryCodePoint_FoldsAsCaseFoldingTxtSays()
        {
            // All 1,112,064 scalar values through the C# code, digested exactly as the generator digests the file:
            // one line per code point the fold changes. The digest is CaseFolding.txt's, so this covers the code
            // and the table over every code point, not a sample.
            var lines = new StringBuilder();
            var changed = 0;
            var runtimeDisagrees = 0;
            for (var cp = 0; cp <= 0x10FFFF; cp++)
            {
                if (cp >= 0xD800 && cp <= 0xDFFF) continue;

                var one = char.ConvertFromUtf32(cp);
                var folded = Fold(one);
                var to = char.ConvertToUtf32(folded, 0);
                if (char.ConvertFromUtf32(to) != folded) Assert.Fail($"U+{cp:X4} folds to more than one code point");

                if (to != cp)
                {
                    lines.Append(cp.ToString("X4", CultureInfo.InvariantCulture)).Append(';')
                        .Append(to.ToString("X4", CultureInfo.InvariantCulture)).Append('\n');
                    changed++;
                }

                if (one.ToUpperInvariant().ToLowerInvariant() != folded) runtimeDisagrees++;
            }

            string digest;
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.ASCII.GetBytes(lines.ToString()));
                digest = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }

            TestContext.WriteLine($"Unicode {Constant<string>("UnicodeVersion")}: {changed} code points fold; "
                                  + $"this runtime's upper-then-lower differs from the fold on {runtimeDisagrees}");
            Assert.AreEqual(Constant<int>("Count"), changed);
            Assert.AreEqual(Constant<string>("Digest"), digest, "The fold is not CaseFolding.txt's C and S lines.");
        }

        [TestCase("ς", "σ")] // final sigma: lowercasing keeps it apart, folding does not
        [TestCase("Σ", "σ")]
        [TestCase("ſ", "s")] // long s
        [TestCase("K", "k")] // the Kelvin sign
        [TestCase("ẞ", "ß")] // capital sharp s, status S
        [TestCase("ꭰ", "Ꭰ")] // Cherokee folds to its capitals
        [TestCase("\U00010400", "\U00010428")] // Deseret, past U+FFFF
        [TestCase("Ab\U00010400ς", "ab\U00010428σ")]
        [TestCase("ß", "ß")] // simple folding never makes ss
        [TestCase("İ", "İ")] // dotted capital I: Turkic (T) and full (F) only
        [TestCase("ı", "ı")] // dotless i
        [TestCase("ﬅ", "ﬆ")] // the long s t ligature, status S (the fixture reference leaves it)
        [TestCase("abc", "abc")]
        [TestCase("", "")]
        public void Fold_IsSimpleCaseFolding(string text, string expected)
        {
            Assert.AreEqual(expected, Fold(text));
        }

        [Test]
        public void Fold_KeepsALoneSurrogate()
        {
            Assert.AreEqual("a\uD801b", Fold("A\uD801B"));
            Assert.AreEqual("\uDC00x", Fold("\uDC00X"));
        }
    }
}
