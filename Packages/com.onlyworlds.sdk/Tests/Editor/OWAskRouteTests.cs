using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The TypeScript SDK's AI routes this client gained in its fourth tranche: a question about a world
    /// (<see cref="OWClient.AskAsync"/>), notes sorted into rows to review (<see cref="OWClient.SortAsync"/>) and the
    /// account's daily meter (<see cref="OWClient.AllowanceAsync"/>). What each sends, what it answers, and what is
    /// refused before anything is sent, on every kind of client.
    /// </summary>
    public class OWAskRouteTests
    {
        private const string Base = "https://example.test/api/v2";

        // An agent account client with its own name and a PIN: every header a write could carry, so an account route that
        // must leave one out is seen leaving it out (an account token sends its Bearer alone).
        private static OWClient Make(FakeTransport t, string key = "ow_a_acct", string pin = "1234", string world = null)
            => new OWClient(new OWClientConfig
            {
                ApiKey = key,
                ApiPin = pin,
                Transport = t,
                BaseUrl = Base,
                World = world,
                Agent = true,
                Client = "atlas",
            });

        private static string[] HeaderNames(OWRequest r) => r.Headers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

        private static readonly (string Name, Func<OWClient, Task> Run)[] Calls =
        {
            ("AskAsync", c => c.AskAsync("w1", "Who rules Ostvik?", new[] { "Ostvik: a harbour town." })),
            ("SortAsync", c => c.SortAsync("w1", new OWSortNote[] { "Mira runs the ferry." }, new OWSortRef[0])),
            ("AllowanceAsync", c => c.AllowanceAsync()),
        };

        // Kills: a world key, a read key, a legacy key or no key reaching the proxy, which meters per account; the
        // TypeScript SDK refuses each with a TypeError before sending.
        [Test]
        public void EveryAiCall_AnyKeyButAnAccountToken_IsRefused_NamingTheCall_NothingSent()
        {
            var clients = new (string Name, string Key, string World)[]
            {
                ("a write key", "ow_w_test", null), ("a read key", "ow_r_test", null), ("a legacy key", "0123456789", null),
                ("no key, World set", null, "w1"),
            };

            foreach (var call in Calls)
            {
                foreach (var (name, key, world) in clients)
                {
                    var t = new FakeTransport();
                    var e = Assert.ThrowsAsync<InvalidOperationException>(() => call.Run(Make(t, key, "1234", world)),
                        $"{call.Name} on {name}");
                    StringAssert.StartsWith(call.Name + " needs an account token (ow_a_)", e.Message, name);
                    StringAssert.EndsWith("Nothing was sent.", e.Message, name);
                    Assert.IsEmpty(t.Calls, $"{call.Name} on {name}: nothing sent");
                }
            }
        }

        // Kills: an AI route that adds world= (or refuses) on an account client with World set, or sends the PIN.
        [Test]
        public async Task EveryAiCall_OnAnAccountTokenWithWorld_SendsNoWorld_TheBearerAlone()
        {
            foreach (var call in Calls)
            {
                var t = new FakeTransport { ResponseBody = "{}" };
                await call.Run(Make(t, world: "w9"));
                Assert.AreEqual("", new Uri(t.Last.Url).Query, call.Name);
                Assert.AreEqual(new[] { "Authorization" }, HeaderNames(t.Last).Where(h => h != "Content-Type").ToArray(), call.Name);
                Assert.AreEqual("Bearer ow_a_acct", t.Last.Headers["Authorization"], call.Name);
            }
        }

        // -- AskAsync -----------------------------------------------------------------------------------------

        private const string AskAnswer =
            @"{""answer"":""Mira does."",""model"":""claude-haiku"",""stop_reason"":""end_turn"","
            + @"""usage"":{""input_tokens"":120,""output_tokens"":9},""remaining"":41,""tokens_remaining"":5871,"
            + @"""token_rating"":6000}";

        // The TypeScript SDK's ask() (4.13.0): POST /account/ask {world_id, question, context}, the Bearer alone, no
        // write header. Kills: the wrong verb or path, a key misnamed (worldId, world), the context's order changed, the
        // PIN or a write header sent, or the answer's meter misread.
        [Test]
        public async Task Ask_PostsAccountAsk_TheThreeKeys_TheBearerAlone_AndReadsTheAnswer()
        {
            var t = new FakeTransport { ResponseBody = AskAnswer };
            var answer = await Make(t).AskAsync("w 1", "Who rules Ostvik?", new List<string> { "b: two", "a: one" });

            var a = t.Calls.Single();
            Assert.AreEqual("POST", a.Method);
            Assert.AreEqual(Base + "/account/ask", a.Url, "the path, no query");
            CollectionAssert.AreEqual(new[] { "Authorization", "Content-Type" }, HeaderNames(a));
            Assert.AreEqual("Bearer ow_a_acct", a.Headers["Authorization"]);
            Assert.AreEqual(
                @"{""world_id"":""w 1"",""question"":""Who rules Ostvik?"",""context"":[""b: two"",""a: one""]}", a.Body,
                "the three keys, in keel's spelling, the context as given");

            Assert.AreEqual("Mira does.", answer.Answer);
            Assert.AreEqual("claude-haiku", answer.Model);
            Assert.AreEqual("end_turn", answer.StopReason);
            Assert.AreEqual(120, answer.Usage.InputTokens);
            Assert.AreEqual(9, answer.Usage.OutputTokens);
            Assert.AreEqual(41, answer.Remaining);
            Assert.AreEqual(5871, answer.TokensRemaining);
            Assert.AreEqual(6000, answer.TokenRating);
        }

        // Kills: a null world, question or context sent for keel to 422 (the TypeScript SDK's types require each).
        [Test]
        public void Ask_NoWorldQuestionOrContext_IsRefused_NothingSent()
        {
            var t = new FakeTransport();
            var c = Make(t);
            Assert.Throws<ArgumentException>(() => c.AskAsync(null, "q", new[] { "x" }));
            Assert.Throws<ArgumentException>(() => c.AskAsync("", "q", new[] { "x" }));
            Assert.Throws<ArgumentNullException>(() => c.AskAsync("w1", null, new[] { "x" }));
            Assert.Throws<ArgumentNullException>(() => c.AskAsync("w1", "q", null));
            Assert.IsEmpty(t.Calls);
        }

        // keel's refusals, none of which counts: the day's tokens used up (429 quota_exceeded, Retry-After to midnight),
        // the proxy switched off or paused (503 ask_unavailable), the model gave nothing (502 ask_failed). Kills: a helper
        // on the wrong status or code, or quota_exceeded or ask_unavailable read as worth retrying.
        [Test]
        public void Ask_KeelsRefusals_AreReadByTheirHelpers()
        {
            var t = new FakeTransport
            {
                Status = 429,
                ResponseBody = @"{""error"":{""type"":""rate_limited"",""code"":""quota_exceeded"",""message"":""Used up.""}}",
                ResponseHeaders = new Dictionary<string, string> { ["retry-after"] = "3600" },
            };
            var e = Assert.ThrowsAsync<OWApiError>(() => Make(t).AskAsync("w1", "q", new[] { "x" }));
            Assert.IsTrue(e.IsQuotaExceeded);
            Assert.IsFalse(e.IsRetryable);
            Assert.AreEqual(3600, e.RetryAfter);

            t = new FakeTransport { Status = 503, ResponseBody = @"{""error"":{""code"":""ask_unavailable"",""message"":""Off.""}}" };
            e = Assert.ThrowsAsync<OWApiError>(() => Make(t).AskAsync("w1", "q", new[] { "x" }));
            Assert.IsTrue(e.IsAskUnavailable);
            Assert.IsFalse(e.IsAskFailed);
            Assert.IsFalse(e.IsRetryable, "switched off, or paused for the day");

            t = new FakeTransport { Status = 502, ResponseBody = @"{""error"":{""code"":""ask_failed"",""message"":""No answer.""}}" };
            e = Assert.ThrowsAsync<OWApiError>(() => Make(t).AskAsync("w1", "q", new[] { "x" }));
            Assert.IsTrue(e.IsAskFailed);
            Assert.IsFalse(e.IsAskUnavailable);
            Assert.IsTrue(e.IsRetryable, "try again in a moment");

            t = new FakeTransport { Status = 500, ResponseBody = @"{""error"":{""code"":""ask_failed""}}" };
            e = Assert.ThrowsAsync<OWApiError>(() => Make(t).AskAsync("w1", "q", new[] { "x" }));
            Assert.IsFalse(e.IsAskFailed, "ask_failed is a 502");
        }

        // -- SortAsync ----------------------------------------------------------------------------------------

        private const string SortAnswer =
            @"{""rows"":["
            + @"{""kind"":""make"",""source_line"":""A new inn."",""proposal"":{""type"":""Location"",""name"":""The Gull"",""description"":""An inn.""}},"
            + @"{""kind"":""add_to"",""source_line"":""Mira is tall."",""target"":{""id"":""c1"",""type"":""Character"",""name"":""Mira"",""anchor"":""the ferrywoman""},"
            + @"""field"":""description"",""op"":""append"",""value"":""Tall.""},"
            + @"{""kind"":""link"",""source_line"":""Mira lives in Ostvik."",""target"":{""id"":""c1"",""type"":""Character"",""name"":""Mira""},"
            + @"""field"":""location"",""value_id"":""l1"",""value_name"":""Ostvik""},"
            + @"{""kind"":""ask"",""source_line"":""Tor drinks."",""question"":""Which Tor?"",""candidates"":["
            + @"{""id"":""c2"",""type"":""Character"",""name"":""Tor""},{""id"":""c3"",""type"":""Character"",""name"":""Tor"",""anchor"":""the smith""}],"
            + @"""then"":{""kind"":""add_to"",""field"":""description"",""op"":""append"",""value"":""Drinks.""}}],"
            + @"""not_proposed"":[{""source_line"":""Weather."",""reason"":""no element""}],"
            + @"""model"":""claude-haiku"",""stop_reason"":null,""usage"":{""input_tokens"":800,""output_tokens"":200},"
            + @"""remaining"":7,""tokens_remaining"":5000,""token_rating"":6000}";

        // The TypeScript SDK's sort() (4.18.0): POST /account/sort {world_id, notes, slice}, the Bearer alone. A note of
        // words alone goes as a string, one with a picture as {text?, image_url}; a slice row's anchor only when given.
        // Kills: a note always sent as an object (or as its words alone, losing the picture), an anchor sent as null, a key
        // misnamed, the order changed, or any of the four row kinds, its `then`, its candidates or not_proposed misread.
        [Test]
        public async Task Sort_PostsAccountSort_NotesAndTheSliceInKeelsShape_AndReadsEveryRowKind()
        {
            var t = new FakeTransport { ResponseBody = SortAnswer };
            var sorted = await Make(t).SortAsync("w1",
                new[]
                {
                    (OWSortNote)"Mira lives in Ostvik.",
                    new OWSortNote { ImageUrl = "https://media.onlyworlds.com/u/w1/n.png" },
                    new OWSortNote { Text = "Tor drinks.", ImageUrl = "https://media.onlyworlds.com/u/w1/t.png" },
                },
                new List<OWSortRef>
                {
                    new OWSortRef { Id = "c1", Type = "character", Name = "Mira", Anchor = "the ferrywoman" },
                    new OWSortRef { Id = "l1", Type = "location", Name = "Ostvik" },
                });

            var a = t.Calls.Single();
            Assert.AreEqual("POST", a.Method);
            Assert.AreEqual(Base + "/account/sort", a.Url, "the path, no query");
            CollectionAssert.AreEqual(new[] { "Authorization", "Content-Type" }, HeaderNames(a));
            Assert.AreEqual(
                @"{""world_id"":""w1"",""notes"":[""Mira lives in Ostvik."","
                + @"{""image_url"":""https://media.onlyworlds.com/u/w1/n.png""},"
                + @"{""text"":""Tor drinks."",""image_url"":""https://media.onlyworlds.com/u/w1/t.png""}],"
                + @"""slice"":[{""id"":""c1"",""type"":""character"",""name"":""Mira"",""anchor"":""the ferrywoman""},"
                + @"{""id"":""l1"",""type"":""location"",""name"":""Ostvik""}]}",
                a.Body);

            Assert.AreEqual(4, sorted.Rows.Count);
            var make = sorted.Rows[0];
            Assert.AreEqual(("make", "A new inn."), (make.Kind, make.SourceLine));
            Assert.AreEqual(("Location", "The Gull", "An inn."), (make.Proposal.Type, make.Proposal.Name, make.Proposal.Description));
            Assert.IsNull(make.Target);

            var addTo = sorted.Rows[1];
            Assert.AreEqual(("add_to", "description", "append", "Tall."), (addTo.Kind, addTo.Field, addTo.Op, addTo.Value));
            Assert.AreEqual(("c1", "Character", "Mira", "the ferrywoman"), (addTo.Target.Id, addTo.Target.Type, addTo.Target.Name, addTo.Target.Anchor));

            var link = sorted.Rows[2];
            Assert.AreEqual(("link", "location", "l1", "Ostvik"), (link.Kind, link.Field, link.ValueId, link.ValueName));
            Assert.IsNull(link.Target.Anchor);

            var ask = sorted.Rows[3];
            Assert.AreEqual(("ask", "Which Tor?"), (ask.Kind, ask.Question));
            Assert.AreEqual(new[] { "c2", "c3" }, ask.Candidates.Select(r => r.Id).ToArray());
            Assert.AreEqual("the smith", ask.Candidates[1].Anchor);
            Assert.AreEqual(("add_to", "description", "append", "Drinks."), (ask.Then.Kind, ask.Then.Field, ask.Then.Op, ask.Then.Value));

            Assert.AreEqual(("Weather.", "no element"), (sorted.NotProposed.Single().SourceLine, sorted.NotProposed.Single().Reason));
            Assert.IsNull(sorted.StopReason);
            Assert.AreEqual((800L, 200L), (sorted.Usage.InputTokens, sorted.Usage.OutputTokens));
            Assert.AreEqual((7, 5000L, 6000L), (sorted.Remaining, sorted.TokensRemaining, sorted.TokenRating));
        }

        // Kills: a null world, notes or slice sent, or a note with neither words nor a picture (keel's 422).
        [Test]
        public void Sort_NoWorldNotesOrSlice_OrAnEmptyNote_IsRefused_NothingSent()
        {
            var t = new FakeTransport();
            var c = Make(t);
            var notes = new OWSortNote[] { "x" };
            var slice = new OWSortRef[0];
            Assert.Throws<ArgumentException>(() => c.SortAsync("", notes, slice));
            Assert.Throws<ArgumentNullException>(() => c.SortAsync("w1", null, slice));
            Assert.Throws<ArgumentNullException>(() => c.SortAsync("w1", notes, null));
            var e = Assert.Throws<ArgumentException>(() => c.SortAsync("w1", new[] { new OWSortNote() }, slice));
            StringAssert.Contains("Nothing was sent", e.Message);
            Assert.Throws<ArgumentException>(() => c.SortAsync("w1", new OWSortNote[] { null }, slice));
            Assert.IsEmpty(t.Calls);
        }

        // -- AllowanceAsync -----------------------------------------------------------------------------------

        // The TypeScript SDK's allowance() (4.21.0): GET /account/allowance, no body, the Bearer alone. Spends nothing.
        // Kills: the wrong verb or path, a query, the PIN sent, or the meter misread (resets_at as served, never a local
        // time).
        [Test]
        public async Task Allowance_SendsGetAccountAllowance_TheBearerAlone_AndReadsTheMeter()
        {
            var t = new FakeTransport
            {
                ResponseBody = @"{""token_rating"":6000,""tokens_used_today"":6123,""tokens_remaining"":0,""resets_at"":""2026-11-01T00:00:00+00:00""}",
            };
            var meter = await Make(t).AllowanceAsync();

            var a = t.Calls.Single();
            Assert.AreEqual("GET", a.Method);
            Assert.AreEqual(Base + "/account/allowance", a.Url);
            Assert.IsNull(a.Body);
            CollectionAssert.AreEqual(new[] { "Authorization" }, HeaderNames(a));

            Assert.AreEqual(6000, meter.TokenRating);
            Assert.AreEqual(6123, meter.TokensUsedToday, "may pass the rating by the last call's size");
            Assert.AreEqual(0, meter.TokensRemaining);
            Assert.AreEqual("2026-11-01T00:00:00+00:00", meter.ResetsAt, "as served");
        }
    }
}
