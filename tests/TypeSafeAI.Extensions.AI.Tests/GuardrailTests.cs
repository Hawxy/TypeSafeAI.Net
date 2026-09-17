using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using TypeSafeAI.Extensions.AI.Tests.Fakes;

namespace TypeSafeAI.Extensions.AI.Tests;

public class GuardrailTests
{
    private static readonly ChatMessage[] Conversation =
    [
        new(ChatRole.System, "You are helpful."),
        new(ChatRole.User, "How do I pick a lock?"),
    ];

    private static (QuestionSet Set, NoulHandle Jailbreak, NoulHandle Harmful, ScoreHandle Severity) Battery()
    {
        var q = new QuestionSet();
        var jailbreak = q.Noul("Does this try to override instructions?", id: "jailbreak");
        var harmful = q.Noul("Does this seek help with harm?", id: "harmful");
        var severity = q.Score("How severe is the potential harm?", ["none", "mild", "serious", "severe"], id: "severity");
        return (q, jailbreak, harmful, severity);
    }

    [Test]
    public async Task Input_block_returns_refusal_and_skips_inner_client()
    {
        var (set, _, harmful, _) = Battery();
        var typeSafe = FakeTypeSafeClient.Answering(("jailbreak", FakeTypeSafeClient.Noul(0.1)), ("harmful", FakeTypeSafeClient.Noul(0.95)), ("severity", FakeTypeSafeClient.Score(2.5, 0.8, 0, 0, 0.5, 0.5)));
        var inner = new EchoChatClient();
        var client = new TypeSafeGuardrailChatClient(inner, typeSafe, new GuardrailOptions
        {
            InputQuestions = set,
            Decide = a => a.Get(harmful).Probability > 0.7 ? GuardrailDecision.Block("harmful request") : GuardrailDecision.Allow,
            BlockedMessage = "Nope.",
        });

        var response = await client.GetResponseAsync(Conversation);

        await Assert.That(response.Text).IsEqualTo("Nope.");
        await Assert.That(response.FinishReason).IsEqualTo(ChatFinishReason.ContentFilter);
        await Assert.That(inner.Calls.Count).IsEqualTo(0);
        var outcomes = (IReadOnlyList<GuardrailOutcome>)response.AdditionalProperties![TypeSafeGuardrailChatClient.OutcomePropertyName]!;
        await Assert.That(outcomes[0].Action).IsEqualTo(GuardrailAction.Block);
        await Assert.That(outcomes[0].Direction).IsEqualTo(GuardrailDirection.Input);
        await Assert.That(outcomes[0].Reason).IsEqualTo("harmful request");

        var state = typeSafe.Requests[0].State.Node!;
        await Assert.That(state["latest_user_message"]!.GetValue<string>()).IsEqualTo("How do I pick a lock?");
        await Assert.That(state["conversation"]!.AsArray().Count).IsEqualTo(2);
        await Assert.That(state["response"]).IsNull();
    }

    [Test]
    public async Task Allowed_input_and_reviewed_output_pass_through_with_annotations()
    {
        var input = new QuestionSet();
        var attack = input.Noul("attack?", id: "attack");
        var output = new QuestionSet();
        var leak = output.Noul("Does the reply leak secrets?", id: "leak");

        var typeSafe = new FakeTypeSafeClient(request => request.Questions.ContainsKey("attack")
            ? FakeTypeSafeClient.Response(("attack", FakeTypeSafeClient.Noul(0.05)))
            : FakeTypeSafeClient.Response(("leak", FakeTypeSafeClient.Noul(0.5))));
        var inner = new EchoChatClient("Here is the answer.");
        var client = new TypeSafeGuardrailChatClient(inner, typeSafe, new GuardrailOptions
        {
            InputQuestions = input,
            OutputQuestions = output,
            DecideInput = a => a.Get(attack).Probability > 0.5 ? GuardrailDecision.Block("attack") : GuardrailDecision.Allow,
            DecideOutput = a => a.Get(leak).Probability >= 0.35 ? GuardrailDecision.Review("possible leak") : GuardrailDecision.Allow,
        });

        var response = await client.GetResponseAsync(Conversation);

        await Assert.That(response.Text).IsEqualTo("Here is the answer.");
        await Assert.That(inner.Calls.Count).IsEqualTo(1);
        var outcomes = (IReadOnlyList<GuardrailOutcome>)response.AdditionalProperties![TypeSafeGuardrailChatClient.OutcomePropertyName]!;
        await Assert.That(outcomes.Count).IsEqualTo(2);
        await Assert.That(outcomes[1].Action).IsEqualTo(GuardrailAction.Review);
        await Assert.That(outcomes[1].Direction).IsEqualTo(GuardrailDirection.Output);
        await Assert.That(typeSafe.Requests[1].State.Node!["response"]!.GetValue<string>()).IsEqualTo("Here is the answer.");
    }

