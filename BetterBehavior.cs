/**
* Better Behavior
*
* Copyright 2026 Draque Thompson
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
* 	http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*
*/

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
    public abstract class BetterBehavior : MonoBehaviour
    {
        private readonly Dictionary<int, Coroutine> AnimCoroutine = new();
        private readonly Dictionary<int, IEnumerator> CurrentWork = new();
        private readonly Dictionary<int, Queue<IEnumerator>> animQueue = new();

        /// <summary>
        /// Queues a coroutine to be completed after others finish
        /// </summary>
        /// <param name="work">Coroutine to queue</param>
        /// <param name="resetQueue">If set to true, cancels all prior queued events before starting next event</param>
        /// <returns></returns>
        protected Coroutine QueueCoroutine(IEnumerator work, bool resetQueue = false, int targetQueue = 0)
        {
            if (!animQueue.ContainsKey(targetQueue))
                animQueue[targetQueue] = new();

            if (resetQueue)
            {
                foreach (var animation in animQueue[targetQueue])
                {
                    StopCoroutine(animation);
                    if (animation is IDisposable disposeOfMe)
                        disposeOfMe.Dispose();
                }
                animQueue[targetQueue].Clear();
                if (CurrentWork.ContainsKey(targetQueue) && CurrentWork[targetQueue] is IDisposable disposeMe)
                    disposeMe.Dispose();
                if (AnimCoroutine.ContainsKey(targetQueue) && AnimCoroutine[targetQueue] != null)
                    StopCoroutine(AnimCoroutine[targetQueue]);
                AnimCoroutine.Remove(targetQueue);
                CurrentWork.Remove(targetQueue);
            }
            animQueue[targetQueue].Enqueue(work);
            if (this != null && (!AnimCoroutine.ContainsKey(targetQueue) || AnimCoroutine[targetQueue] == null))
                AnimCoroutine[targetQueue] = StartCoroutine(RunQueue(targetQueue));
            return AnimCoroutine[targetQueue];
        }

        public Coroutine GetAnimCoroutine(int targetQueue = 0)
        {
            if (AnimCoroutine.ContainsKey(targetQueue))
                return AnimCoroutine[targetQueue];
            return null;
        }

        private IEnumerator RunQueue(int targetQueue)
        {
            try
            {
                while (animQueue[targetQueue].Count > 0 && this.gameObject != null)
                {
                    CurrentWork[targetQueue] = animQueue[targetQueue].Dequeue();
                    yield return StartCoroutine(CurrentWork[targetQueue]);
                }
                AnimCoroutine[targetQueue] = null;
            }
            finally
            {
                CurrentWork.Remove(targetQueue);
            }
        }
    }
}
