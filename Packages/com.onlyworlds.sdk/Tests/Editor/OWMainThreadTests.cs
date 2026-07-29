using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The marshalling queue that fixed the paging bug of 2026-07-28.
    /// </summary>
    /// <remarks>
    /// This class existed for a day with no tests at all, which is a poor state for the one piece
    /// of code written in response to a bug that reached the user. These do not -- and cannot --
    /// prove the thing that actually matters (that <c>UnityWebRequest</c> is issued from Unity's
    /// main thread); only a real request against a real world does that, and it is what found the
    /// bug in the first place. What they do prove is the queue's own contract: ordering, drain
    /// semantics, isolation between actions, and that work can never be silently swallowed.
    /// </remarks>
    public class OWMainThreadTests
    {
        [SetUp]
        public void SetUp()
        {
            // The editor pump installs itself via [InitializeOnLoad], so tests run with a pump
            // present. Capture is idempotent; this just makes the precondition explicit.
            OWMainThread.Capture();
            OWMainThread.Pump();
        }

        [TearDown]
        public void TearDown() => OWMainThread.Pump();

        [Test]
        public void Pump_RunsQueuedWork()
        {
            var ran = false;
            OWMainThread.Run(() => ran = true);

            Assert.IsFalse(ran, "Run must queue, not execute inline -- the point is to defer.");

            OWMainThread.Pump();

            Assert.IsTrue(ran);
        }

        [Test]
        public void Pump_PreservesOrder()
        {
            var order = "";
            OWMainThread.Run(() => order += "a");
            OWMainThread.Run(() => order += "b");
            OWMainThread.Run(() => order += "c");

            OWMainThread.Pump();

            Assert.AreEqual("abc", order, "A request queue that reorders is a request queue that lies.");
        }

        [Test]
        public void Pump_DrainsEverythingInOnePass()
        {
            var count = 0;
            for (var i = 0; i < 25; i++) OWMainThread.Run(() => count++);

            OWMainThread.Pump();

            Assert.AreEqual(25, count, "A partial drain leaves requests hanging for a frame or forever.");
        }

        [Test]
        public void ThrowingAction_DoesNotStallTheQueue()
        {
            var afterRan = false;

            OWMainThread.Run(() => throw new InvalidOperationException("deliberate"));
            OWMainThread.Run(() => afterRan = true);

            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("deliberate"));

            OWMainThread.Pump();

            Assert.IsTrue(afterRan,
                "One bad action must not stall the pump -- a stalled pump hangs every later request.");
        }

        [Test]
        public void Run_QueuedFromAnotherThread_IsDrainedByTheMainThread()
        {
            // The actual shape of the bug: a continuation resumes on a thread-pool thread and has
            // to get its work back onto the main thread to issue a request at all.
            var ranOn = -1;
            var mainThreadId = Thread.CurrentThread.ManagedThreadId;

            Task.Run(() =>
            {
                Assert.IsFalse(OWMainThread.IsMainThread,
                    "A thread-pool thread must not report itself as Unity's main thread.");
                OWMainThread.Run(() => ranOn = Thread.CurrentThread.ManagedThreadId);
            }).Wait();

            OWMainThread.Pump();

            Assert.AreEqual(mainThreadId, ranOn,
                "Work queued off-thread must execute on the thread that drives the pump.");
        }

        [Test]
        public void Run_IgnoresNull()
        {
            Assert.DoesNotThrow(() => OWMainThread.Run(null));
        }

        [Test]
        public void IsMainThread_IsTrueOnTheTestThread()
        {
            Assert.IsTrue(OWMainThread.IsMainThread,
                "EditMode tests run on the main thread; if this fails the pump's premise is wrong.");
        }
    }
}
