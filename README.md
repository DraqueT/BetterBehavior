UNDERGOING SOME DEBUGING, PATIENCE APPRECIATED

BetterBehavior is a class that extends MonoBehavior to address problems I have encountered.

1) MonoBehavior does not properly respect try/finally blocks. If code his an exception or for any unexpected reason ceases execution, the finally block will NOT be hit until the parent object of the MonoBehavior is disabled or destroyed.
  - BetterBehavior corrects this. Coroutines running under it are guaranteed to have try/finally blocks which execute in the expected manner.
2) MonoBehavior does not have any kind of queuing system built in which will sequentially run coroutines.
  - BetterBehavior's QueueCoroutine() method largely replaces StartCoroutine(), and allows for single file queuing of coroutines.
  - Arbitrarily many queues may be addressed via an optional id parameter (defaults to 0)

I might add more features to this if requested, but the try/finally thing was the one that always burned me, especially if it was some downstream effect. So I wanted to create something that addressed the issue. And I did.
