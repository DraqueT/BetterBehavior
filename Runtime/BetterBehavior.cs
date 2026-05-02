using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace com.DarisaDesigns
{
    /// <summary>
    /// This can be used as an upgrade from Monobehavior. It adds the concept of queued IEnumerators.
    /// Queued IEnumerators will be executed in sequence. Finally blocks are now GUARANTEED. The queue
    /// defaults to ID = 0, which keeps track of multiple queues, but additional queues can be accessed
    /// as well if you need multiple queues for a single object.
    /// </summary>
    public class BetterBehavior : MonoBehaviour
    {
        private readonly Dictionary<int, IEnumerator> CurQueueIEnumerators = new();
        private readonly Dictionary<int, WorkUnit> CurrentWork = new();
        private readonly Dictionary<int, Queue<WorkUnit>> workQueue = new();
        private readonly HashSet<long> cancelledWorkIds = new();
        private readonly Dictionary<int, long> assignedQueueWorkIds = new();
        private readonly Dictionary<long, string> workIdCallTraces = new();
        private long curWorkId = 0;

        /// <summary>
        /// Use this as a top level call to start coroutines that will never be yielded. If they will be yielded, instead, call QueueIEnumerator
        /// and yield that. Yielding the return value of QueueToCoroutine outside of test scenarios may result in unmanaged coroutine behavior
        /// and loss of BetterBehavior guarantees. Also, it will toss warnings up, bothering you to fix it.
        /// </summary>
        /// <param name="work">IEnumerator to queue</param>
        /// <param name="resetQueue">If set to true, cancels all prior queued events before starting next event</param>
        /// <param name="targetQueueId">This is the ID of the queue you are adding work to. Defaults to 0. Can be any arbitrary int.</param>
        /// <param name="onException">This action will consume any uncaught exception which interrupts work passed into the queue</param>
        /// <param name="suppressWarnings">Sometimes reliance on unmanaged Coroutine/AsyncOperation/Etc. behavior is required. This silences warnings for these cases.</param>
        /// <returns>IEnumerator representing the full work of the queue that work was assigned to.</returns>
        public Coroutine QueueToCoroutine(IEnumerator work, bool resetQueue = false, int targetQueueId = 0, Action<Exception> onException = null, bool suppressWarnings = false)
        {
            return StartCoroutine(QueueIEnumerator(work, resetQueue, targetQueueId, onException, Environment.StackTrace.ToString(), suppressWarnings));
        }

        /// <summary>
        /// Queues a IEnumerator to be completed after others finish
        /// </summary>
        /// <param name="work">IEnumerator to queue</param>
        /// <param name="resetQueue">If set to true, cancels all prior queued events before starting next event</param>
        /// <param name="targetQueueId">This is the ID of the queue you are adding work to. Defaults to 0. Can be any arbitrary int.</param>
        /// <param name="onException">This action will consume any uncaught exception which interrupts work passed into the queue</param>
        /// <param name="trace">A string representing the relevant trace of the work's start point</param>
        /// <param name="suppressWarnings">Sometimes reliance on unmanaged Coroutine/AsyncOperation/Etc. behavior is required. This silences warnings for these cases.</param>
        /// <returns>IEnumerator representing the full work of the queue that work was assigned to.</returns>
        public IEnumerator QueueIEnumerator(IEnumerator work, bool resetQueue = false, int targetQueueId = 0, Action<Exception> onException = null, string trace = null, bool suppressWarnings = false)
        {
            if (trace == null)
                trace = Environment.StackTrace.ToString(); // if not passed, attempt to capture trace here
            if (this == null || this.gameObject == null || !this.gameObject.activeInHierarchy)
                throw new Exception("Cannot run code from a disabled or inactive object");
            if (work == null)
                throw new ArgumentException("Target IEnumerator work cannot be null.");
            if (!workQueue.ContainsKey(targetQueueId))
                workQueue[targetQueueId] = new();

            if (resetQueue)
                ResetQueue(targetQueueId);

            workQueue[targetQueueId].Enqueue(new(work, onException, suppressWarnings));
            if (!CurQueueIEnumerators.ContainsKey(targetQueueId) || CurQueueIEnumerators[targetQueueId] == null)
                CurQueueIEnumerators[targetQueueId] = SafelyWrapIEnum(RunQueue(targetQueueId, trace), trace: trace, suppressWarnings: suppressWarnings);

            if (CurQueueIEnumerators.TryGetValue(targetQueueId, out var myIEnumerator))
                yield return myIEnumerator;
            yield return null;
        }

        private long GetNextWorkId()
        {
            var id = curWorkId;
            curWorkId++;
            return id;
        }

        /// <summary>
        /// Cancels all prior queued events before starting next event.
        /// </summary>
        /// <param name="targetQueueId">Queue to reset. Defaults to 0.</param>
        public void ResetQueue(int targetQueueId = 0)
        {
            if (assignedQueueWorkIds.TryGetValue(targetQueueId, out var workId))
                cancelledWorkIds.Add(workId);
            if (workQueue.TryGetValue(targetQueueId, out var targetQueue))
                targetQueue.Clear();
        }

        /// <summary>
        /// Tests whether queue is complete and has no remaining work.
        /// </summary>
        /// <param name="targetQueue">Queue to test. Defaults to 0.</param>
        /// <returns>True if queue is completely empty and has no remaining work. False otherwise.</returns>
        public bool IsQueueDone(int targetQueue = 0)
        {
            if (workQueue.TryGetValue(targetQueue, out var myQueue))
                return (myQueue == null || myQueue.Count == 0) && (!CurrentWork.ContainsKey(targetQueue) || CurrentWork[targetQueue] == null);
            return true;
        }

        /// <summary>
        /// Returns the target queue's driving IEnumerator
        /// </summary>
        /// <param name="targetQueue">Queue to fetch current work (if any) from. Defaults to 0.</param> 
        /// <returns>Currently processing IEnumerator of target queue. Null if none.</returns>
        public IEnumerator GetIEnumeratorDriver(int targetQueue = 0)
        {
            if (CurQueueIEnumerators.TryGetValue(targetQueue, out var targetIEnumerator))
                yield return targetIEnumerator;
            yield return null;
        }

        /// <summary>
        /// Returns the current work item of a target queue, if any.
        /// </summary>
        /// <param name="targetQueue">Queue to return current work from</param>
        /// <returns>Current work from queue, null if none exists</returns>
        public IEnumerator GetCurWork(int targetQueue)
        {
            if (CurrentWork.TryGetValue(targetQueue, out var work))
                yield return work.targetIEnumerator;
            yield return null;
        }

        private IEnumerator RunQueue(int targetQueue, string trace)
        {
            // wait for prior iteration of queue to finish winding down before beginning
            while (assignedQueueWorkIds.ContainsKey(targetQueue))
                yield return null;

            var workId = GetNextWorkId();
            workIdCallTraces[workId] = trace;
            try
            {
                assignedQueueWorkIds[targetQueue] = workId;
                while (workQueue[targetQueue].Count > 0 && this != null && !cancelledWorkIds.Contains(workId))
                {
                    var workUnit = workQueue[targetQueue].Dequeue();
                    CurrentWork[targetQueue] = workUnit;
                    yield return SafelyWrapIEnum(workUnit.targetIEnumerator, workUnit.OnException, workId, trace, workUnit.suppressWarnings);
                }
                CurQueueIEnumerators[targetQueue] = null;
            }
            finally
            {
                CurrentWork.Remove(targetQueue);
                assignedQueueWorkIds.Remove(targetQueue);
                workIdCallTraces.Remove(workId);
            }
        }

        /// <summary>
        /// A basic replacement for StartCoroutine. Wraps work in coroutine via StartCoroutine. Any final blocks are guaranteed to run post-uncaught exception.
        /// All exceptions are fed to the Debug log, whether or not there is an onException callback. Outside of testing, avoid yielding the returned value of this
        /// method, as that may result in a Coroutine with code which cannot be managed and loss of BetterBehavior guarantees.
        /// </summary>
        /// <param name="targetWork">Work to be started.</param>
        /// <param name="onException">An action which will be fed any exception which causes the coroutine to terminate early.</param>
        /// <param name="workId">Used for managed cancelation.</param>
        /// <param name="suppressWarnings">Sometimes reliance on unmanaged Coroutine/AsyncOperation/Etc. behavior is required. This silences warnings for these cases.</param>
        /// <returns>Yields like a typical coroutine in all cases except if the target coroutine experiences an uncaught exception. In this case, it yields a break.</returns>
        public Coroutine SafelyStartCoroutine(IEnumerator targetWork, Action<Exception> onException = null, long workId = -1, bool suppressWarnings = false)
        {
            return StartCoroutine(SafelyWrapIEnum(targetWork, onException, workId, Environment.StackTrace.ToString(), suppressWarnings));
        }

        /// <summary>
        /// Half of the basic replacement for StartCoroutine. Does not start work, but returns wrapped IEnumerator. Any final blocks are guaranteed to run post-uncaught exception.
        /// All exceptions are fed to the Debug log, whether or not there is an onException callback.
        /// </summary>
        /// <param name="targetWork">Work to be started.</param>
        /// <param name="onException">An action which will be fed any exception which causes the coroutine to terminate early.</param>
        /// <param name="workId">Used for managed cancelation.</param>
        /// <param name="trace">Optionally provided for naked work to provide context</param>
        /// <param name="suppressWarnings">Sometimes reliance on unmanaged Coroutine/AsyncOperation/Etc. behavior is required. This silences warnings for these cases.</param>
        /// <returns>Passed work wrapped in BestBehavior code for behavioral guarantees.</returns>
        public IEnumerator SafelyWrapIEnum(IEnumerator targetWork, Action<Exception> onException = null, long workId = -1, string trace = null, bool suppressWarnings = false)
        {
            if (targetWork == null)
                throw new ArgumentException("Target IEnumerator targetWork cannot be null.");
            var stack = new Stack<IEnumerator>();
            stack.Push(targetWork);

            if (workId == -1 && trace == null)
                trace = Environment.StackTrace.ToString(); // naked calls to this will not have trace recorded with workId
            trace ??= "";

            string callerName;

            try
            {
                callerName = targetWork.ToString();
            }
            catch
            {
                callerName = targetWork.GetType().FullName;
            }

            if (suppressWarnings)
                Debug.Log($"Suppressing warnings for {callerName}");

            while (stack.Count > 0)
            {
                if (cancelledWorkIds.Contains(workId))
                {
                    yield return TerminateStack();
                    break;
                }
                var cur = stack.Peek();
                object current;
                bool movedNext = false;
                try
                {
                    movedNext = cur.MoveNext();
                    if (!movedNext)
                    {
                        if (cur is IDisposable disposeMe)
                            disposeMe.Dispose();
                        stack.Pop();
                        continue;
                    }
                    current = cur.Current;
                }
                catch (Exception e)
                {
                    // On exception, we're done. Feed it to any passed action then yield.
                    if (cur is IDisposable disposeMe)
                        disposeMe.Dispose();
                    stack.Pop(); // cur was gathered via Peek, so pop one first to avoid double-dispose
                    while (stack.Count > 0)
                    {
                        var parent = stack.Pop();
                        if (parent is IDisposable parentDispose)
                            parentDispose.Dispose();
                    }
                    Debug.LogException(e);
                    PrintStack();
                    SafeInvokeOnException(e);
                    yield break;
                }

                // ensure that nested IEnumerators are handled in the same manner
                if (current is IEnumerator nested)
                {
                    stack.Push(nested);

                    // // wait for next frame if appropriate
                    // if (movedNext)
                    //     yield return null;
                    continue;
                }

                if (workId == -1) // value -1 treated as temp value and set on the fly for naked calls
                    workIdCallTraces[-1] = trace;

                if (!suppressWarnings && current is Coroutine)
                {
                    Debug.LogWarning("Please use form: yield return MyIEnumeratorCall() rather than yield return StartCoroutine(MyIEnumeratorCall()) to allow BetterBehavior full scope. Continuing unsafe execution.");
                    PrintStack();
                }
                else if (!suppressWarnings && current is AsyncOperation)
                {
                    Debug.LogWarning("BetterBehavior cannot control behavior of nested AsyncOperation. Continuing unsafe execution.");
                    PrintStack();
                }
                else if (!suppressWarnings
                    && current != null
                    && current is not YieldInstruction
                    && current is not CustomYieldInstruction)
                {
                    Debug.LogWarning("BetterBehavior can safely consume exclusively IEnumerators, YieldInstruction, CustomYieldInstruction, and null (including nested operations). Continuing unsafe execution.");
                    PrintStack();
                }

                yield return current;

                IEnumerator TerminateStack()
                {
                    while (stack.Count > 0)
                    {
                        if (stack.Pop() is IDisposable disposeMe)
                            disposeMe.Dispose();
                    }
                    yield return null; // yield to allow RunQueue() to quit before removing workId
                    cancelledWorkIds.Remove(workId);
                }

                void PrintStack()
                {
                    if (workIdCallTraces.TryGetValue(workId, out var trace))
                        Debug.LogWarning($"Wrapped IEnumerator: {callerName}\nTrace of initial work's call:\n{trace}");
                    else
                        Debug.LogWarning($"Wrapped IEnumerator: {callerName}\nUnable to trace work's initial call stack.");
                }
            }

            void SafeInvokeOnException(Exception e)
            {
                try
                {
                    onException?.Invoke(e);
                }
                catch (Exception e2)
                {
                    // if there's a bad exception handler handed in, we need to guard against its throws as well
                    Debug.LogException(e2);
                }
            }
        }
    }

    class WorkUnit
    {
        public WorkUnit(IEnumerator targetIEnumerator, Action<Exception> OnException, bool suppressWarnings)
        {
            this.targetIEnumerator = targetIEnumerator;
            this.OnException = OnException;
            this.suppressWarnings = suppressWarnings;
        }
        public IEnumerator targetIEnumerator;
        public Action<Exception> OnException;
        public bool suppressWarnings;
    }
}
