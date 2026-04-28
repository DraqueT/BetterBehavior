BetterBehavior is a class that extends MonoBehavior to address problems I have encountered. It guarantees finally execution for work expressed as nested IEnumerator chains. Engine-managed coroutine handles (Coroutine) and other opaque async primitives will run, but result in a warning, as code executed under them cannot be guaranteed.

1) MonoBehavior does not properly respect try/finally blocks. If code his an exception or for any unexpected reason ceases execution, the finally block will NOT be hit until the parent object of the MonoBehavior is disabled or destroyed.
  - BetterBehavior corrects this. Coroutines running under it are guaranteed to have try/finally blocks which execute in the expected manner.
2) MonoBehavior does not have any kind of queuing system built in which will sequentially run coroutines.
  - BetterBehavior's QueueCoroutine() method largely replaces StartCoroutine(), and allows for single file queuing of coroutines.
  - Arbitrarily many queues may be addressed via an optional id parameter (defaults to 0)
  - Can fetch a yieldable object representing the total work of a queue by queueId (defaults to 0)
  - Can fetch a yieldable object representing the currently processing coroutine of a queue by queueId (defaults to 0)
3) Unity does not have any way to gather the exception which led to the failure of a Coroutine.
  - BetterBehavior allows an Action to be passed in which processes the exception causing your coroutine to fail in any way you like.
  - BetterBehavior guards against actions which, themselves throw, but has no mechanism to directly return the exceptions.
  - Exceptions from both wrapped coroutines and bad exception handling actions are fed to the default UnityEngine logger.
4) Unity does not have any way to provide insight into the initial StartCoroutine() call which led to an unhandled Exception.
  - BetterBehavior records the stack location of queued IEnumerators as they are added, and will log this if an unhandled Exception is thrown within managed code.

Of Note:
- This will account for all nested coroutines *of disposable types*. In the case of things like Async processes/non-IEnumerator type yield instructions, BetterBehavior will not be able to make the same guarantees, and will post warnings.
- Sequence and non-overlap is guaranteed within a given queue.
- If multiple queues are in use, no guarantees are made as to how time is distributed. This still depends on unity yield handling.

LIMITATIONS:
- BetterBehavior is currently cannot make guarantees within Coroutines or AsyncOperations. This applies to both the work fed to it directly and all downstream yielded objects.
- If Coroutines or AsyncOperations are encountered, crashes within them will simply hang as they would normally, with no visibility returned to the passed onException Action.
  - The reason for this is that types such as Coroutine and AsyncOperation cannot be unwound and executed step by step/have their exceptions caught and managed.
- The following yield types (and descendents) are accepted and passed, as they do not execute custom code: YieldInstruction, CustomYieldInstruction, null
- yield types such as WaitUntil and WaitWhile are accepted without warning, but HIDE UNMANAGED CODE. Any uncaught Exceptions thrown by these will result in hanging and unreported code. It is HIGHLY recommended that instead of these, patterns which simply use '''while (<case>) yield return null''' to avoid the problem altogether.

I might add more features to this if requested, but the try/finally thing was the one that always burned me, especially if it was some downstream effect.
