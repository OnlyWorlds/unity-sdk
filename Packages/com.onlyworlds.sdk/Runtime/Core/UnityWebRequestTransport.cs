using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// The default transport: <see cref="UnityWebRequest"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Chosen over <c>HttpClient</c> because it is the only HTTP path that works everywhere Unity
    /// ships -- WebGL in particular has no socket access, so <c>HttpClient</c> is not an option
    /// there. One transport that works on every target beats a faster one that strands a platform.
    /// </para>
    /// <para>
    /// Works in both the editor and at runtime: <see cref="UnityWebRequestAsyncOperation"/> raises
    /// <c>completed</c> in either context, so no EditorCoroutine detour is needed.
    /// </para>
    /// <para>
    /// ⚑ DEADLOCK HAZARD, learned the hard way (2026-07-28, hung the editor). The
    /// <c>completed</c> callback is raised from Unity's update loop. Any caller that BLOCKS that
    /// loop while waiting -- <c>.Result</c>, <c>.Wait()</c>, or a synchronous NUnit
    /// <c>[Test]</c> awaiting this task -- prevents the very pump that would complete it, and the
    /// editor freezes until the request times out. Callers must be genuinely asynchronous:
    /// <c>async Task</c> tests via <c>[Test]</c> are NOT sufficient in the editor; use
    /// <c>[UnityTest]</c> with a yield, or drive from an async context that returns to the loop.
    /// This is why the live-API tests live in their own assembly behind a define constraint.
    /// </para>
    /// <para>
    /// Non-2xx responses are returned normally rather than thrown -- the client owns error shaping
    /// (see <c>OWClient.BuildError</c>), and a transport that threw on 404 would rob it of the
    /// envelope.
    /// </para>
    /// </remarks>
    public class UnityWebRequestTransport : IOWTransport
    {
        private readonly int _timeoutSeconds;

        public UnityWebRequestTransport(int timeoutSeconds = 30)
        {
            _timeoutSeconds = timeoutSeconds;
        }

        public Task<OWResponse> SendAsync(OWRequest request, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<OWResponse>();

            // ⚑ MAIN-THREAD ONLY. SendWebRequest() must be issued from Unity's main thread.
            // After the first await in a multi-request flow (ListAllAsync paging), the
            // continuation resumes on a THREAD-POOL thread -- so page 2 would be sent from the
            // wrong thread and fail as ConnectionError, surfacing as "network unreachable" on
            // exactly the types large enough to need a second page.
            //
            // Found by real use, not by tests: single-page types worked, character (172 elements,
            // 2 pages) did not. The fake transport in the contract tests is thread-agnostic and
            // structurally could not catch this.
            if (OWMainThread.IsMainThread)
            {
                Dispatch(request, ct, tcs);
            }
            else
            {
                OWMainThread.Run(() => Dispatch(request, ct, tcs));
            }

            return tcs.Task;
        }

        private void Dispatch(OWRequest request, CancellationToken ct, TaskCompletionSource<OWResponse> tcs)
        {
            var uwr = new UnityWebRequest(request.Url, request.Method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = _timeoutSeconds,
            };

            if (!string.IsNullOrEmpty(request.Body))
            {
                uwr.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.Body));
            }

            if (request.Headers != null)
            {
                foreach (var pair in request.Headers)
                {
                    uwr.SetRequestHeader(pair.Key, pair.Value);
                }
            }

            CancellationTokenRegistration registration = default;
            if (ct.CanBeCanceled)
            {
                registration = ct.Register(() =>
                {
                    if (!uwr.isDone) uwr.Abort();
                });
            }

            var operation = uwr.SendWebRequest();
            operation.completed += _ =>
            {
                try
                {
                    if (ct.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(ct);
                        return;
                    }

                    // ConnectionError and DataProcessingError mean no usable answer came back.
                    // ProtocolError DOES have a response -- a 422 with a full error envelope is a
                    // ProtocolError, and swallowing it here would lose the very thing the client
                    // needs to build a typed error.
                    if (uwr.result == UnityWebRequest.Result.ConnectionError ||
                        uwr.result == UnityWebRequest.Result.DataProcessingError)
                    {
                        tcs.TrySetException(new OWTransportError(
                            $"{request.Method} {request.Url} failed: {uwr.error}"));
                        return;
                    }

                    tcs.TrySetResult(new OWResponse
                    {
                        Status = (int)uwr.responseCode,
                        Body = uwr.downloadHandler?.text,
                        Headers = uwr.GetResponseHeaders() ?? new Dictionary<string, string>(),
                    });
                }
                catch (Exception e)
                {
                    tcs.TrySetException(new OWTransportError("Transport failure.", e));
                }
                finally
                {
                    registration.Dispose();
                    uwr.Dispose();
                }
            };
        }
    }
}
