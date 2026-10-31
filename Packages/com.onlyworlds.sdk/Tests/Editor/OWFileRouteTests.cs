using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The TypeScript SDK's files-on-elements calls this client gained in its third tranche: an element's files and the
    /// world's (<see cref="OWClient.ListFilesAsync"/>, <see cref="OWClient.WorldFilesAsync"/>), a file's ticket and its
    /// upload (<see cref="OWClient.CreateFileTicketAsync"/>, <see cref="OWClient.UploadFileAsync"/>), its download link and
    /// its removal (<see cref="OWClient.FileDownloadLinkAsync"/>, <see cref="OWClient.RemoveFileAsync"/>), and the kind
    /// read from a name (<see cref="OWFileKinds"/>). What each sends to keel, what the upload sends to the upload host
    /// (the ticket alone), what each answers, and what is refused before anything is sent.
    /// </summary>
    public class OWFileRouteTests
    {
        private const string Base = "https://example.test/api/v2";
        private const string UploadUrl = "https://upload.example.test/v1/file";
        private const string El = "018f0000-0000-7000-8000-0000000000e1";
        private const string Fid = "018f0000-0000-7000-8000-0000000000f1";

        // An id that is one segment but not a uuid (dots, an underscore, a dash, capitals), sent as given. An id that is
        // not one segment is refused before sending (OWPathSegmentTests).
        private const string OddId = "a.b_c-D9";
        private const string OddIdEscaped = "a.b_c-D9";

        private const string Row =
            @"{""id"":""" + Fid + @""",""name"":""Map notes.pdf"",""kind"":""pdf"",""size"":5,""label"":""description"","
            + @"""added_by"":{""name"":""Ada"",""membership"":null,""agent"":false},""added_at"":""2026-10-19T18:00:00Z""}";

        private const string WorldFiles =
            @"{""data"":[{""id"":""" + Fid + @""",""name"":""Map notes.pdf"",""kind"":""pdf"",""size"":5,""label"":null,"
            + @"""added_by"":{""name"":""Scout"",""membership"":""m1"",""agent"":true},""added_at"":""2026-10-19T18:00:00Z"","
            + @"""element_type"":""location"",""element_id"":""" + El + @"""}],""used_bytes"":42991616,""cap_bytes"":262144000}";

        private const string Ticket =
            @"{""ticket"":""f1.sig"",""upload_url"":""" + UploadUrl + @""",""prefix"":""w/w1/" + El + @"/"","
            + @"""key"":""w/w1/" + El + "/" + Fid + @".pdf"",""max_bytes"":8,""exp"":1900000000,""uses"":1,"
            + @"""issued_to"":{""account"":""a1"",""membership"":null},""kind"":""file"",""file_id"":""" + Fid + @"""}";

        private const string Stored =
            @"{""key"":""w/w1/" + El + "/" + Fid + @".pdf"",""fid"":""" + Fid + @""",""bytes"":5,""type"":""application/pdf"",""etag"":""e""}";

        private const string Link =
            @"{""url"":""https://files.example.test/v1/get?t=g1.sig"",""exp"":1900000300,""name"":""Map notes.pdf"",""kind"":""pdf"",""size"":5}";

        private static readonly byte[] Pdf = { 0x25, 0x50, 0x44, 0x46, 0x2d }; // %PDF-

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

        private static string[] BodyKeys(OWRequest r) => JObject.Parse(r.Body).Properties().Select(p => p.Name).ToArray();

        private static FakeTransport Uploading()
        {
            var t = new FakeTransport();
            t.Then(201, Ticket);
            t.Then(201, Stored);
            return t;
        }

        // -- ListFilesAsync / WorldFilesAsync -----------------------------------------------------------------

        // The TypeScript SDK's listFiles(type, id) (4.32.0): GET /{type}/{id}/files with the key (a read: no PIN), the rows of
        // keel's { data }. Kills: the id unescaped, the PIN or a write header sent, a row's key misread, an answer with no
        // data thrown on.
        [Test]
        public async Task ListFiles_GetsTheElementsFiles_IdEscaped_TheKeyAlone_AndReadsTheRows()
        {
            var t = new FakeTransport { ResponseBody = @"{""data"":[" + Row + "]}" };
            var rows = await Make(t).ListFilesAsync("location", OddId);

            var sent = t.Calls.Single();
            Assert.AreEqual("GET", sent.Method);
            Assert.AreEqual($"{Base}/location/{OddIdEscaped}/files", sent.Url);
            Assert.IsNull(sent.Body);
            CollectionAssert.AreEqual(new[] { "API-Key" }, HeaderNames(sent));

            var row = rows.Single();
            Assert.AreEqual(Fid, row.Id);
            Assert.AreEqual("Map notes.pdf", row.Name);
            Assert.AreEqual("pdf", row.Kind);
            Assert.AreEqual(5, row.Size);
            Assert.AreEqual("description", row.Label);
            Assert.AreEqual("Ada", row.AddedBy.Name);
            Assert.IsNull(row.AddedBy.Membership);
            Assert.IsFalse(row.AddedBy.Agent);
            Assert.AreEqual("2026-10-19T18:00:00Z", row.AddedAt);

            var empty = new FakeTransport { ResponseBody = "{}" };
            CollectionAssert.IsEmpty(await Make(empty).ListFilesAsync("location", El));
        }

        // The TypeScript SDK's worldFiles() (4.32.0): GET /world/files, the rows with their element, and the world's total.
        [Test]
        public async Task WorldFiles_GetsWorldFiles_TheKeyAlone_AndReadsTheRowsAndTheTotal()
        {
            var t = new FakeTransport { ResponseBody = WorldFiles };
            var files = await Make(t).WorldFilesAsync();

            var sent = t.Calls.Single();
            Assert.AreEqual("GET", sent.Method);
            Assert.AreEqual(Base + "/world/files", sent.Url);
            CollectionAssert.AreEqual(new[] { "API-Key" }, HeaderNames(sent));

            Assert.AreEqual(42991616, files.UsedBytes);
            Assert.AreEqual(262144000, files.CapBytes);
            var row = files.Data.Single();
            Assert.AreEqual("location", row.ElementType);
            Assert.AreEqual(El, row.ElementId);
            Assert.IsNull(row.Label);
            Assert.AreEqual("m1", row.AddedBy.Membership);
            Assert.IsTrue(row.AddedBy.Agent);

            var none = await Make(new FakeTransport { ResponseBody = @"{""used_bytes"":0,""cap_bytes"":1}" }).WorldFilesAsync();
            CollectionAssert.IsEmpty(none.Data);
        }

        // -- CreateFileTicketAsync --------------------------------------------------------------------------------

        // The TypeScript SDK's createFileTicket(type, id, { name, size, label }) (4.32.0; X-OW-Agent from 4.33.1): POST
        // /{type}/{id}/files {name, kind, size, label?}, the key and PIN, no Idempotency-Key (keel's rule), X-OW-Agent on an
        // agent client (the one files route keel's document lists it on) and no X-OW-Client. Kills: the kind not read, the
        // label sent as null when not given, an Idempotency-Key, X-OW-Client sent, X-OW-Agent dropped, the id unescaped.
        [Test]
        public async Task CreateFileTicket_PostsFiles_NameKindSizeLabel_KeyAndPin_AgentDeclared_NoClientName_NoIdempotencyKey()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = Ticket };
            var c = Make(t);

            var ticket = await c.CreateFileTicketAsync("location", OddId, "Map notes.pdf", 5, label: "description");
            await c.CreateFileTicketAsync("location", OddId, "Map notes.pdf", 5);

            foreach (var sent in t.Calls)
            {
                Assert.AreEqual("POST", sent.Method);
                Assert.AreEqual($"{Base}/location/{OddIdEscaped}/files", sent.Url);
                CollectionAssert.AreEqual(new[] { "API-Key", "API-Pin", "Content-Type", "X-OW-Agent" }, HeaderNames(sent));
            }

            Assert.AreEqual(@"{""name"":""Map notes.pdf"",""kind"":""pdf"",""size"":5,""label"":""description""}", t.Calls[0].Body);
            Assert.AreEqual(@"{""name"":""Map notes.pdf"",""kind"":""pdf"",""size"":5}", t.Calls[1].Body);

            Assert.AreEqual("f1.sig", ticket.Ticket);
            Assert.AreEqual(UploadUrl, ticket.UploadUrl);
            Assert.AreEqual($"w/w1/{El}/", ticket.Prefix);
            Assert.AreEqual($"w/w1/{El}/{Fid}.pdf", ticket.Key);
            Assert.AreEqual(8, ticket.MaxBytes);
            Assert.AreEqual(1900000000, ticket.Exp);
            Assert.AreEqual(1, ticket.Uses);
            Assert.AreEqual("a1", ticket.IssuedTo.Account);
            Assert.AreEqual("file", ticket.Kind);
            Assert.AreEqual(Fid, ticket.FileId);

            // A person's client declares nothing.
            var plain = new FakeTransport { Status = 201, ResponseBody = Ticket };
            await Make(plain, agent: false).CreateFileTicketAsync("location", El, "a.pdf", 1);
            CollectionAssert.AreEqual(new[] { "API-Key", "API-Pin", "Content-Type" }, HeaderNames(plain.Last));
        }

        // npm's fileKindOf, keel's NAME_EXTS: the last path part, what follows its last dot (with something before it),
        // trimmed and lower-cased; .yml is yaml, .markdown is md. The name goes as given: keel cleans it. Kills: case kept,
        // a folder's dot read, .yml or .markdown missed, the name sent cleaned.
        [Test]
        public async Task TheKind_IsReadFromTheName_AsKeelReadsIt_AndTheNameGoesAsGiven()
        {
            var cases = new[]
            {
                ("rules.yml", "yaml"), ("rules.yaml", "yaml"), ("README.markdown", "md"), ("notes.md", "md"),
                ("MAP.PDF", "pdf"), ("a.tar.zip", "zip"), (@"C:\worlds\lore.txt", "txt"), ("data/table.csv", "csv"),
                ("x.json", "json"), ("Cargo.toml", "toml"), ("v1.2/notes.md", "md"),
            };

            var t = new FakeTransport { Status = 201, ResponseBody = Ticket };
            var c = Make(t);
            foreach (var (name, _) in cases) await c.CreateFileTicketAsync("character", El, name, 1);

            CollectionAssert.AreEqual(cases.Select(k => k.Item2).ToArray(), t.Calls.Select(r => (string)JObject.Parse(r.Body)["kind"]).ToArray());
            Assert.AreEqual(@"C:\worlds\lore.txt", (string)JObject.Parse(t.Calls[6].Body)["name"]);

            foreach (var (name, kind) in cases) Assert.AreEqual(kind, OWFileKinds.Of(name), name);
            CollectionAssert.AreEquivalent(
                new[] { "pdf", "txt", "md", "markdown", "json", "csv", "yaml", "yml", "toml", "zip" }, OWFileKinds.Extensions.Keys);
        }

        // Kills: a kind passed overridden by the name.
        [Test]
        public async Task AKindPassed_IsSentAsPassed_WithNoReadingOfTheName()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = Ticket };
            await Make(t).CreateFileTicketAsync("character", El, "odd.bin", 1, kind: "txt");
            Assert.AreEqual("txt", (string)t.LastBody["kind"]);
        }

        // npm: a name with no extension keel takes is a TypeError naming the ones it takes, before any request; so is an
        // empty name. Kills: such a name sent, or the extensions not named.
        [Test]
        public void ANameKeelTakesNoFileWith_IsRefused_NamingTheExtensions_NothingSent()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = Ticket };
            var c = Make(t);

            foreach (var name in new[] { "report.docx", "noextension", ".pdf", "trailing.", "image.png", "folder.pdf/notes", "folder/.pdf" })
            {
                Assert.IsNull(OWFileKinds.Of(name), name);
                var e = Assert.ThrowsAsync<ArgumentException>(() => c.CreateFileTicketAsync("character", El, name, 1), name);
                StringAssert.IsMatch(@"\.pdf .*\.yml .*\.zip", e.Message, name);
                StringAssert.Contains("Nothing was sent", e.Message, name);
                Assert.ThrowsAsync<ArgumentException>(() => c.UploadFileAsync("character", El, Pdf, name: name), name);
            }

            foreach (var name in new[] { null, "", "   " })
            {
                var e = Assert.ThrowsAsync<ArgumentException>(() => c.CreateFileTicketAsync("character", El, name, 1), name ?? "null");
                StringAssert.Contains("needs the file's name", e.Message, name ?? "null");
            }

            Assert.IsEmpty(t.Calls);
        }

        // -- FileDownloadLinkAsync / RemoveFileAsync ------------------------------------------------------------

        // The TypeScript SDK's fileDownloadLink(type, id, fileId) (4.32.0): GET /{type}/{id}/files/{file_id}/download, a read.
        [Test]
        public async Task FileDownloadLink_GetsDownload_IdsEscaped_TheKeyAlone_AndReadsTheLink()
        {
            var t = new FakeTransport { ResponseBody = Link };
            var link = await Make(t).FileDownloadLinkAsync("location", OddId, "f.1");

            var sent = t.Calls.Single();
            Assert.AreEqual("GET", sent.Method);
            Assert.AreEqual($"{Base}/location/{OddIdEscaped}/files/f.1/download", sent.Url);
            CollectionAssert.AreEqual(new[] { "API-Key" }, HeaderNames(sent));

            Assert.AreEqual("https://files.example.test/v1/get?t=g1.sig", link.Url);
            Assert.AreEqual(1900000300, link.Exp);
            Assert.AreEqual("Map notes.pdf", link.Name);
            Assert.AreEqual("pdf", link.Kind);
            Assert.AreEqual(5, link.Size);
        }

        // The TypeScript SDK's removeFile(type, id, fileId) (4.32.0): DELETE /{type}/{id}/files/{file_id}, the key and PIN,
        // no write header; a 204 is nothing. Kills: the element deleted instead (the path cut short), a body, a header.
        [Test]
        public async Task RemoveFile_DeletesTheFile_IdsEscaped_KeyAndPin_NoWriteHeader_A204IsNothing()
        {
            var t = new FakeTransport { Status = 204, ResponseBody = "" };
            await Make(t).RemoveFileAsync("location", OddId, Fid);

            var sent = t.Calls.Single();
            Assert.AreEqual("DELETE", sent.Method);
            Assert.AreEqual($"{Base}/location/{OddIdEscaped}/files/{Fid}", sent.Url);
            Assert.IsNull(sent.Body);
            CollectionAssert.AreEqual(new[] { "API-Key", "API-Pin" }, HeaderNames(sent));
        }

        // npm: a key that is neither the owner's nor the uploader's is a 403 permission_error.
        [Test]
        public void RemoveFile_NeitherOwnerNorUploader_IsA403PermissionError()
        {
            var t = new FakeTransport
            {
                Status = 403,
                ResponseBody = @"{""error"":{""type"":""permission_error"",""code"":""permission_error"",""message"":""not yours""}}",
            };
            var e = Assert.ThrowsAsync<OWApiError>(() => Make(t).RemoveFileAsync("location", El, Fid));
            Assert.AreEqual(403, e.StatusCode);
            Assert.AreEqual("permission_error", e.Type);
        }

        // -- UploadFileAsync -------------------------------------------------------------------------------------

        // The TypeScript SDK's uploadFile(type, id, file, { name, label }) (4.32.0): the ticket with the byte length as
        // size, then the raw bytes to upload_url, POST, with Authorization: Bearer <ticket> alone. Kills: the size not the
        // byte length, the key or PIN or X-OW-Agent sent to the host, the bytes sent as text.
        [Test]
        public async Task UploadFile_TheTicketWithTheByteLength_ThenTheRawBytesToTheHost_WithTheTicketAlone()
        {
            var t = Uploading();
            var stored = await Make(t).UploadFileAsync("location", El, Pdf, name: "Map notes.pdf", label: "description");

            Assert.AreEqual(2, t.Calls.Count);
            Assert.AreEqual(@"{""name"":""Map notes.pdf"",""kind"":""pdf"",""size"":5,""label"":""description""}", t.Calls[0].Body);
            Assert.IsTrue(t.Calls[0].Headers.ContainsKey("X-OW-Agent"), "the ticket declares the agent");

            var host = t.Calls[1];
            Assert.AreEqual("POST", host.Method);
            Assert.AreEqual(UploadUrl, host.Url);
            Assert.IsNull(host.Body);
            CollectionAssert.AreEqual(Pdf, host.BodyBytes);
            CollectionAssert.AreEqual(new[] { "Authorization" }, HeaderNames(host));
            Assert.AreEqual("Bearer f1.sig", host.Headers["Authorization"]);

            Assert.AreEqual($"w/w1/{El}/{Fid}.pdf", stored.Key);
            Assert.AreEqual(Fid, stored.Fid);
            Assert.AreEqual(5, stored.Bytes);
            Assert.AreEqual("application/pdf", stored.Type);
            Assert.AreEqual("e", stored.Etag);
        }

        // Kills: keel asked for a ticket when one was passed, or a name demanded with it.
        [Test]
        public async Task UploadFile_ATicketPassed_IsUsed_KeelIsNotCalled_AndNoNameIsNeeded()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = Stored };
            await Make(t).UploadFileAsync("location", El, Pdf, ticket: OWJson.Deserialize<OWFileTicket>(Ticket));
            Assert.AreEqual(new[] { UploadUrl }, t.Calls.Select(c => c.Url).ToArray());
        }

        // Kills: bytes with no name sent to keel (which cannot tell their kind), or a null file thrown on late.
        [Test]
        public void UploadFile_NoNameAndNoTicket_OrNoFile_IsRefused_NothingSent()
        {
            var t = Uploading();
            var e = Assert.ThrowsAsync<ArgumentException>(() => Make(t).UploadFileAsync("location", El, Pdf));
            StringAssert.StartsWith("UploadFileAsync needs the file's name", e.Message);
            StringAssert.Contains("or a ticket", e.Message, "says the other way to call it");
            StringAssert.Contains("Nothing was sent", e.Message);
            Assert.ThrowsAsync<ArgumentNullException>(() => Make(t).UploadFileAsync("location", El, null, name: "a.pdf"));
            Assert.IsEmpty(t.Calls);
        }

        // npm: a file larger than the ticket's max_bytes throws 413 too_large before any byte is sent.
        [Test]
        public void UploadFile_LargerThanMaxBytes_Is413TooLarge_NothingSentToTheHost()
        {
            var t = new FakeTransport();
            var e = Assert.ThrowsAsync<OWApiError>(() => Make(t).UploadFileAsync("location", El, new byte[9],
                ticket: OWJson.Deserialize<OWFileTicket>(Ticket)));
            Assert.AreEqual(413, e.StatusCode);
            Assert.AreEqual("too_large", e.Code);
            Assert.IsFalse(e.IsFileTooLarge, "the host's too_large, not keel's file_too_large");
            Assert.IsEmpty(t.Calls);
        }

        // keel's refusals of the ticket, each with its helper; the host is never called. Kills: a helper on the wrong
        // status or code, files_full read as not_author, the host called after keel refused.
        [Test]
        public void KeelsRefusals_ArriveWithTheirHelpers_AndTheHostIsNeverCalled()
        {
            var big = new FakeTransport
            {
                Status = 413,
                ResponseBody = @"{""error"":{""type"":""invalid_request"",""code"":""file_too_large"",""param"":""size"",""message"":""over 25 MB""}}",
            };
            var e = Assert.ThrowsAsync<OWApiError>(() => Make(big).UploadFileAsync("location", El, Pdf, name: "big.pdf"));
            Assert.IsTrue(e.IsFileTooLarge);
            Assert.AreEqual("size", e.Param);
            Assert.AreEqual(1, big.Calls.Count);

            var quota = new FakeTransport
            {
                Status = 429,
                ResponseBody = @"{""error"":{""type"":""rate_limit_error"",""code"":""quota_exceeded"",""message"":""200 a day""}}",
                ResponseHeaders = new Dictionary<string, string> { ["retry-after"] = "3600" },
            };
            e = Assert.ThrowsAsync<OWApiError>(() => Make(quota).UploadFileAsync("location", El, Pdf, name: "a.pdf"));
            Assert.IsTrue(e.IsQuotaExceeded);
            Assert.AreEqual(3600, e.RetryAfter);
            Assert.IsFalse(e.IsRetryable);
            Assert.AreEqual(1, quota.Calls.Count);

            var full = new FakeTransport
            {
                Status = 403,
                ResponseBody = @"{""error"":{""type"":""permission_error"",""code"":""files_full"",""message"":""250 MB""}}",
            };
            e = Assert.ThrowsAsync<OWApiError>(() => Make(full).UploadFileAsync("location", El, Pdf, name: "a.pdf"));
            Assert.IsTrue(e.IsFilesFull);
            Assert.IsFalse(e.IsNotAuthor);
            Assert.AreEqual(1, full.Calls.Count);
        }

        // npm: the host's 409 exists and 415 unsupported_type arrive as OwApiError with their codes.
        [Test]
        public void TheHostsRefusals_ArriveAsOWApiErrors_WithTheirCodes()
        {
            foreach (var (status, code) in new[] { (409, "exists"), (415, "unsupported_type"), (401, "ticket_expired") })
            {
                var t = new FakeTransport();
                t.Then(201, Ticket);
                t.Then(status, @"{""error"":""" + code + @"""}");
                var e = Assert.ThrowsAsync<OWApiError>(() => Make(t).UploadFileAsync("location", El, Pdf, name: "a.pdf"));
                Assert.AreEqual(status, e.StatusCode, code);
                Assert.AreEqual(code, e.Code);
            }
        }

        // npm without XMLHttpRequest: onProgress is called once, (total, total), after the host's 201, and never on a
        // failure. Kills: called before the send, with the wrong numbers, or on a refused upload.
        [Test]
        public async Task OnProgress_IsCalledOnce_WithTheTotal_AfterSuccess_AndNeverOnAFailure()
        {
            var seen = new List<(long, long)>();
            await Make(Uploading()).UploadFileAsync("location", El, Pdf, name: "a.pdf", onProgress: (l, n) => seen.Add((l, n)));
            CollectionAssert.AreEqual(new[] { (5L, 5L) }, seen);

            var failed = new FakeTransport();
            failed.Then(201, Ticket);
            failed.Then(502, @"{""error"":""write_failed""}");
            var none = new List<(long, long)>();
            Assert.ThrowsAsync<OWApiError>(() => Make(failed).UploadFileAsync("location", El, Pdf, name: "a.pdf",
                onProgress: (l, n) => none.Add((l, n))));
            CollectionAssert.IsEmpty(none);
        }

        // npm 4.33.1: an agent client sends X-OW-Agent on the ticket alone, never on the other files routes or to the host.
        [Test]
        public async Task Agent_DeclaresItself_OnTheTicketAlone()
        {
            var t = new FakeTransport { ResponseBody = @"{""data"":[]}" };
            var c = Make(t);
            await c.ListFilesAsync("location", El);
            await c.WorldFilesAsync();
            t.Then(201, Ticket);
            t.Then(201, Stored);
            await c.UploadFileAsync("location", El, Pdf, name: "a.pdf");
            t.ResponseBody = Link;
            await c.FileDownloadLinkAsync("location", El, Fid);
            t.Status = 204;
            t.ResponseBody = "";
            await c.RemoveFileAsync("location", El, Fid);

            Assert.AreEqual(6, t.Calls.Count);
            foreach (var sent in t.Calls)
            {
                var ticket = sent.Method == "POST" && sent.Url.StartsWith(Base, StringComparison.Ordinal);
                Assert.AreEqual(ticket, sent.Headers.ContainsKey("X-OW-Agent"), $"{sent.Method} {sent.Url}");
                Assert.IsFalse(sent.Headers.ContainsKey("X-OW-Client"), $"{sent.Method} {sent.Url}");
            }
        }

        // -- Secrets ------------------------------------------------------------------------------------------

        // The ticket and the download link are credentials: neither reaches an exception's text. Kills: a malformed
        // answer's body, a refusal's message or the 413's text quoting either.
        [Test]
        public void ATicketOrADownloadLink_NeverReachesAnExceptionMessage()
        {
            var ticket = new OWFileTicket { Ticket = "f1.leakme", UploadUrl = UploadUrl, MaxBytes = 2 };
            var errors = new List<Exception>
            {
                Assert.CatchAsync<Exception>(() => Make(new FakeTransport { ResponseBody = @"{""url"":""https://files.example.test/get?t=leakme"",""exp"":" })
                    .FileDownloadLinkAsync("location", El, Fid)),
                Assert.CatchAsync<Exception>(() => Make(new FakeTransport { Status = 201, ResponseBody = @"{""ticket"":""f1.leakme"",""upload_url"":" })
                    .UploadFileAsync("location", El, Pdf, name: "a.pdf")),
                Assert.CatchAsync<Exception>(() => Make(new FakeTransport()).UploadFileAsync("location", El, Pdf, ticket: ticket)),
                Assert.CatchAsync<Exception>(() => Make(new FakeTransport { Status = 401, ResponseBody = @"{""error"":""ticket_invalid""}" })
                    .UploadFileAsync("location", El, new byte[1], ticket: ticket)),
            };

            foreach (var e in errors)
            {
                Assert.IsNotNull(e);
                StringAssert.DoesNotContain("leakme", e.ToString(), e.GetType().Name);
            }
        }
    }
}
