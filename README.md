# TypeSafeAI for .NET

A .NET SDK for the [TypeSafe AI](https://docs.typesafe.ai) System One API. Ask small, typed judgments (yes/no, pick one, rate on a scale) about text or structured state and get calibrated probabilities back that your code can act on.

> This is a community SDK and is not affiliated with or endorsed by TypeSafe AI.

| Package | Purpose |
| --- | --- |
| `TypeSafeAI` | Core client: `TypeSafeClient`, typed `QuestionSet`, retries, `HttpClientFactory` and DI support. |
| `TypeSafeAI.Extensions.AI` | Microsoft.Extensions.AI integration: guardrail and routing chat-client middleware, an `AIFunction` bridge, and an `IEvaluator` adapter. |

Documentation lives in this README until the first release.
