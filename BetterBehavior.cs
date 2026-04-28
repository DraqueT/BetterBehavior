using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace com.DarisaDesigns
{
    /// <summary>
    /// This can be used as an upgrade from Monobehavior. It adds the concept of queued coroutines.
    /// Queued coroutines will be executed in sequence. Finally blocks are now GUARANTEED. The queue
    /// defaults to ID = 0, which keeps track of multiple queues, but additional queues can be accessed
    /// as well if you need multiple queues for a single object.
    /// </summary>
    public class BetterBehavior : MonoBehaviour
    {
        private readonly Dictionary<int, Coroutine> CurQueueCoroutines = new();
        private readonly Dictionary<int, WorkUnit> CurrentWork = new();
        private readonly Dictionary<int, Queue<WorkUnit>> workQueue = new();
        private readonly HashSet<long> cancelledWorkIds = new();
        private readonly Dictionary<int, long> assignedQueueWorkIds = new();
        private readonly Dictionary<long, string> workIdCallTraces = new();
        private long curWorkId = 0;


        /// <summary>
        /// Queues a coroutine to be completed after others finish
        /// </summary>
        /// <param name="work">Coroutine to queue</param>
        /// <param name="resetQueue">If set to true, cancels all prior queued events before starting next event</param>
        /// <param name="targetQueueId">This is the ID of the queue you are adding work to. Defaults to 0. Can be any arbitrary int.</param>
        /// <param name="onException">This action will consume any uncaught exception which interrupts work passed into the queue</param>
        /// <returns>Coroutine representing the full work of the queue that work was assigned to.</returns>
        public Coroutine QueueCoroutine(IEnumerator work, bool resetQueue = false, int targetQueueId = 0, Action<Exception> onException = null)
        {
            var trace = System.Environment.StackTrace.ToString(); // tied to workID in case of failures to report call location
            if (this == null || this.gameObject == null || !this.gameObject.activeInHierarchy)
                throw new Exception("Cannot run code from a disabled or inactive object");
            if (work == null)
                throw new ArgumentException("Target coroutine work cannot be null.");
            if (!workQueue.ContainsKey(targetQueueId))
                workQueue[targetQueueId] = new();

            if (resetQueue)
                ResetQueue(targetQueueId);

            workQueue[targetQueueId].Enqueue(new(work, onException));
            if (!CurQueueCoroutines.ContainsKey(targetQueueId) || CurQueueCoroutines[targetQueueId] == null)
                CurQueueCoroutines[targetQueueId] = StartCoroutine(StartSafely(RunQueue(targetQueueId, trace)));

            if (CurQueueCoroutines.TryGetValue(targetQueueId, out var myCoroutine))
                return myCoroutine;
            return null;
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
        /// Returns the target queue's driver coroutine
        /// </summary>
        /// <param name="targetQueue">Queue to fetch current work (if any) from. Defaults to 0.</param> 
        /// <returns>Currently processing coroutine of target queue. Null if none.</returns>
        public Coroutine GetCoroutineDriver(int targetQueue = 0)
        {
            if (CurQueueCoroutines.TryGetValue(targetQueue, out var targetCoroutine))
                return targetCoroutine;
            return null;
        }

        /// <summary>
        /// Returns the current work item of a target queue, if any.
        /// </summary>
        /// <param name="targetQueue">Queue to return current work from</param>
        /// <returns>Current work from queue, null if none exists</returns>
        public IEnumerator GetCurWork(int targetQueue)
        {
            if (CurrentWork.TryGetValue(targetQueue, out var work))
                return work.targetCoroutine;
            return null;
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
                    CurrentWork[targetQueue] = workQueue[targetQueue].Dequeue();
                    var workUnit = CurrentWork[targetQueue];

                    yield return StartSafely(workUnit.targetCoroutine, workUnit.OnException, workId);
                }
                CurQueueCoroutines[targetQueue] = null;
            }
            finally
            {
                CurrentWork.Remove(targetQueue);
                assignedQueueWorkIds.Remove(targetQueue);
                workIdCallTraces.Remove(workId);
            }
        }

        /// <summary>
        /// A basic replacement for StartCoroutine. Any final blocks are guaranteed to run post-uncaught exception.
        /// All exceptions are fed to the Debug log, whether or not there is an onException callback.
        /// </summary>
        /// <param name="targetWork">Work to be started.</param>
        /// <param name="onException">An action which will be fed any exception which causes the coroutine to terminate early.</param>
        /// <param name="id">Used for managed cancelation.</param>
        /// <returns>Yields like a typical coroutine in all cases except if the target coroutine experiences an uncaught exception. In this case, it yields a break.</returns>
        public IEnumerator StartSafely(IEnumerator targetWork, Action<Exception> onException = null, long workId = -1)
        {
            if (targetWork == null)
                throw new ArgumentException("Target coroutine targetWork cannot be null.");
            var stack = new Stack<IEnumerator>();
            stack.Push(targetWork);

            while (stack.Count > 0)
            {
                if (cancelledWorkIds.Contains(workId))
                {
                    yield return TerminateStack();
                    break;
                }
                var cur = stack.Peek();
                object current;
                try
                {
                    if (!cur.MoveNext())
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

                // ensure that nested coroutines are handled in the same manner
                if (current is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }
                else if (current is Coroutine coroutine)
                {
                    Debug.LogWarning("Please use form: yield return MyIEnumeratorCall() rather than yield return StartCoroutine(MyIEnumeratorCall()) to allow BetterBehavior full scope. Continuing unsafe execution.");
                    PrintStack();
                }
                else if (current is AsyncOperation asyncOp)
                {
                    Debug.LogWarning("BetterBehavior cannot control behavior of nested AsyncOperation. Continuing unsafe execution.");
                    PrintStack();
                }
                else if (current != null
                    && current is not YieldInstruction
                    && current is not CustomYieldInstruction)
                {
                    Debug.Log("BetterBehavior can safely consume exclusively IEnumerators, YieldInstruction, CustomYieldInstruction, and null (including nested operations). Continuing unsafe execution.");
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
                        Debug.LogWarning($"Trace of initial coroutine call:\n{trace}");
                    else
                        Debug.LogWarning("Unable to trace coroutine's initial call stack.");
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
        public WorkUnit(IEnumerator targetCoroutine, Action<Exception> OnException)
        {
            this.targetCoroutine = targetCoroutine;
            this.OnException = OnException;
        }
        public IEnumerator targetCoroutine;
        public Action<Exception> OnException;
    }
}
