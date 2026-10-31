using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The TypeScript SDK's media calls this client gained in its third tranche: an image's ticket, upload and removal
    /// (<see cref="OWClient.CreateMediaTicketAsync"/>, <see cref="OWClient.UploadImageAsync"/>,
    /// <see cref="OWClient.CreateRemovalTicketAsync"/>, <see cref="OWClient.RemoveImageAsync"/>). What each sends to keel,
    /// what the two-step calls send to the upload host (the ticket alone, never a credential of ours), what each answers,
    /// and what is refused before anything is sent.
    /// </summary>
    public class OWMediaRouteTests
    {
        private const string Base = "https://example.test/api/v2";
        private const string UploadUrl = "https://upload.example.test/v1/upload";
        private const string RemoveUrl = "https://upload.example.test/v1/remove";

        private const string Ticket =
            @"{""ticket"":""tkt.sig"",""upload_url"":""" + UploadUrl + @""",""prefix"":""u/w1/"",""max_bytes"":8,"
            + @"""exp"":1900000000,""uses"":1,""issued_to"":{""account"":""a1"",""membership"":null}}";

        private const string Uploaded =
            @"{""url"":""https://media.example.test/u/w1/x.png"",""key"":""u/w1/x.png"",""bytes"":4,""type"":""image/png"",""etag"":""e""}";

        private const string RemovalTicket =
            @"{""ticket"":""d1.sig"",""remove_url"":""" + RemoveUrl + @""",""exp"":1900000000,""key"":""u/w1/x.png"",""referenced"":2}";

        private const string Removed = @"{""removed"":""u/w1/x.png"",""bytes"":4,""cache"":""may_linger""}";

        private static readonly byte[] Png = { 0x89, 0x50, 0x4e, 0x47 };

        // An agent client with its own name and a PIN: every header a write could carry, so a call that must leave one
        // out is seen leaving it out.
        private static OWClient Make(FakeTransport t, string key = "ow_w_test", string pin = "1234", bool agent = true)
            => new OWClient(new OWClientConfig
            {
                ApiKey = key,
                ApiPin = pin,
                Transport = t,
                BaseUrl = Base,
                Agent = agent,
                Client = "atlas",
            });

        private static string[] HeaderNames(OWRequest r) => r.Headers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

        private static OWMediaTicket TicketOf(string body) => OWJson.Deserialize<OWMediaTicket>(body);

        /// <summary>Answers keel as <see cref="FakeTransport"/> does; a request to any other host fails with no answer.</summary>
        private sealed class UnreachableEdge : IOWTransport
        {
            public readonly FakeTransport Keel = new FakeTransport();

            public Task<OWResponse> SendAsync(OWRequest request, CancellationToken ct = default)
            {
                if (request.Url.StartsWith(Base + "/", StringComparison.Ordinal)) return Keel.SendAsync(request, ct);
                Keel.Calls.Add(request);
                throw new System.Net.WebException("connection refused");
            }
        }

        // -- CreateMediaTicketAsync -------------------------------------------------------------------------

        // The TypeScript SDK's createMediaTicket() (4.6.0): POST /media/ticket with the key and PIN and no body. keel's
        // document declares no header there, so an agent client sends neither write header. Kills: a body ({}), a write
        // header, the wrong path, a ticket key misread.
        [Test]
        public async Task CreateMediaTicket_PostsMediaTicket_NoBody_TheKeyAndPin_NoWriteHeader_AndReadsTheTicket()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = Ticket };
            var ticket = await Make(t).CreateMediaTicketAsync();

            var sent = t.Calls.Single();
            Assert.AreEqual("POST", sent.Method);
            Assert.AreEqual(Base + "/media/ticket", sent.Url);
            Assert.IsNull(sent.Body);
            Assert.IsNull(sent.BodyBytes);
            CollectionAssert.AreEqual(new[] { "API-Key", "API-Pin" }, HeaderNames(sent));
            Assert.AreEqual("ow_w_test", sent.Headers["API-Key"]);
            Assert.AreEqual("1234", sent.Headers["API-Pin"]);

            Assert.AreEqual("tkt.sig", ticket.Ticket);
            Assert.AreEqual(UploadUrl, ticket.UploadUrl);
            Assert.AreEqual("u/w1/", ticket.Prefix);
            Assert.AreEqual(8, ticket.MaxBytes);
            Assert.AreEqual(1900000000, ticket.Exp);
            Assert.AreEqual(1, ticket.Uses);
            Assert.AreEqual("a1", ticket.IssuedTo.Account);
            Assert.IsNull(ticket.IssuedTo.Membership);
        }

        // -- UploadImageAsync ----------------------------------------------------------------------------------

        // The TypeScript SDK's uploadImage(image, { key }) (4.6.0): the ticket from keel, then the bytes to the ticket's
        // upload_url, POST, with Authorization: Bearer <ticket> and X-Key alone. Kills: the key, the PIN, a write header or
        // a Content-Type sent to the edge; the bytes sent as text or not at all; the edge's answer misread.
        [Test]
        public async Task UploadImage_TicketFromKeel_ThenTheBytesToTheEdge_WithTheTicketAlone()
        {
            var t = new FakeTransport();
            t.Then(201, Ticket);
            t.Then(201, Uploaded);

            var image = await Make(t).UploadImageAsync(Png, key: "u/w1/x.png");

            Assert.AreEqual(2, t.Calls.Count);
            Assert.AreEqual(Base + "/media/ticket", t.Calls[0].Url);

            var edge = t.Calls[1];
            Assert.AreEqual("POST", edge.Method);
            Assert.AreEqual(UploadUrl, edge.Url);
            Assert.IsNull(edge.Body, "the bytes go as bytes, not as a JSON body");
            CollectionAssert.AreEqual(Png, edge.BodyBytes);
            CollectionAssert.AreEqual(new[] { "Authorization", "X-Key" }, HeaderNames(edge));
            Assert.AreEqual("Bearer tkt.sig", edge.Headers["Authorization"]);
            Assert.AreEqual("u/w1/x.png", edge.Headers["X-Key"]);

            Assert.AreEqual("https://media.example.test/u/w1/x.png", image.Url);
            Assert.AreEqual("u/w1/x.png", image.Key);
            Assert.AreEqual(4, image.Bytes);
            Assert.AreEqual("image/png", image.Type);
            Assert.AreEqual("e", image.Etag);
        }

        // Kills: keel asked for a ticket when one was passed; an X-Key sent with no key (or an empty one).
        [Test]
        public async Task UploadImage_ATicketPassed_IsUsed_KeelIsNotCalled_AndNoKeyMeansNoXKey()
        {
            foreach (var key in new[] { null, "" })
            {
                var t = new FakeTransport { Status = 201, ResponseBody = Uploaded };
                await Make(t).UploadImageAsync(Png, key: key, ticket: TicketOf(Ticket));

                var edge = t.Calls.Single();
                Assert.AreEqual(UploadUrl, edge.Url);
                CollectionAssert.AreEqual(new[] { "Authorization" }, HeaderNames(edge), $"key {key ?? "null"}");
            }
        }

        // npm: an image larger than the ticket's max_bytes throws 413 too_large before any byte is sent. Kills: the check
        // dropped, off by one (8 bytes fit an 8-byte ticket), or made after the edge was called.
        [Test]
        public async Task UploadImage_LargerThanMaxBytes_Is413TooLarge_NothingSentToTheEdge()
        {
            var t = new FakeTransport();
            var e = Assert.ThrowsAsync<OWApiError>(() => Make(t).UploadImageAsync(new byte[9], ticket: TicketOf(Ticket)));
            Assert.AreEqual(413, e.StatusCode);
            Assert.AreEqual("too_large", e.Code);
            StringAssert.Contains("Nothing was sent", e.Message);
            Assert.IsEmpty(t.Calls);

            // Fetched from keel: the ticket is asked for, the edge never.
            t.Then(201, Ticket);
            Assert.ThrowsAsync<OWApiError>(() => Make(t).UploadImageAsync(new byte[9]));
            Assert.AreEqual(new[] { Base + "/media/ticket" }, t.Calls.Select(c => c.Url).ToArray());

            // Exactly max_bytes goes.
            var fits = new FakeTransport { Status = 201, ResponseBody = Uploaded };
            await Make(fits).UploadImageAsync(new byte[8], ticket: TicketOf(Ticket));
            Assert.AreEqual(1, fits.Calls.Count);
        }

        // The edge answers {"error": "<code>"}, not keel's envelope; npm reads the string as the code. Kills: the code lost
        // (a bare 401 the caller cannot tell from a wrong key).
        [Test]
        public void UploadImage_TheEdgesRefusal_ArrivesAsOWApiError_WithItsCode()
        {
            var t = new FakeTransport();
            t.Then(201, Ticket);
            t.Then(401, @"{""error"":""ticket_used"",""note"":""one upload per ticket; request another""}");

            var e = Assert.ThrowsAsync<OWApiError>(() => Make(t).UploadImageAsync(Png));
            Assert.AreEqual(401, e.StatusCode);
            Assert.AreEqual("ticket_used", e.Code);
            Assert.IsFalse(e.IsAuthError, "a spent ticket is not a wrong key");
        }

        // Kills: a null image sent (or thrown on as a NullReferenceException after the ticket was spent from the quota).
        [Test]
        public void UploadImage_NoImage_IsRefused_NothingSent()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = Ticket };
            Assert.ThrowsAsync<ArgumentNullException>(() => Make(t).UploadImageAsync(null));
            Assert.IsEmpty(t.Calls);
        }

        // Unity's own refusal: a ticket with nowhere to go (no ticket, or no absolute upload_url) is not sent anywhere.
        // Kills: the Bearer sent to a relative or empty URL, or "Bearer " with no ticket.
        [Test]
        public void UploadImage_ATicketWithNoTicketOrNoUploadUrl_IsRefused_NothingSentToTheEdge()
        {
            foreach (var bad in new[]
            {
                new OWMediaTicket { Ticket = null, UploadUrl = UploadUrl, MaxBytes = 8 },
                new OWMediaTicket { Ticket = "", UploadUrl = UploadUrl, MaxBytes = 8 },
                new OWMediaTicket { Ticket = "tkt.sig", UploadUrl = null, MaxBytes = 8 },
                new OWMediaTicket { Ticket = "tkt.sig", UploadUrl = "/v1/upload", MaxBytes = 8 },
                new OWMediaTicket { Ticket = "tkt.sig", UploadUrl = "ftp://upload.example.test/v1/upload", MaxBytes = 8 },
            })
            {
                var t = new FakeTransport();
                var e = Assert.ThrowsAsync<ArgumentException>(() => Make(t).UploadImageAsync(Png, ticket: bad), bad.UploadUrl);
                StringAssert.Contains("Nothing was sent", e.Message);
                Assert.IsEmpty(t.Calls);
            }
        }

        // Kills: an edge that cannot be reached surfacing as anything but a transport error.
        [Test]
        public void UploadImage_AnEdgeThatCannotBeReached_IsAnOWTransportError()
        {
            var t = new UnreachableEdge();
            t.Keel.Then(201, Ticket);
            var c = new OWClient(new OWClientConfig { ApiKey = "ow_w_test", ApiPin = "1234", Transport = t, BaseUrl = Base });
            var e = Assert.ThrowsAsync<OWTransportError>(() => c.UploadImageAsync(Png));
            StringAssert.Contains("image upload", e.Message);
            StringAssert.Contains("upload.example.test", e.Message, "names the host it could not reach");
            Assert.AreEqual(2, t.Keel.Calls.Count);
        }

        // -- CreateRemovalTicketAsync -----------------------------------------------------------------------

        // The TypeScript SDK's createRemovalTicket(key) (4.8.0): POST /media/remove-ticket {key}, the key and PIN; a whole
        // image_url is cut to its key (the path after the host, unescaped). Kills: the URL sent whole, its leading slash
        // kept, its escapes kept, a write header sent (keel declares none there).
        [Test]
        public async Task CreateRemovalTicket_PostsRemoveTicket_WithTheKey_AWholeImageUrlIsCutToItsKey()
        {
            var t = new FakeTransport { ResponseBody = RemovalTicket };
            var c = Make(t);

            var ticket = await c.CreateRemovalTicketAsync("u/w1/x.png");
            await c.CreateRemovalTicketAsync("https://media.example.test/u/w1/x.png");
            await c.CreateRemovalTicketAsync("HTTP://media.example.test//u/w1/x.png");
            await c.CreateRemovalTicketAsync("https://media.example.test/u/w1/a%20b.png");

            var keys = t.Calls.Select(r => (string)JObject.Parse(r.Body)["key"]).ToArray();
            CollectionAssert.AreEqual(new[] { "u/w1/x.png", "u/w1/x.png", "u/w1/x.png", "u/w1/a b.png" }, keys);

            foreach (var sent in t.Calls)
            {
                Assert.AreEqual("POST", sent.Method);
                Assert.AreEqual(Base + "/media/remove-ticket", sent.Url);
                CollectionAssert.AreEqual(new[] { "key" }, JObject.Parse(sent.Body).Properties().Select(p => p.Name).ToArray());
                CollectionAssert.AreEqual(new[] { "API-Key", "API-Pin", "Content-Type" }, HeaderNames(sent));
            }

            Assert.AreEqual("d1.sig", ticket.Ticket);
            Assert.AreEqual(RemoveUrl, ticket.RemoveUrl);
            Assert.AreEqual(1900000000, ticket.Exp);
            Assert.AreEqual("u/w1/x.png", ticket.Key);
            Assert.AreEqual(2, ticket.Referenced);
        }

        // Kills: an empty key sent, which keel answers 422 (param key).
        [Test]
        public void CreateRemovalTicket_NoKey_IsRefused_NothingSent()
        {
            foreach (var key in new[] { null, "" })
            {
                var t = new FakeTransport();
                var e = Assert.ThrowsAsync<ArgumentException>(() => Make(t).CreateRemovalTicketAsync(key));
                StringAssert.Contains("Nothing was sent", e.Message);
                Assert.IsEmpty(t.Calls);
            }
        }

        // -- RemoveImageAsync ------------------------------------------------------------------------------------

        // The TypeScript SDK's removeImage(keyOrUrl) (4.8.0): the removal ticket from keel, then POST to the ticket's
        // remove_url with Authorization: Bearer <ticket> alone and no body. Kills: the key or PIN sent to the edge, a body
        // sent, the answer misread.
        [Test]
        public async Task RemoveImage_ATicketFromKeel_ThenTheRemovalAtTheEdge_WithTheTicketAlone()
        {
            var t = new FakeTransport();
            t.Then(200, RemovalTicket);
            t.Then(200, Removed);

            var removed = await Make(t).RemoveImageAsync("https://media.example.test/u/w1/x.png");

            Assert.AreEqual(2, t.Calls.Count);
            Assert.AreEqual("u/w1/x.png", (string)JObject.Parse(t.Calls[0].Body)["key"]);

            var edge = t.Calls[1];
            Assert.AreEqual("POST", edge.Method);
            Assert.AreEqual(RemoveUrl, edge.Url);
            Assert.IsNull(edge.Body);
            Assert.IsNull(edge.BodyBytes);
            CollectionAssert.AreEqual(new[] { "Authorization" }, HeaderNames(edge));
            Assert.AreEqual("Bearer d1.sig", edge.Headers["Authorization"]);

            Assert.AreEqual("u/w1/x.png", removed.Removed);
            Assert.AreEqual(4, removed.Bytes);
            Assert.AreEqual("may_linger", removed.Cache);
        }

        // Kills: keel asked for a removal ticket when one was passed.
        [Test]
        public async Task RemoveImage_ATicketPassed_IsUsed_KeelIsNotCalled()
        {
            var t = new FakeTransport { ResponseBody = Removed };
            await Make(t).RemoveImageAsync("u/w1/x.png", ticket: OWJson.Deserialize<OWRemovalTicket>(RemovalTicket));
            Assert.AreEqual(new[] { RemoveUrl }, t.Calls.Select(c => c.Url).ToArray());
        }

        // Kills: keel's envelope or the edge's bare code read wrongly; a refused ticket going on to the edge.
        [Test]
        public void RemoveImage_KeelsRefusalAndTheEdgesArriveAsOWApiErrors_WithTheirCodes()
        {
            var keel = new FakeTransport
            {
                Status = 409,
                ResponseBody = @"{""error"":{""type"":""invalid_request"",""code"":""upload_pending"",""message"":""retry""}}",
            };
            var e = Assert.ThrowsAsync<OWApiError>(() => Make(keel).RemoveImageAsync("u/w1/x.png"));
            Assert.AreEqual(409, e.StatusCode);
            Assert.AreEqual("upload_pending", e.Code);
            Assert.IsTrue(e.IsRetryable);
            Assert.AreEqual(1, keel.Calls.Count, "the edge is never called");

            var edge = new FakeTransport();
            edge.Then(200, RemovalTicket);
            edge.Then(403, @"{""error"":""not_removable""}");
            e = Assert.ThrowsAsync<OWApiError>(() => Make(edge).RemoveImageAsync("u/w1/x.png"));
            Assert.AreEqual(403, e.StatusCode);
            Assert.AreEqual("not_removable", e.Code);
        }

        // Unity's own refusal, as for the upload.
        [Test]
        public void RemoveImage_ATicketWithNoTicketOrNoRemoveUrl_IsRefused_NothingSentToTheEdge()
        {
            foreach (var bad in new[]
            {
                new OWRemovalTicket { Ticket = null, RemoveUrl = RemoveUrl },
                new OWRemovalTicket { Ticket = "d1.sig", RemoveUrl = "" },
            })
            {
                var t = new FakeTransport();
                Assert.ThrowsAsync<ArgumentException>(() => Make(t).RemoveImageAsync("u/w1/x.png", ticket: bad));
                Assert.IsEmpty(t.Calls);
            }
        }

        // -- The ticket is a secret ---------------------------------------------------------------------------

        // A ticket is the upload's or the removal's credential: it never reaches an exception's text, whether keel's answer
        // is malformed, the edge refuses, the edge cannot be reached or the image is too large. Kills: a message that
        // quotes the ticket, the body that carries it, or the Authorization header.
        [Test]
        public void ATicket_NeverReachesAnExceptionMessage()
        {
            const string secret = "ow_tk_leakme";
            var ticket = new OWMediaTicket { Ticket = secret, UploadUrl = UploadUrl, MaxBytes = 2 };

            var malformed = new FakeTransport { Status = 201, ResponseBody = @"{""ticket"":""" + secret + @""",""upload_url"":" };
            var errors = new List<Exception>
            {
                Assert.CatchAsync<Exception>(() => Make(malformed).CreateMediaTicketAsync()),
                Assert.CatchAsync<Exception>(() => Make(malformed).UploadImageAsync(Png)),
                Assert.CatchAsync<Exception>(() => Make(new FakeTransport()).UploadImageAsync(Png, ticket: ticket)),
                Assert.CatchAsync<Exception>(() => Make(new FakeTransport { Status = 401, ResponseBody = @"{""error"":""ticket_invalid""}" })
                    .UploadImageAsync(new byte[1], ticket: ticket)),
                Assert.CatchAsync<Exception>(() => new OWClient(new OWClientConfig { ApiKey = "ow_w_test", Transport = new UnreachableEdge(), BaseUrl = Base })
                    .UploadImageAsync(new byte[1], ticket: ticket)),
                Assert.CatchAsync<Exception>(() => Make(new FakeTransport { Status = 502, ResponseBody = "<html>bad gateway</html>" })
                    .RemoveImageAsync("u/w1/x.png", ticket: new OWRemovalTicket { Ticket = secret, RemoveUrl = RemoveUrl })),
            };

            foreach (var e in errors)
            {
                Assert.IsNotNull(e);
                StringAssert.DoesNotContain("leakme", e.ToString(), e.GetType().Name);
            }
        }
    }
}
