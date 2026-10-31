using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The link listener over real loopback sockets: what a sent link yields, what each refusal answers, and that
    /// nothing but 127.0.0.1 is ever bound. Each test listens on a port the system picks (port 0), so two runs, or an
    /// Editor that listens on its own port, never collide.
    /// </summary>
    public class OWLinkListenerTests
    {
        private const string World = "0199c3a1-7b2e-7c3d-8e4f-5a6b7c8d9e0f";
        private const string Element = "019a1f3c-2b4e-7d10-8c3a-6f2e9b0d4a71";
        private const string Link = "ow://" + World + "/character/" + Element;

        private readonly List<OWLinkListener> _listeners = new List<OWLinkListener>();
        private ConcurrentQueue<Action> _delivered;
        private List<OWAddressReading> _readings;

        [SetUp]
        public void SetUp()
        {
            _delivered = new ConcurrentQueue<Action>();
            _readings = new List<OWAddressReading>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var listener in _listeners) listener.Stop();
            _listeners.Clear();
        }

        /// <summary>A listener on a free port whose readings wait in a queue until <see cref="Deliver"/>.</summary>
        private OWLinkListener Started(int port = 0, Action<Action> dispatch = null, int timeoutMs = 2000)
        {
            var listener = new OWLinkListener(port, _readings.Add, dispatch ?? _delivered.Enqueue, timeoutMs);
            _listeners.Add(listener);
            Assert.IsTrue(listener.TryStart(out var refusal), $"start refused: {refusal}");
            return listener;
        }

        private void Deliver()
        {
            while (_delivered.TryDequeue(out var action)) action();
        }

        /// <summary>Writes raw bytes on a fresh connection and returns the answer line, or null.</summary>
        private static string SendRaw(int port, byte[] bytes, bool close = true)
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                socket.ReceiveTimeout = 5000;
                socket.Connect(new IPEndPoint(IPAddress.Loopback, port));
                if (bytes.Length > 0) socket.Send(bytes);
                if (close) socket.Shutdown(SocketShutdown.Send);

                var answer = new StringBuilder();
                var buffer = new byte[256];
                int read;
                while ((read = socket.Receive(buffer)) > 0) answer.Append(Encoding.UTF8.GetString(buffer, 0, read));
                var text = answer.ToString();
                var newline = text.IndexOf('\n');
                return newline < 0 ? null : text.Substring(0, newline);
            }
        }

        [Test]
        public void SentLink_IsAccepted_AndYieldsItsReading()
        {
            var listener = Started();
            Assert.AreNotEqual(0, listener.Port, "port 0 must resolve to the port the system picked");

            Assert.IsTrue(OWLinkListener.TrySend(listener.Port, Link, 2000, out var answer));
            Assert.AreEqual(OWLinkListener.Accepted, answer);
            Assert.IsEmpty(_readings, "a reading reaches the caller through its dispatch, never on the listener's thread");

            Deliver();

            Assert.AreEqual(1, _readings.Count);
            Assert.AreEqual(OWAddress.Text, _readings[0].Form);
            Assert.AreEqual(World, _readings[0].World);
            Assert.AreEqual("character", _readings[0].Type);
            Assert.AreEqual(Element, _readings[0].Id);
        }

        [Test]
        public void EveryFormTheProfileReads_IsAccepted()
        {
            var listener = Started();
            var links = new[]
            {
                OWAddress.HumanPrefix + World + "/location/" + Element,
                "ow://event/" + Element + "@12",
                "ow://" + World,
            };

            foreach (var link in links) Assert.IsTrue(OWLinkListener.TrySend(listener.Port, link), link);
            Deliver();

            Assert.AreEqual(3, _readings.Count);
            Assert.AreEqual(OWAddress.Human, _readings[0].Form);
            Assert.AreEqual(12, _readings[1].Moment);
            Assert.IsTrue(_readings[2].IsWorld);
        }

        [Test]
        public void ConnectionsInTurn_EachYieldTheirOwn()
        {
            var listener = Started();
            for (var i = 0; i < 5; i++) Assert.IsTrue(OWLinkListener.TrySend(listener.Port, Link), $"send {i}");
            Deliver();
            Assert.AreEqual(5, _readings.Count);
        }

        [Test]
        public void RefusedLink_AnswersTheProfilesRefusal_AndYieldsNothing()
        {
            var listener = Started();
            var cases = new Dictionary<string, string>
            {
                { "ow://" + World + "/Character/" + Element, OWAddressRefusal.Uppercase },
                { "ow://" + World + "/dragon/" + Element, OWAddressRefusal.UnknownType },
                { "onlyworlds://" + World, OWAddressRefusal.NotAnAddress },
                { "GET / HTTP/1.1", OWAddressRefusal.NotAnAddress },
            };

            foreach (var pair in cases)
            {
                Assert.IsFalse(OWLinkListener.TrySend(listener.Port, pair.Key, 2000, out var answer), pair.Key);
                Assert.AreEqual(OWLinkListener.RefusedPrefix + pair.Value, answer, pair.Key);
            }

            Deliver();
            Assert.IsEmpty(_readings);
            Assert.IsTrue(_delivered.IsEmpty);
        }

        [Test]
        public void LinePastTheCap_IsRefused()
        {
            var listener = Started();
            var tooLong = Link + "?lens=" + new string('a', OWLinkListener.MaxLineBytes);

            Assert.IsFalse(OWLinkListener.TrySend(listener.Port, tooLong, 2000, out var answer));
            Assert.AreEqual(OWLinkListener.RefusedPrefix + OWLinkRefusal.TooLong, answer);

            Deliver();
            Assert.IsEmpty(_readings);
        }

        [Test]
        public void LineAtTheCap_IsRead_NotRefusedForItsLength()
        {
            var listener = Started();
            var atCap = new string('a', OWLinkListener.MaxLineBytes);
            var pastCap = new string('a', OWLinkListener.MaxLineBytes + 1);

            OWLinkListener.TrySend(listener.Port, atCap, 2000, out var atCapAnswer);
            OWLinkListener.TrySend(listener.Port, pastCap, 2000, out var pastCapAnswer);

            Assert.AreEqual(OWLinkListener.RefusedPrefix + OWAddressRefusal.NotAnAddress, atCapAnswer,
                "a line of exactly the cap is read and refused by the profile, not for its length");
            Assert.AreEqual(OWLinkListener.RefusedPrefix + OWLinkRefusal.TooLong, pastCapAnswer);
        }

        [Test]
        public void CrLfEnding_IsTheSameLine()
        {
            var listener = Started();
            Assert.AreEqual(OWLinkListener.Accepted, SendRaw(listener.Port, Encoding.UTF8.GetBytes(Link + "\r\n")));
            Deliver();
            Assert.AreEqual(1, _readings.Count);
        }

        [Test]
        public void SilentSender_IsAnsweredNoLine_AfterTheTimeout()
        {
            var listener = Started(timeoutMs: 300);
            Assert.AreEqual(OWLinkListener.RefusedPrefix + OWLinkRefusal.NoLine,
                SendRaw(listener.Port, new byte[0], close: false));
        }

        [Test]
        public void SenderThatClosesAtOnce_IsAnsweredNoLine()
        {
            var listener = Started();
            Assert.AreEqual(OWLinkListener.RefusedPrefix + OWLinkRefusal.NoLine, SendRaw(listener.Port, new byte[0]));
        }

        [Test]
        public void SenderThatClosesWithoutANewline_StillDeliversTheLine()
        {
            var listener = Started();
            Assert.AreEqual(OWLinkListener.Accepted, SendRaw(listener.Port, Encoding.UTF8.GetBytes(Link)));
            Deliver();
            Assert.AreEqual(1, _readings.Count);
        }

        [Test]
        public void DispatchThatThrows_IsAnsweredNoReceiver()
        {
            var listener = Started(dispatch: _ => throw new InvalidOperationException("no pump"));

            Assert.IsFalse(OWLinkListener.TrySend(listener.Port, Link, 2000, out var answer));
            Assert.AreEqual(OWLinkListener.RefusedPrefix + OWLinkRefusal.NoReceiver, answer);
        }

        /// <summary>A listener whose readings arrive with their line, dispatched through <paramref name="dispatch"/>.</summary>
        private OWLinkListener StartedWithText(List<string> texts, Func<Action, bool> dispatch)
        {
            var listener = new OWLinkListener(0, (reading, text) =>
            {
                _readings.Add(reading);
                texts.Add(text);
            }, dispatch);
            _listeners.Add(listener);
            Assert.IsTrue(listener.TryStart(out var refusal), $"start refused: {refusal}");
            return listener;
        }

        [Test]
        public void TheLineAsReceived_ArrivesBesideItsReading()
        {
            var texts = new List<string>();
            var listener = StartedWithText(texts, work =>
            {
                _delivered.Enqueue(work);
                return true;
            });

            // A site address by slug: its reading cannot be written back as an address, so the text is the only copy.
            const string site = "https://www.onlyworlds.com/w/old-harbour/character/" + Element;
            Assert.AreEqual(OWLinkListener.Accepted, SendRaw(listener.Port, Encoding.UTF8.GetBytes(site + "\r\n")));
            Assert.IsTrue(OWLinkListener.TrySend(listener.Port, Link));
            Deliver();

            Assert.AreEqual(new[] { site, Link }, texts.ToArray(), "the line as sent, its ending stripped");
            Assert.IsTrue(_readings[0].WorldIsSlug);
            Assert.AreEqual(World, _readings[1].World);
        }

        [Test]
        public void DispatchThatRefuses_IsAnsweredNoReceiver_AndNothingRuns()
        {
            var texts = new List<string>();
            var offered = 0;
            var listener = StartedWithText(texts, _ =>
            {
                offered++;
                return false;
            });

            Assert.IsFalse(OWLinkListener.TrySend(listener.Port, Link, 2000, out var answer));
            Assert.AreEqual(OWLinkListener.RefusedPrefix + OWLinkRefusal.NoReceiver, answer);
            Assert.AreEqual(1, offered, "the dispatch was asked once");
            Assert.IsEmpty(texts);
            Assert.IsEmpty(_readings);
        }

        [Test]
        public void DispatchThatThrows_IsStillAnsweredNoReceiver_InTheTextForm()
        {
            var listener = StartedWithText(new List<string>(), _ => throw new InvalidOperationException("no pump"));

            Assert.IsFalse(OWLinkListener.TrySend(listener.Port, Link, 2000, out var answer));
            Assert.AreEqual(OWLinkListener.RefusedPrefix + OWLinkRefusal.NoReceiver, answer);
        }

        [Test]
        public void TextFormsDefaultDispatch_IsTheMainThreadQueue_RefusedWithoutAPump()
        {
            OWMainThread.Capture();
            OWMainThread.Pump();

            var texts = new List<string>();
            var listener = new OWLinkListener(0, (reading, text) => texts.Add(text));
            _listeners.Add(listener);
            Assert.IsTrue(listener.TryStart(out _));

            OWLinkListener.TrySend(listener.Port, Link, 2000, out var answer);
            if (answer == OWLinkListener.Accepted)
            {
                Assert.IsEmpty(texts);
                OWMainThread.Pump();
                Assert.AreEqual(new[] { Link }, texts.ToArray());
            }
            else
            {
                Assert.AreEqual(OWLinkListener.RefusedPrefix + OWLinkRefusal.NoReceiver, answer);
                Assert.IsEmpty(texts);
            }
        }

        [Test]
        public void TheTextForm_RefusesANullOnLink()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new OWLinkListener(0, (Action<OWAddressReading, string>)null, (Func<Action, bool>)null));
        }

        [Test]
        public void DefaultDispatch_IsTheMainThreadQueue()
        {
            OWMainThread.Capture();
            OWMainThread.Pump();

            var listener = new OWLinkListener(0, _readings.Add);
            _listeners.Add(listener);
            Assert.IsTrue(listener.TryStart(out _));

            OWLinkListener.TrySend(listener.Port, Link, 2000, out var answer);
            if (answer == OWLinkListener.Accepted)
            {
                // The Editor's pump is installed: the reading waits in the queue until the pump runs.
                Assert.IsEmpty(_readings);
                OWMainThread.Pump();
                Assert.AreEqual(1, _readings.Count);
            }
            else
            {
                // No pump outside the Editor: OWMainThread.Run refuses the work, and the sender hears so.
                Assert.AreEqual(OWLinkListener.RefusedPrefix + OWLinkRefusal.NoReceiver, answer);
                Assert.IsEmpty(_readings);
            }
        }

        [Test]
        public void SecondListener_OnATakenPort_SaysSo_AndTheFirstKeepsListening()
        {
            var first = Started();
            var second = new OWLinkListener(first.Port, _readings.Add, _delivered.Enqueue);
            _listeners.Add(second);

            Assert.IsFalse(second.TryStart(out var refusal));
            Assert.AreEqual(OWLinkRefusal.PortTaken, refusal);
            Assert.IsFalse(second.IsRunning);
            Assert.AreEqual(0, second.Port);

            Assert.IsTrue(OWLinkListener.TrySend(first.Port, Link));
        }

        [Test]
        public void StartTwice_IsRefused_AndABadPortIsRefused()
        {
            var listener = Started();
            Assert.IsFalse(listener.TryStart(out var refusal));
            Assert.AreEqual(OWLinkRefusal.Running, refusal);

            Assert.IsFalse(new OWLinkListener(70000, _readings.Add).TryStart(out refusal));
            Assert.AreEqual(OWLinkRefusal.BadPort, refusal);
            Assert.IsFalse(new OWLinkListener(-1, _readings.Add).TryStart(out refusal));
            Assert.AreEqual(OWLinkRefusal.BadPort, refusal);
        }

        [Test]
        public void Stop_FreesThePort_AndASendThenFails()
        {
            var listener = Started();
            var port = listener.Port;
            listener.Stop();
            listener.Stop();

            Assert.IsFalse(listener.IsRunning);
            Assert.IsNull(listener.Endpoint);
            Assert.IsFalse(OWLinkListener.TrySend(port, Link, 500, out var answer));
            Assert.IsNull(answer, "nothing listens, so nothing answers");

            Started(port);
        }

        [Test]
        public void Endpoint_IsLoopbackOnly()
        {
            var listener = Started();

            Assert.AreEqual(IPAddress.Loopback, listener.Endpoint.Address);
            Assert.IsTrue(IPAddress.IsLoopback(listener.Endpoint.Address));

            // Through any of this machine's other addresses, the port is not there.
            IPAddress[] own;
            try
            {
                own = Dns.GetHostAddresses(Dns.GetHostName());
            }
            catch (SocketException)
            {
                own = new IPAddress[0];
            }

            foreach (var address in own)
            {
                if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address)) continue;
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    var connecting = socket.BeginConnect(new IPEndPoint(address, listener.Port), null, null);
                    var connected = false;
                    if (connecting.AsyncWaitHandle.WaitOne(500))
                    {
                        try
                        {
                            socket.EndConnect(connecting);
                            connected = true;
                        }
                        catch (SocketException)
                        {
                            // Refused: the answer this test wants.
                        }
                    }

                    Assert.IsFalse(connected, $"reached the listener through {address}");
                }
            }
        }

        [Test]
        public void TrySend_NeverSendsALineBreak_OrToNoPort()
        {
            var listener = Started();

            Assert.IsFalse(OWLinkListener.TrySend(listener.Port, Link + "\nGET / HTTP/1.1", 2000, out var answer));
            Assert.IsNull(answer, "a link with a line break is not sent at all");
            Assert.IsFalse(OWLinkListener.TrySend(listener.Port, null));
            Assert.IsFalse(OWLinkListener.TrySend(0, Link));

            Deliver();
            Assert.IsEmpty(_readings);
        }
    }
}
