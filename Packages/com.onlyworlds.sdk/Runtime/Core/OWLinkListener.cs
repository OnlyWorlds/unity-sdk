// A loopback-only listener for address links: a link clicked in a browser reaches a running Editor or player through
// a small forwarder the operating system runs (Tools~/ow-link), which hands it to this listener over 127.0.0.1.
//
// The protocol is one line each way. The sender connects to 127.0.0.1:<port> and writes one address as UTF-8,
// ended by "\n" (at most MaxLineBytes before it). The listener answers "ok\n" when it read an address and handed it
// on, or "refused <reason>\n", then closes. It can only deliver an address: no other verb, no file, no process.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// The listener's own refusals, beside the address profile's (<see cref="OWAddressRefusal"/>). A line is answered
    /// with one of either; a start is refused with one of the last four.
    /// </summary>
    public static class OWLinkRefusal
    {
        /// <summary>The line ran past <see cref="OWLinkListener.MaxLineBytes"/> bytes.</summary>
        public const string TooLong = "too_long";

        /// <summary>No line arrived before the timeout, or the sender closed without writing one.</summary>
        public const string NoLine = "no_line";

        /// <summary>The address was read, but the caller's dispatch could not take it (no main-thread pump, say).</summary>
        public const string NoReceiver = "no_receiver";

        /// <summary>Start: the port is held by another listener or process (another Editor, a player), or reserved.</summary>
        public const string PortTaken = "port_taken";

        /// <summary>Start: the port is not 0 to 65535.</summary>
        public const string BadPort = "bad_port";

        /// <summary>Start: this listener is already running.</summary>
        public const string Running = "running";

        /// <summary>Start: the platform has no sockets here (WebGL), or the socket failed for another reason.</summary>
        public const string Unavailable = "unavailable";
    }

    /// <summary>
    /// Listens on 127.0.0.1, never another interface, for one address link per connection, and hands each address it
    /// reads to the caller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A connection carries one line: the link, read whole by <see cref="OWAddress.TryParseLink"/>, so a line the
    /// address profile refuses is answered with the profile's own word and goes no further. An HTTP request a web page
    /// sends to the port is such a line (<c>GET / HTTP/1.1</c> is not an address).
    /// </para>
    /// <para>
    /// The listener runs on a background thread. An accepted address reaches <c>onLink</c> through the caller's
    /// <c>dispatch</c>, by default <see cref="OWMainThread.Run"/>, so it arrives on Unity's main thread at the next
    /// pump. Pass your own dispatch to deliver elsewhere. A dispatch that throws is answered
    /// <see cref="OWLinkRefusal.NoReceiver"/>, so the sender can try the next port.
    /// </para>
    /// <para>
    /// Two Editors may run at once: a port already taken is <see cref="OWLinkRefusal.PortTaken"/> from
    /// <see cref="TryStart"/>, never an exception. Stop the listener when you are done with it; a domain unload (an
    /// Editor's script reload) stops every running listener, so the port is free for the next.
    /// </para>
    /// </remarks>
    public sealed class OWLinkListener : IDisposable
    {
        /// <summary>The longest line the listener reads, in bytes, without its line ending.</summary>
        public const int MaxLineBytes = 4096;

        /// <summary>How long one connection may take to deliver its line, and a sender to hear its answer.</summary>
        public const int DefaultTimeoutMs = 2000;

        /// <summary>The answer to a line whose address was handed on.</summary>
        public const string Accepted = "ok";

        /// <summary>The answer to any other line starts with this, then names the refusal.</summary>
        public const string RefusedPrefix = "refused ";

        /// <summary>The most bytes read past a refusal, so closing does not reset the connection before the answer.</summary>
        private const int DrainBytes = 65536;

        private static readonly List<OWLinkListener> Live = new List<OWLinkListener>();
        private static bool _unloadHooked;

        private readonly int _requestedPort;
        private readonly Action<OWAddressReading> _onLink;
        private readonly Action<Action> _dispatch;
        private readonly int _timeoutMs;
        private readonly object _gate = new object();
        private Run _run;

        /// <summary>A listener for <paramref name="port"/> (0: a free port the system picks; read <see cref="Port"/>).</summary>
        /// <param name="port">The port on 127.0.0.1.</param>
        /// <param name="onLink">Receives each accepted address.</param>
        /// <param name="dispatch">How <paramref name="onLink"/> is called; null for <see cref="OWMainThread.Run"/>.</param>
        /// <param name="timeoutMs">How long one connection may take to deliver its line.</param>
        public OWLinkListener(int port, Action<OWAddressReading> onLink, Action<Action> dispatch = null,
            int timeoutMs = DefaultTimeoutMs)
        {
            if (timeoutMs <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
            _requestedPort = port;
            _onLink = onLink ?? throw new ArgumentNullException(nameof(onLink));
            _dispatch = dispatch ?? OWMainThread.Run;
            _timeoutMs = timeoutMs;
        }

        /// <summary>The port it listens on while running; 0 when stopped.</summary>
        public int Port => _run?.Endpoint.Port ?? 0;

        /// <summary>The address and port it listens on while running, always 127.0.0.1; null when stopped.</summary>
        public IPEndPoint Endpoint => _run?.Endpoint;

        /// <summary>True between a <see cref="TryStart"/> that succeeded and <see cref="Stop"/>.</summary>
        public bool IsRunning => _run != null;

        /// <summary>
        /// Binds 127.0.0.1 and starts listening. False with the <paramref name="refusal"/>, one of
        /// <see cref="OWLinkRefusal"/>'s start words, when it cannot.
        /// </summary>
        public bool TryStart(out string refusal)
        {
            lock (_gate)
            {
                refusal = null;
                if (_run != null)
                {
                    refusal = OWLinkRefusal.Running;
                    return false;
                }

                if (_requestedPort < IPEndPoint.MinPort || _requestedPort > IPEndPoint.MaxPort)
                {
                    refusal = OWLinkRefusal.BadPort;
                    return false;
                }

                Socket socket = null;
                try
                {
                    socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        // Windows lets a second socket share a port unless the first claims it alone.
                        socket.ExclusiveAddressUse = true;
                    }
                    catch (Exception)
                    {
                        // Not every platform has the option; the bind below still refuses a taken port there.
                    }

                    socket.Bind(new IPEndPoint(IPAddress.Loopback, _requestedPort));
                    socket.Listen(8);
                }
                catch (SocketException e)
                {
                    socket?.Close();
                    refusal = e.SocketErrorCode == SocketError.AddressAlreadyInUse
                              || e.SocketErrorCode == SocketError.AccessDenied
                        ? OWLinkRefusal.PortTaken
                        : OWLinkRefusal.Unavailable;
                    return false;
                }
                catch (Exception)
                {
                    socket?.Close();
                    refusal = OWLinkRefusal.Unavailable;
                    return false;
                }

                var run = new Run(socket, (IPEndPoint)socket.LocalEndPoint);
                run.Thread = new Thread(() => AcceptLoop(run)) { IsBackground = true, Name = "OnlyWorlds link listener" };
                _run = run;
                run.Thread.Start();
                Track(this);
                return true;
            }
        }

        /// <summary>Stops listening and frees the port. Safe to call when stopped.</summary>
        public void Stop()
        {
            Run run;
            lock (_gate)
            {
                run = _run;
                if (run == null) return;
                _run = null;
                run.Stopped = true;
            }

            Untrack(this);
            try
            {
                run.Socket.Close();
            }
            catch (Exception)
            {
                // Closing is all that is left to do; a socket that will not close is already unusable.
            }

            if (run.Thread != Thread.CurrentThread) run.Thread.Join(_timeoutMs + 1000);
        }

        /// <summary>Same as <see cref="Stop"/>.</summary>
        public void Dispose() => Stop();

        /// <summary>
        /// Sends <paramref name="link"/> to a listener on 127.0.0.1:<paramref name="port"/>. True when it answered
        /// <see cref="Accepted"/>.
        /// </summary>
        public static bool TrySend(int port, string link, int timeoutMs = DefaultTimeoutMs) =>
            TrySend(port, link, timeoutMs, out _);

        /// <summary>
        /// Sends <paramref name="link"/> to a listener on 127.0.0.1:<paramref name="port"/>, with the
        /// <paramref name="answer"/> it gave: <see cref="Accepted"/>, <see cref="RefusedPrefix"/> and a reason, or null
        /// when nothing answered (no listener, a timeout) or the link holds a line break and was never sent.
        /// </summary>
        public static bool TrySend(int port, string link, int timeoutMs, out string answer)
        {
            answer = null;
            if (link == null || link.IndexOf('\n') >= 0 || link.IndexOf('\r') >= 0) return false;
            if (port <= 0 || port > IPEndPoint.MaxPort || timeoutMs <= 0) return false;

            var clock = Stopwatch.StartNew();
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    var connecting = socket.BeginConnect(new IPEndPoint(IPAddress.Loopback, port), null, null);
                    if (!connecting.AsyncWaitHandle.WaitOne(timeoutMs)) return false;
                    socket.EndConnect(connecting);

                    socket.SendTimeout = Left(clock, timeoutMs);
                    socket.Send(Encoding.UTF8.GetBytes(link + "\n"));
                    socket.Shutdown(SocketShutdown.Send);

                    var buffer = new byte[256];
                    var count = 0;
                    while (count < buffer.Length)
                    {
                        socket.ReceiveTimeout = Left(clock, timeoutMs);
                        var read = socket.Receive(buffer, count, buffer.Length - count, SocketFlags.None);
                        if (read == 0) break;
                        count += read;
                        if (Array.IndexOf(buffer, (byte)'\n', 0, count) >= 0) break;
                    }

                    var newline = Array.IndexOf(buffer, (byte)'\n', 0, count);
                    if (newline < 0) return false;
                    answer = Encoding.UTF8.GetString(buffer, 0, newline).TrimEnd('\r');
                    return answer == Accepted;
                }
            }
            catch (SocketException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        private void AcceptLoop(Run run)
        {
            while (!run.Stopped)
            {
                Socket client;
                try
                {
                    // Poll rather than block in Accept, so a Stop is seen within the poll on every runtime.
                    if (!run.Socket.Poll(200 * 1000, SelectMode.SelectRead)) continue;
                    client = run.Socket.Accept();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    if (run.Stopped) return;
                    Thread.Sleep(50);
                    continue;
                }

                using (client)
                {
                    Serve(client);
                }
            }
        }

        private void Serve(Socket client)
        {
            string answer;
            var line = ReadLine(client, _timeoutMs, out var lineRefusal);
            if (line == null)
            {
                answer = RefusedPrefix + lineRefusal;
            }
            else if (!OWAddress.TryParseLink(line, out var reading, out var refusal))
            {
                answer = RefusedPrefix + refusal;
            }
            else
            {
                var onLink = _onLink;
                try
                {
                    _dispatch(() => onLink(reading));
                    answer = Accepted;
                }
                catch (Exception)
                {
                    answer = RefusedPrefix + OWLinkRefusal.NoReceiver;
                }
            }

            try
            {
                client.SendTimeout = _timeoutMs;
                client.Send(Encoding.UTF8.GetBytes(answer + "\n"));
                client.Shutdown(SocketShutdown.Send);

                // Read what the sender still has in flight (a line past the cap), briefly, then close.
                var scratch = new byte[4096];
                var drained = 0;
                client.ReceiveTimeout = 200;
                while (drained < DrainBytes)
                {
                    var read = client.Receive(scratch);
                    if (read == 0) break;
                    drained += read;
                }
            }
            catch (SocketException)
            {
                // The sender left early; it has its answer or does not want it.
            }
            catch (ObjectDisposedException)
            {
                // The listener stopped mid-answer.
            }
        }

        /// <summary>One line, its ending stripped, or null with the refusal. The bytes past its "\n" are ignored.</summary>
        private static string ReadLine(Socket client, int timeoutMs, out string refusal)
        {
            refusal = null;
            var buffer = new byte[MaxLineBytes + 2]; // the line, then "\r\n"
            var count = 0;
            var ended = false;
            var clock = Stopwatch.StartNew();
            while (count < buffer.Length)
            {
                var left = timeoutMs - (int)clock.ElapsedMilliseconds;
                if (left <= 0) break;

                int read;
                try
                {
                    client.ReceiveTimeout = left;
                    read = client.Receive(buffer, count, buffer.Length - count, SocketFlags.None);
                }
                catch (SocketException)
                {
                    break;
                }

                if (read == 0)
                {
                    // The sender closed: what came is the line, when anything came.
                    ended = count > 0;
                    break;
                }

                var newline = Array.IndexOf(buffer, (byte)'\n', count, read);
                count += read;
                if (newline >= 0)
                {
                    count = newline;
                    ended = true;
                    break;
                }
            }

            if (!ended)
            {
                refusal = count == buffer.Length ? OWLinkRefusal.TooLong : OWLinkRefusal.NoLine;
                return null;
            }

            if (count > 0 && buffer[count - 1] == (byte)'\r') count--;
            if (count > MaxLineBytes)
            {
                refusal = OWLinkRefusal.TooLong;
                return null;
            }

            return Encoding.UTF8.GetString(buffer, 0, count);
        }

        private static int Left(Stopwatch clock, int timeoutMs) =>
            Math.Max(1, timeoutMs - (int)clock.ElapsedMilliseconds);

        private static void Track(OWLinkListener listener)
        {
            lock (Live)
            {
                Live.Add(listener);
                if (_unloadHooked) return;
                AppDomain.CurrentDomain.DomainUnload += (_, __) => StopAll();
                _unloadHooked = true;
            }
        }

        private static void Untrack(OWLinkListener listener)
        {
            lock (Live)
            {
                Live.Remove(listener);
            }
        }

        private static void StopAll()
        {
            OWLinkListener[] running;
            lock (Live)
            {
                running = Live.ToArray();
            }

            foreach (var listener in running) listener.Stop();
        }

        /// <summary>One run of the listener, from a start to its stop: the socket, its thread and its stop flag.</summary>
        private sealed class Run
        {
            public Run(Socket socket, IPEndPoint endpoint)
            {
                Socket = socket;
                Endpoint = endpoint;
            }

            public Socket Socket { get; }
            public IPEndPoint Endpoint { get; }
            public Thread Thread { get; set; }
            public volatile bool Stopped;
        }
    }
}
