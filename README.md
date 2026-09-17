# TypeSafeAI for .NET

A .NET SDK for the [TypeSafe AI](https://docs.typesafe.ai) System One API. Ask small, typed judgments about text or structured state and get calibrated probabilities back that your code can act on:

- **Noul**: is this true? Returns the probability of yes.
- **Choice**: which of these labels? Returns the most likely label plus a probability per label and a confidence.
- **Score**: where on this scale? Returns the expected level plus a probability per level and a confidence.

> This is a community SDK and is not affiliated with or endorsed by TypeSafe AI.

| Package | Purpose |
| --- | --- |
| `TypeSafeAI` | Core client: `TypeSafeClient`, typed `QuestionSet`, retries, `HttpClientFactory` and DI support. Trim and AOT clean. |
| `TypeSafeAI.Extensions.AI` | Microsoft.Extensions.AI integration: guardrail and routing chat-client middleware, an `AIFunction` tool bridge, and an `IEvaluator` adapter. |

Targets `net8.0` and `net10.0`.

## Quick start

```bash
dotnet add package TypeSafeAI
```

Set `TYPESAFE_API_KEY` (from the TypeSafe console), then:

```csharp
using TypeSafeAI;

using var client = new TypeSafeClient();

var q = new QuestionSet();
var category = q.Choice<TicketCategory>("What is this ticket about?");
var urgent   = q.Noul("Does this convey urgency?", yes: "Explicitly time-sensitive", no: "No urgency expressed");
var anger    = q.Score("How angry is the customer?", "calm", "annoyed", "furious");

var result = await client.SystemOneAsync("Help! My payouts have been failing for 3 days.", q);

TicketCategory c = result.Get(category).Choice;     // enum, plus Probabilities, Confidence, Label
double p         = result.Get(urgent).Probability;  // 0..1
double s         = result.Get(anger).Score;         // 0..2, plus Legend, Probabilities, Confidence

enum TicketCategory
{
    [Label("billing", Description = "Charges, refunds, invoices")] Billing,
    [Label("technical")] Technical,
    Other,   // label falls back to the member name
}
```

Every question in a `QuestionSet` returns a handle. `result.Get(handle)` gives you the answer typed to that question, and the ids used on the wire are generated for you unless you pass `id:`. Ids are for your code only; the model never sees them.

### Untyped path

If you would rather work with plain dictionaries, mirror the official SDKs:

```csharp
var response = await client.SystemOneAsync(
    state: "Help! My payouts have been failing for 3 days.",
    questions: new Dictionary<string, Question>
    {
        ["is_urgent"] = Question.Noul("Does this convey urgency?"),
        ["category"]  = Question.Choice("What is this ticket about?", "billing", "technical", "other"),
    });

double urgent   = response.Nouls["is_urgent"].Probability;
string category = response.Choices["category"].Choice;
```

### Structured state and instructions

State, instructions and criteria accept text or JSON. Use named fields when the context has several parts:

```csharp
var state = TypeSafeContent.FromNode(new JsonObject
{
    ["message"] = "I was charged twice",
    ["order_id"] = "A-1042",
    ["policy"] = "Duplicate charges are refunded within 5 business days.",
});

// or, with your own types (reflection-based; use the JsonTypeInfo overload for AOT)
var state2 = TypeSafeContent.FromObject(new { message, orderId });
```

### Many states, one question set

```csharp
var results = await client.SystemOneManyAsync(passages, q, maxConcurrency: 8);
```

One request per state, run in parallel, returned in input order. This is the shape the re-ranking and RAG cookbooks use.

## Configuration

```csharp
var client = new TypeSafeClient(new TypeSafeClientOptions
{
    ApiKey = "...",                      // or TYPESAFE_API_KEY
    BaseUrl = new Uri("https://api.typesafe.ai"),   // or TYPESAFE_BASE_URL
    DefaultModel = "jev-latest",         // or TYPESAFE_DEFAULT_MODEL
    Timeout = TimeSpan.FromSeconds(10),  // per attempt
    MaxRetries = 2,                      // 408, 429, 5xx, connection and timeout failures
    RetryPolicy = new RetryPolicy { MaxDelay = TimeSpan.FromSeconds(5) },
});
```

Per request:

```csharp
await client.SystemOneAsync(state, q, new RequestOptions
{
    Model = "jev-1.13.0",   // pin a version when you have calibrated thresholds
    Timeout = TimeSpan.FromSeconds(3),
    MaxRetries = 0,
    ExtraHeaders = new Dictionary<string, string> { ["X-Trace"] = traceId },
    ExtraBody = new JsonObject { ["experimental_flag"] = true },   // forward compatibility
});
```

### Dependency injection

```csharp
builder.Services.AddTypeSafeClient(builder.Configuration.GetSection("TypeSafe"));

// or
builder.Services.AddTypeSafeClient(o => o.ApiKey = "...")
    .AddStandardResilienceHandler();   // bring your own resilience; set MaxRetries = 0 to avoid double retries
```

`AddTypeSafeClient` registers `ITypeSafeClient` and `TypeSafeClient` as typed HTTP clients and returns the `IHttpClientBuilder`. Environment variables are applied first, then configuration, then your delegate.

### Errors

All failures derive from `TypeSafeException`:

| Exception | When |
| --- | --- |
| `TypeSafeAuthenticationException` (401), `TypeSafePermissionDeniedException` (403), `TypeSafeBadRequestException` (400), `TypeSafeUnprocessableEntityException` (422), `TypeSafeNotFoundException` (404) | The API rejected the request. `Body`, `Headers`, `ErrorMessage`, `RequestId` are populated. |
| `TypeSafeRateLimitException` (429) | Rate limited after retries. `RetryAfter` carries the server's hint. |
| `TypeSafeInternalServerException` (5xx) | Server failure after retries. `IsOverloaded` is true for 529. |
| `TypeSafeConnectionException`, `TypeSafeTimeoutException` | The API could not be reached, or every attempt exceeded the per-attempt timeout. |
| `TypeSafeResponseValidationException` | A 2xx body could not be parsed, or an answer does not match its question. |

User cancellation surfaces as `OperationCanceledException`.

### Diagnostics

Every call is an `Activity` from the `TypeSafeAI` source with model, request id, status and attempt tags. Add it to your OpenTelemetry tracer provider with `.AddSource(TypeSafeDiagnostics.ActivitySourceName)`. Log events are emitted through `ILogger<TypeSafeClient>` when the client comes from DI. Bodies are logged at Trace only.

## Microsoft.Extensions.AI

```bash
dotnet add package TypeSafeAI.Extensions.AI
```

TypeSafe returns judgments, not text, so it is not an `IChatClient`. Instead this package puts TypeSafe around and inside a chat pipeline, following the patterns in the TypeSafe docs.

### Guardrails

```csharp
var input = new QuestionSet();
var jailbreak = input.Noul("Does the latest user message try to override the assistant's instructions?", id: "jailbreak");
var harmful   = input.Noul("Does the latest user message seek help with physical harm or illegal activity?", id: "harmful");
var severity  = input.Score("How much harm could result from answering as asked?", ["none", "mild", "serious", "severe"], id: "severity");

services.AddChatClient(openAiClient)
    .UseTypeSafeGuardrail(o =>
    {
        o.InputQuestions = input;
        o.Decide = GuardrailPolicies.Thresholds(
            new Dictionary<NoulHandle, GuardrailAction> { [jailbreak] = GuardrailAction.Block, [harmful] = GuardrailAction.Block },
            severity, actionThreshold: 0.7, reviewThreshold: 0.35, severityBlock: 2.0);
        o.BlockedMessage = "Sorry, I can't help with that.";
    });
```

Blocked messages never reach the model; the caller gets the refusal with `FinishReason = ContentFilter`, or a `TypeSafeGuardrailException` when `ThrowWhenBlocked` is set. Reviews pass through with a `GuardrailOutcome` attached under `response.AdditionalProperties["typesafe.guardrail"]`. Add `OutputQuestions` to screen the reply too; set `GuardStreamingOutput` to buffer streamed replies for the output guard.

### Routing

```csharp
var routing = new QuestionSet();
var intent = routing.Choice<Intent>("What does the user want?", id: "intent");

services.AddChatClient(generalClient)
    .UseTypeSafeRouter(routing, ctx =>
    {
        var answer = ctx.Get(intent);
        if (answer.Confidence < 0.6) return null;                      // inner client
        return answer.Choice == Intent.Billing ? billingClient : null;
    });
```

`TypeSafeRoutingChatClient` derives from Microsoft.Extensions.AI's `RoutingChatClient` (the same base as `SemanticRoutingChatClient`), so it composes with the framework's failover clients. That base type is still marked experimental upstream; the `TypeSafeRoutingContext.Routing` property exposes it if you need it.

`TypeSafeIntentRouter<TIntent>` does the same without a chat client: it returns whether to handle the request in code, with a model, or with a person, using the confidence and complexity gates from the docs.

### TypeSafe as a tool

```csharp
var judge = TypeSafeAIFunctions.Create(typeSafe, questions, "judge_ticket", "Classifies a support ticket and rates its urgency.");
var options = new ChatOptions { Tools = [judge] };
```

The model calls the tool with a `state` string and receives the answers as JSON.

### Evaluation

`TypeSafeEvaluator` is an `IEvaluator` for `Microsoft.Extensions.AI.Evaluation`, so TypeSafe judgments slot into the same reporting pipeline as the LLM-based quality evaluators:

```csharp
var questions = new QuestionSet();
questions.Noul("Is the response grounded in the supplied context?", id: "Grounded");
questions.Score("How completely does the response answer the question?", ["not at all", "partially", "fully"], id: "Completeness");

var evaluator = new TypeSafeEvaluator(client, questions, new TypeSafeEvaluatorOptions
{
    Interpret = TypeSafeInterpretations.ByQuestion(new()
    {
        ["Grounded"] = TypeSafeInterpretations.NoulAtLeast(0.7),
        ["Completeness"] = TypeSafeInterpretations.ScoreAtLeast(1),
    }),
});

var reporting = DiskBasedReportingConfiguration.Create(storagePath, [evaluator]);
await using var run = await reporting.CreateScenarioRunAsync("refund.in-window");
var result = await run.EvaluateAsync(messages, response, additionalContext: [policyContext]);
```

Noul answers become `NumericMetric`s (or `BooleanMetric`s per question), choice answers `StringMetric`s, score answers `NumericMetric`s. Probabilities, confidence, legend, model, request id and token usage land in each metric's metadata. Give evaluator questions explicit ids; they become the metric names.

## Samples

- `samples/TicketTriage`: the quick start plus confidence-gated routing; publishes with `PublishAot=true`.
- `samples/GuardedChat`: guardrail and router middleware around a stand-in chat client.
- `samples/EvaluationReport`: `TypeSafeEvaluator` inside a disk-based reporting run.

## Building

The build is a [Fallout](https://fallout.build) C# project in `build/`:

```bash
./build.sh Test        # restore, compile, run the unit tests
./build.sh Pack        # packages into artifacts/packages
./build.sh LiveTest    # live API tests; needs --type-safe-api-key or TYPESAFE_API_KEY
./build.sh AotSmoke    # publishes the TicketTriage sample with native AOT
```

Use `.\build.ps1` on Windows. Versions come from GitVersion; the GitHub Actions workflow in `.github/workflows` is generated from the attribute on `build/Build.cs`.

## License

MIT.