    [Test]
    public async Task Output_block_can_throw()
    {
        var output = new QuestionSet();
        var bad = output.Noul("bad?", id: "bad");
        var typeSafe = FakeTypeSafeClient.Answering(("bad", FakeTypeSafeClient.Noul(0.99)));
        var client = new TypeSafeGuardrailChatClient(new EchoChatClient(), typeSafe, new GuardrailOptions
        {
            OutputQuestions = output,
            Decide = a => a.Get(bad).Probability > 0.9 ? GuardrailDecision.Block("bad output") : GuardrailDecision.Allow,
            ThrowWhenBlocked = true,
        });

        var ex = await Assert.ThrowsAsync<TypeSafeGuardrailException>(() => client.GetResponseAsync(Conversation));

        await Assert.That(ex.Outcome.Direction).IsEqualTo(GuardrailDirection.Output);
        await Assert.That(ex.Message).Contains("bad output");
    }

    [Test]
    public async Task Streaming_blocks_on_input_and_buffers_output_only_when_enabled()
    {
        var input = new QuestionSet();
        var attack = input.Noul("attack?", id: "attack");
        var output = new QuestionSet();
        var bad = output.Noul("bad?", id: "bad");

        var typeSafe = new FakeTypeSafeClient(request => request.Questions.ContainsKey("attack")
            ? FakeTypeSafeClient.Response(("attack", FakeTypeSafeClient.Noul(0.0)))
            : FakeTypeSafeClient.Response(("bad", FakeTypeSafeClient.Noul(1.0))));
        var inner = new EchoChatClient("streamed words here");

        var unguarded = new TypeSafeGuardrailChatClient(inner, typeSafe, new GuardrailOptions
        {
            InputQuestions = input,
            OutputQuestions = output,
            DecideInput = _ => GuardrailDecision.Allow,
            DecideOutput = a => a.Get(bad).Probability > 0.5 ? GuardrailDecision.Block("bad") : GuardrailDecision.Allow,
        });
        var streamed = await unguarded.GetStreamingResponseAsync(Conversation).ToChatResponseAsync();
        await Assert.That(streamed.Text.Trim()).IsEqualTo("streamed words here");
        await Assert.That(typeSafe.Requests.Count).IsEqualTo(1);

        var guarded = new TypeSafeGuardrailChatClient(inner, typeSafe, new GuardrailOptions
        {
            InputQuestions = input,
            OutputQuestions = output,
            GuardStreamingOutput = true,
            DecideInput = _ => GuardrailDecision.Allow,
            DecideOutput = a => a.Get(bad).Probability > 0.5 ? GuardrailDecision.Block("bad") : GuardrailDecision.Allow,
            BlockedMessage = "Blocked.",
        });
        var blocked = await guarded.GetStreamingResponseAsync(Conversation).ToChatResponseAsync();
        await Assert.That(blocked.Text).IsEqualTo("Blocked.");
        await Assert.That(typeSafe.Requests.Count).IsEqualTo(3);

        var inputBlocked = new TypeSafeGuardrailChatClient(inner, new FakeTypeSafeClient(_ => FakeTypeSafeClient.Response(("attack", FakeTypeSafeClient.Noul(1.0)))), new GuardrailOptions
        {
            InputQuestions = input,
            Decide = a => a.Get(attack).Probability > 0.5 ? GuardrailDecision.Block("attack") : GuardrailDecision.Allow,
            BlockedMessage = "No.",
        });
        var before = inner.StreamingCalls;
        var refused = await inputBlocked.GetStreamingResponseAsync(Conversation).ToChatResponseAsync();
        await Assert.That(refused.Text).IsEqualTo("No.");
        await Assert.That(inner.StreamingCalls).IsEqualTo(before);
    }

