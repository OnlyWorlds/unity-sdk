using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// <see cref="UnityWebRequestTransport"/> against a real socket on 127.0.0.1: an answer cut off while its body is read
    /// (a body shorter than its Content-Length, a chunked body with no last chunk) is an <see cref="OWTransportError"/>,
    /// never a value or a parse error; a whole answer read to the end of the connection is the value. Mirrors the
    /// TypeScript SDK's 4.223.1 test of the same name. A [UnityTest]: the transport completes on the Editor's update
    /// loop, so the test yields a frame at a time while it waits.
    /// </summary>
    public class OWCutBodyTests
    {
        private const string Head = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n";

        /// <summary>
        /// One connection answered with the bytes given, as they are, then closed (FIN). Listens on a port the system
        /// picks; disposing stops the listener and waits for the serving task.
        /// </summary>
        private sealed class OneAnswer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly Task _serving;

            public int Port { get; }
            public string Base => $"http://127.0.0.1:{Port}";

            public OneAnswer(string answer)
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                var bytes = Encoding.UTF8.GetBytes(answer);
                _serving = Task.Run(() => Serve(bytes));
            }

            private void Serve(byte[] answer)
            {
                try
                {
                    using (var c = _listener.AcceptTcpClient())
                    {
                        var s = c.GetStream();
                        var got = new StringBuilder();
                        var buf = new byte[4096];
                        int end;
                        while ((end = got.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal)) < 0)
                        {
                            var n = s.Read(buf, 0, buf.Length);
                            if (n <= 0) return;
                            got.Append(Encoding.ASCII.GetString(buf, 0, n));
                        }

                        // The request's own body is read whole first: a socket closed with bytes unread answers RST.
                        var head = got.ToString(0, end);
                        var have = got.Length - end - 4;
                        foreach (var line in head.Split(new[] { "\r\n" }, StringSplitOptions.None))
                        {
                            if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) continue;
                            var want = int.Parse(line.Substring(15).Trim());
                            while (have < want)
                            {
                                var n = s.Read(buf, 0, buf.Length);
                                if (n <= 0) break;
                                have += n;
                            }
                        }

                        s.Write(answer, 0, answer.Length);
                        c.Client.Shutdown(SocketShutdown.Send);
                    }
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }

            public void Dispose()
            {
                _listener.Stop();
                try { _serving.Wait(5000); } catch (AggregateException) { }
            }
        }

        private static OWClient Make(OneAnswer server) => new OWClient(new OWClientConfig
        {
            ApiKey = "ow_w_test",
            ApiPin = "1234",
            BaseUrl = server.Base + "/api/v2",
            Transport = new UnityWebRequestTransport(10),
        });

        /// <summary>Sends <paramref name="call"/> to a server answering <paramref name="answer"/>, waits, then checks what it threw.</summary>
        private static IEnumerator Run(string label, string answer, Func<OneAnswer, Task> call, Action<Exception> check)
        {
            using (var server = new OneAnswer(answer))
            {
                var task = call(server);
                var start = DateTime.UtcNow;
                while (!task.IsCompleted && (DateTime.UtcNow - start).TotalSeconds < 20) yield return null;
                Assert.IsTrue(task.IsCompleted, $"{label}: answered inside 20 s");
                var e = task.Exception?.GetBaseException();
                TestContext.Out.WriteLine(
                    $"cutbody {label}: {(e == null ? "no throw" : e.GetType().Name + ": " + e.Message)}"
                    + (e?.InnerException != null ? " | inner " + e.InnerException.GetType().Name + ": " + e.InnerException.Message : ""));
                check(e);
            }
        }

        private const string LostWords = "the answer was lost while it was read";

        private static void Lost(Exception e, string words)
        {
            Assert.IsInstanceOf<OWTransportError>(e, "a transport failure, never a value or a parse error");
            StringAssert.StartsWith(words, e.Message);
            Assert.IsInstanceOf<OWTransportError>(e.InnerException, "the transport's own failure, inside");
            StringAssert.Contains(LostWords, e.InnerException.Message, "an answer had begun, and the words say so");
        }

        [UnityTest]
        public IEnumerator A_request_no_server_answered_keeps_the_transport_words()
        {
            // A port that was free a moment ago: nothing answers, no status was read, so not the cut-off words.
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            var client = new OWClient(new OWClientConfig
            {
                ApiKey = "ow_w_test",
                ApiPin = "1234",
                BaseUrl = $"http://127.0.0.1:{port}/api/v2",
                Transport = new UnityWebRequestTransport(10),
            });
            var task = client.GetAsync<JObject>("character", "x");
            var start = DateTime.UtcNow;
            while (!task.IsCompleted && (DateTime.UtcNow - start).TotalSeconds < 20) yield return null;
            Assert.IsTrue(task.IsCompleted, "answered inside 20 s");
            var e = task.Exception?.GetBaseException();
            TestContext.Out.WriteLine($"cutbody no server: {e?.Message} | inner {e?.InnerException?.Message}");
            Assert.IsInstanceOf<OWTransportError>(e);
            StringAssert.DoesNotContain(LostWords, e.InnerException.Message);
        }

        [UnityTest]
        public IEnumerator A_body_shorter_than_its_length_that_parses_is_a_transport_failure() => Run("length cut, parses",
            Head + "Content-Length: 100\r\n\r\n{\"id\":\"x\"}",
            s => Make(s).GetAsync<JObject>("character", "x"),
            e => Lost(e, "OnlyWorlds request failed: GET /character/x/"));

        [UnityTest]
        public IEnumerator A_body_shorter_than_its_length_that_does_not_parse_is_a_transport_failure() => Run("length cut, broken",
            Head + "Content-Length: 100\r\n\r\n{\"id\":\"x\",\"na",
            s => Make(s).GetAsync<JObject>("character", "x"),
            e => Lost(e, "OnlyWorlds request failed: GET /character/x/"));

        [UnityTest]
        public IEnumerator A_delete_whose_answer_never_came_is_a_transport_failure() => Run("length cut, no bytes, DELETE",
            Head + "Content-Length: 100\r\n\r\n",
            s => Make(s).DeleteAsync("character", "x"),
            e => Lost(e, "OnlyWorlds request failed: DELETE /character/x/"));

        [UnityTest]
        public IEnumerator A_chunked_body_cut_before_its_last_chunk_is_a_transport_failure() => Run("chunked cut",
            Head + "Transfer-Encoding: chunked\r\n\r\na\r\n{\"id\":\"x\"}\r\n",
            s => Make(s).GetAsync<JObject>("character", "x"),
            e => Lost(e, "OnlyWorlds request failed: GET /character/x/"));

        [UnityTest]
        public IEnumerator An_upload_host_answer_cut_off_is_a_transport_failure_naming_the_host() => Run("upload host, length cut",
            Head + "Content-Length: 100\r\n\r\n{\"key\":\"k\"}",
            s => Make(s).UploadImageAsync(new byte[] { 1, 2, 3, 4 },
                ticket: new OWMediaTicket { Ticket = "t", UploadUrl = s.Base + "/upload", MaxBytes = 1000 }),
            e => Lost(e, "OnlyWorlds image upload failed: POST to 127.0.0.1 got no answer"));

        [UnityTest]
        public IEnumerator A_whole_answer_read_to_the_end_of_the_connection_is_the_value() => Run("whole, no length",
            Head + "Connection: close\r\n\r\n{\"id\":\"x\",\"name\":\"whole\"}",
            async s => Assert.AreEqual("whole", (string)(await Make(s).GetAsync<JObject>("character", "x"))["name"]),
            e => Assert.IsNull(e, "the whole answer"));

        [UnityTest]
        public IEnumerator A_whole_body_that_is_not_json_keeps_its_own_error() => Run("whole, not JSON",
            Head + "Content-Length: 5\r\n\r\n<html",
            s => Make(s).GetAsync<JObject>("character", "x"),
            e => Assert.IsFalse(e == null || e is OWTransportError, "the reader's own error, not a transport failure"));
    }
}
