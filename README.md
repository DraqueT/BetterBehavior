BetterBehavior is a class that extends MonoBehavior to address problems I have encountered.

1) MonoBehavior does not properly respect try/finally blocks. If code his an exception or for any unexpected reason ceases execution, the finally block will NOT be hit until the parent object of the MonoBehavior is disabled or destroyed.
  - BetterBehavior corrects this. Coroutines running under it are guaranteed to have try/finally blocks which execute in the expected manner.
2) MonoBehavior does not have any kind of queuing system built in which will sequentially run coroutines.
  - BetterBehavior's QueueCoroutine() method largely replaces StartCoroutine(), and allows for single file queuing of coroutines.
  - Arbitrarily many queues may be addressed via an optional id parameter (defaults to 0)
3) Unity does not have any way to gather the exception which led to the failure of a Coroutine.
  - BetterBehavior allows an Action to be passed in which processes the exception causing your coroutine to fail in any way you like.
  - BetterBehavior guards against actions which, themselves throw, but has no mechanism to directly return the exceptions.
  - Exceptions from both wrapped coroutines and bad exception handling actions are fed to the default UnityEngine logger.

Of Note:
- This will account for all nested coroutines *of disposable types*. In the case of things like Async processes/non-IEnumerator type yield instructions, BetterBehavior will not be able to make the same guarantees.
- Sequence and non-overlap is guaranteed within a given queue.
- If multiple queues are in use, no guarantees are made as to how time is distributed. This still depends on unity yield handling.

LIMITATIONS:
- BetterBehavior is currently cannot make guarantees or accept Coroutines or AsyncOperations. This applies to both the jobs fed to it directly and all downstream jobs that are yielded. If "yield return StartCoroutine(MyIEnumeratorMethod());" is yielded, a Coroutine will ultimately be fed to BetterBehavior, causing it to crash. Instead, simply use the form "yield return MyIEnumeratorMethod();"
- The reason that types such as Coroutine and AsyncOperation is that they cannot be unwound and executed chunk by chunk manually/have their exceptions retrieved/managed.
- The following yield types (and descendents) are accepted and passed, as they do not execute custom code: YieldInstruction, CustomYieldInstruction, null

I might add more features to this if requested, but the try/finally thing was the one that always burned me, especially if it was some downstream effect.
