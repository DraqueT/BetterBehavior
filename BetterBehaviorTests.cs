using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using NUnit.Framework.Internal;
using UnityEngine;
using UnityEngine.TestTools;

namespace com.DarisaDesigns
{
    public class TestBetterBehavior
    {
        private bool finalBlockRan = false;
        private bool parentNestedFinalBlockRan = false;
        private bool basicFunctionRan = false;
        GameObject testerParent;
        BetterBehavior tester;

        [SetUp]
        public void Setup()
        {
            finalBlockRan = false;
            parentNestedFinalBlockRan = false;
            basicFunctionRan = false;
            testerParent = new GameObject();
            tester = testerParent.AddComponent<BetterBehavior>();
        }

        [TearDown]
        public void TearDown()
        {
            if (testerParent != null)
                UnityEngine.Object.Destroy(testerParent);
        }

        [UnityTest]
        public IEnumerator TestBasicRun()
        {
            yield return tester.StartSafely(BasicRun());
            Assert.IsTrue(basicFunctionRan);
        }

        [UnityTest]
        public IEnumerator TestBasicQueueing()
        {
            yield return tester.QueueCoroutine(BasicRun());
            Assert.IsTrue(basicFunctionRan);
        }

        [UnityTest]
        public IEnumerator TestFinalBlockRunsAfterException()
        {
            LogAssert.Expect(LogType.Exception, "Exception: ExpectedException");
            yield return tester.QueueCoroutine(ExceptionThrown());
            Assert.IsTrue(finalBlockRan);
        }

        [UnityTest]
        public IEnumerator TestNestedFinalBlocksRunsAfterException()
        {
            LogAssert.Expect(LogType.Exception, "Exception: ExpectedException");
            yield return tester.QueueCoroutine(NestedExceptionThrown());
            Assert.IsTrue(parentNestedFinalBlockRan);
        }

        [UnityTest]
        public IEnumerator TestIsQueueDone()
        {
            Assert.IsTrue(tester.IsQueueDone());
            tester.QueueCoroutine(BasicRun());
            Assert.IsFalse(tester.IsQueueDone());
            yield return null;
            Assert.IsTrue(tester.IsQueueDone());
        }

        [UnityTest]
        public IEnumerator TestSequentialQueuedRuns()
        {
            var curRunning = false;
            var sequence = 0;
            bool[] testOrderedQueue = new bool[10];

            for (var i = 0; i < testOrderedQueue.Length; i++)
                tester.QueueCoroutine(setValTrue(i));
            while (!tester.IsQueueDone())
                yield return null;
            foreach (var queueRan in testOrderedQueue)
                Assert.IsTrue(queueRan);

            IEnumerator setValTrue(int i)
            {
                Assert.AreEqual(i, sequence); // ensure proper order
                Assert.IsFalse(curRunning); // ensure no run overlap
                curRunning = true;
                sequence++;
                yield return null;
                testOrderedQueue[i] = true;
                curRunning = false;
            }
        }

        [UnityTest]
        public IEnumerator TestConcurrantQueuedRuns()
        {
            bool[] testOrderedQueue = new bool[10];

            for (var i = 0; i < testOrderedQueue.Length; i++)
                tester.QueueCoroutine(setValTrue(i), targetQueueId: i);
            for (var i = 0; i < testOrderedQueue.Length; i++)
                while (!tester.IsQueueDone(i))
                    yield return null;
            foreach (var queueRan in testOrderedQueue)
                Assert.IsTrue(queueRan);

            IEnumerator setValTrue(int i)
            {
                yield return null;
                testOrderedQueue[i] = true;
            }
        }

        [UnityTest]
        public IEnumerator TestResetQueue()
        {
            bool[] testOrderedQueue = new bool[2];

            tester.QueueCoroutine(setValTrue(0));
            tester.QueueCoroutine(setValTrue(1), resetQueue: true);
            while (!tester.IsQueueDone())
                yield return null;
            Assert.IsFalse(testOrderedQueue[0]);
            Assert.IsTrue(testOrderedQueue[1]);

            IEnumerator setValTrue(int i)
            {
                yield return null;
                testOrderedQueue[i] = true;
            }
        }

        [UnityTest]
        public IEnumerator TestResetQueueNestedFinally()
        {
            var firstFinally = false;
            var secondFinally = false;

            tester.QueueCoroutine(parentCoroutine());
            tester.ResetQueue();
            yield return null;
            Assert.IsTrue(firstFinally);
            Assert.IsTrue(secondFinally);

            IEnumerator parentCoroutine()
            {
                try
                {
                    yield return childCoroutine();
                }
                finally
                {
                    firstFinally = true;
                }
            }

            IEnumerator childCoroutine()
            {
                try
                {
                    yield return null;
                }
                finally
                {
                    secondFinally = true;
                }
            }
        }

        [UnityTest]
        public IEnumerator TestExceptionHandlerHit()
        {
            LogAssert.Expect(LogType.Exception, "Exception: ExpectedException");
            Exception resultException = null;
            void exceptionHandler(Exception e) => resultException = e;
            yield return tester.QueueCoroutine(ExceptionThrown(), onException: exceptionHandler);

            Assert.IsNotNull(resultException);
            Assert.AreEqual("ExpectedException", resultException.Message);
        }

        [UnityTest]
        public IEnumerator TestBadExceptionHandler()
        {
            LogAssert.Expect(LogType.Exception, "Exception: ExpectedException");
            LogAssert.Expect(LogType.Exception, "Exception: Also-Expected");
            static void exceptionHandler(Exception e) => throw new Exception("Also-Expected");
            yield return tester.QueueCoroutine(ExceptionThrown(), onException: exceptionHandler);
        }

        [UnityTest]
        public IEnumerator TestQueueWorksPostBadExceptionHandler()
        {
            LogAssert.Expect(LogType.Exception, "Exception: ExpectedException");
            LogAssert.Expect(LogType.Exception, "Exception: Also-Expected");
            static void exceptionHandler(Exception e) => throw new Exception("Also-Expected");
            yield return tester.QueueCoroutine(ExceptionThrown(), onException: exceptionHandler);
            yield return tester.QueueCoroutine(BasicRun());
            Assert.IsTrue(basicFunctionRan);
        }

        [UnityTest]
        public IEnumerator TestFedCorotine()
        {
            var warning = new Regex(".*Continuing unsafe execution..*");
            LogAssert.Expect(LogType.Warning, warning);
            yield return tester.QueueCoroutine(parent());

            IEnumerator parent()
            {
                yield return null;
                yield return tester.StartCoroutine(child());
            }

            IEnumerator child()
            {
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator TestAllAllowedTypes()
        {
            var complete = false;
            yield return tester.QueueCoroutine(testTypes());
            Assert.IsTrue(complete);

            IEnumerator testTypes()
            {
                yield return new WaitForSeconds(0.001f);
                yield return new WaitForSecondsRealtime(0.001f);
                yield return new WaitForFixedUpdate();
                yield return new WaitForEndOfFrame();
                yield return null;
                complete = true;
            }
        }

        private IEnumerator NestedExceptionThrown()
        {
            try
            {
                yield return null;
                yield return ExceptionThrown();
            }
            finally
            {
                parentNestedFinalBlockRan = true;
            }
        }

        private IEnumerator ExceptionThrown()
        {
            yield return null;
            try
            {
                throw new System.Exception("ExpectedException");
            }
            finally
            {
                finalBlockRan = true;
            }
        }

        private IEnumerator BasicRun()
        {
            yield return null;
            basicFunctionRan = true;
        }
    }
}
