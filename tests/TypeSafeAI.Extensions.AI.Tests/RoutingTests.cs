using Microsoft.Extensions.AI;
using TypeSafeAI.Extensions.AI.Tests.Fakes;

namespace TypeSafeAI.Extensions.AI.Tests;

public enum SupportIntent
{
    [Label("order_status")]
    OrderStatus,

    [Label("complaint")]
    Complaint,

    [Label("product_question")]
    ProductQuestion,
}

public class RoutingTests
{
    [Test]
    public async Task Selector_picks_a_client_from_typed_answers_and_response_is_annotated()
    {
        var q = new QuestionSet();
        var intent = q.Choice<SupportIntent>("What does the customer want?", id: "intent");
        var typeSafe = FakeTypeSafeClient.Answering(("intent", FakeTypeSafeClient.Choice("complaint", 0.9, ("complaint", 0.9), ("order_status", 0.1))));
        var general = new EchoChatClient("general");
        var specialist = new EchoChatClient("specialist");

        using var client = new TypeSafeRoutingChatClient(typeSafe, q, ctx =>
            ctx.Get(intent).Choice == SupportIntent.Complaint && ctx.Get(intent).Confidence > 0.5 ? specialist : null, general);

        var response = await client.GetResponseAsync("I am unhappy with my order");

        await Assert.That(response.Text).IsEqualTo("specialist");
        await Assert.That(general.Calls.Count).IsEqualTo(0);
        var result = (QuestionSetResult)response.AdditionalProperties![TypeSafeRoutingChatClient.ResultPropertyName]!;
        await Assert.That(result.Get(intent).Choice).IsEqualTo(SupportIntent.Complaint);
    }

    [Test]
    public async Task Router_composes_with_the_framework_failover_client_and_does_not_dispose_selected_clients()
    {
        var q = new QuestionSet();
        var intent = q.Choice<SupportIntent>("intent?", id: "intent");
        var typeSafe = FakeTypeSafeClient.Answering(("intent", FakeTypeSafeClient.Choice("complaint", 0.9, ("complaint", 0.9))));
        var general = new EchoChatClient("general");
        var specialist = new EchoChatClient("specialist");

        var router = new TypeSafeRoutingChatClient(typeSafe, q, ctx => ctx.Get(intent).Choice == SupportIntent.Complaint ? specialist : null, general);
        var response = await router.GetResponseAsync("complaint");
        router.Dispose();

        await Assert.That(response.Text).IsEqualTo("specialist");
        // Selected clients stay usable after the router is disposed.
        await Assert.That((await specialist.GetResponseAsync("again")).Text).IsEqualTo("specialist");
    }

    [Test]
    public async Task Null_selection_uses_inner_client_and_streaming_routes_too()
    {
        var q = new QuestionSet();
        var intent = q.Choice<SupportIntent>("intent?", id: "intent");
        var typeSafe = FakeTypeSafeClient.Answering(("intent", FakeTypeSafeClient.Choice("order_status", 0.95, ("order_status", 0.95))));
        var general = new EchoChatClient("general reply");
        var specialist = new EchoChatClient("specialist reply");

        var client = new ChatClientBuilder(general)
            .UseTypeSafeRouter(typeSafe, q, ctx => ctx.Get(intent).Choice == SupportIntent.Complaint ? specialist : null, o => o.AnnotateResponses = false)
            .Build();

        var response = await client.GetResponseAsync("where is my order?");
        await Assert.That(response.Text).IsEqualTo("general reply");
        await Assert.That(response.AdditionalProperties).IsNull();

        var streamed = await client.GetStreamingResponseAsync("where is my order?").ToChatResponseAsync();
        await Assert.That(streamed.Text.Trim()).IsEqualTo("general reply");
        await Assert.That(general.StreamingCalls).IsEqualTo(1);
        await Assert.That(typeSafe.Requests.Count).IsEqualTo(2);
    }

    [Test]
    [Arguments(0.3, 0.0, 0.9, RouteTarget.Human)]
    [Arguments(0.9, 0.0, 0.9, RouteTarget.Code)]
    [Arguments(0.9, 1.5, 0.9, RouteTarget.Human)]
    [Arguments(0.9, 0.5, 0.2, RouteTarget.Human)]
    public async Task Intent_router_applies_confidence_and_complexity_gates(double intentConfidence, double complexity, double complexityConfidence, RouteTarget expected)
    {
        var typeSafe = FakeTypeSafeClient.Answering(
            ("intent", FakeTypeSafeClient.Choice("order_status", intentConfidence, ("order_status", intentConfidence))),
            ("complexity", FakeTypeSafeClient.Score(complexity, complexityConfidence, 0.5, 0.5, 0)));
        var router = new TypeSafeIntentRouter<SupportIntent>(typeSafe, "What does the customer want?", ["simple lookup", "needs judgment", "edge case"]);
        router.CodeIntents.Add(SupportIntent.OrderStatus);

        var route = await router.RouteAsync("where is my order?");

        await Assert.That(route.Target).IsEqualTo(expected);
        await Assert.That(route.Intent).IsEqualTo(SupportIntent.OrderStatus);
        await Assert.That(route.Complexity!.Score).IsEqualTo(complexity);
    }

    [Test]
    public async Task Intent_router_sends_model_intents_to_model_and_supports_extra_questions()
    {
        var typeSafe = FakeTypeSafeClient.Answering(
            ("intent", FakeTypeSafeClient.Choice("product_question", 0.8, ("product_question", 0.8))),
            ("urgent", FakeTypeSafeClient.Noul(0.7)));
        var router = new TypeSafeIntentRouter<SupportIntent>(typeSafe, "intent?");
        var urgent = router.Questions.Noul("urgent?", id: "urgent");

        var route = await router.RouteAsync("does it come in blue?");

        await Assert.That(route.Target).IsEqualTo(RouteTarget.Model);
        await Assert.That(route.Complexity).IsNull();
        await Assert.That(route.Result.Get(urgent).Probability).IsEqualTo(0.7);
    }
}