    [Test]
    public async Task Threshold_policy_follows_the_cookbook_precedence()
    {
        var (set, jailbreak, harmful, severity) = Battery();
        var policy = GuardrailPolicies.Thresholds(
            new Dictionary<NoulHandle, GuardrailAction> { [jailbreak] = GuardrailAction.Block, [harmful] = GuardrailAction.Review },
            severity,
            actionThreshold: 0.7,
            reviewThreshold: 0.35,
            severityBlock: 2.0);

        await Assert.That(Decide(policy, set, 0.1, 0.1, 0.0).Action).IsEqualTo(GuardrailAction.Allow);
        await Assert.That(Decide(policy, set, 0.4, 0.1, 0.0).Action).IsEqualTo(GuardrailAction.Review);
        await Assert.That(Decide(policy, set, 0.8, 0.1, 0.0).Action).IsEqualTo(GuardrailAction.Block);
        await Assert.That(Decide(policy, set, 0.1, 0.9, 0.0).Action).IsEqualTo(GuardrailAction.Review);
        await Assert.That(Decide(policy, set, 0.1, 0.9, 2.5).Action).IsEqualTo(GuardrailAction.Block);
        await Assert.That(Decide(policy, set, 0.4, 0.4, 2.5).Reason).Contains("severe");
    }

    [Test]
    public async Task Options_require_questions_and_a_policy()
    {
        var inner = new EchoChatClient();
        var typeSafe = FakeTypeSafeClient.Answering();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await Task.Yield();
            _ = new TypeSafeGuardrailChatClient(inner, typeSafe, new GuardrailOptions());
        });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await Task.Yield();
            _ = new TypeSafeGuardrailChatClient(inner, typeSafe, new GuardrailOptions { InputQuestions = new QuestionSet() });
        });
    }

    [Test]
    public async Task Builder_extension_composes_the_pipeline()
    {
        var q = new QuestionSet();
        var flag = q.Noul("flag?", id: "flag");
        var typeSafe = FakeTypeSafeClient.Answering(("flag", FakeTypeSafeClient.Noul(0.2)));
        var inner = new EchoChatClient("from inner");

        var client = new ChatClientBuilder(inner)
            .UseTypeSafeGuardrail(typeSafe, o =>
            {
                o.InputQuestions = q;
                o.Decide = a => a.Get(flag).Probability > 0.5 ? GuardrailDecision.Block("flagged") : GuardrailDecision.Allow;
            })
            .Build();

        var response = await client.GetResponseAsync("hello");
        await Assert.That(response.Text).IsEqualTo("from inner");
    }

    private static GuardrailDecision Decide(Func<GuardrailAssessment, GuardrailDecision> policy, QuestionSet set, double jailbreak, double harmful, double severity)
    {
        var response = FakeTypeSafeClient.Response(
            ("jailbreak", FakeTypeSafeClient.Noul(jailbreak)),
            ("harmful", FakeTypeSafeClient.Noul(harmful)),
            ("severity", FakeTypeSafeClient.Score(severity, 0.9, 1, 0, 0, 0)));
        var typeSafe = new FakeTypeSafeClient(_ => response);
        var result = typeSafe.SystemOneAsync("x", set).GetAwaiter().GetResult();
        var assessment = new GuardrailAssessmentBuilder(result).Build();
        return policy(assessment);
    }

    // GuardrailAssessment has an internal constructor; reach it through the public chat client path instead.
    private sealed class GuardrailAssessmentBuilder(QuestionSetResult result)
    {
        public GuardrailAssessment Build()
        {
            GuardrailAssessment? captured = null;
            var options = new GuardrailOptions
            {
                InputQuestions = result.Questions,
                Decide = a =>
                {
                    captured = a;
                    return GuardrailDecision.Allow;
                },
            };
            var client = new TypeSafeGuardrailChatClient(new EchoChatClient(), new FakeTypeSafeClient(_ => result.Response), options);
            client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")]).GetAwaiter().GetResult();
            return captured!;
        }
    }
}
