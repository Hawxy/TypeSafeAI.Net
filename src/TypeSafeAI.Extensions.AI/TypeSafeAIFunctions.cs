using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace TypeSafeAI.Extensions.AI;

/// <summary>Exposes TypeSafe judgments as tools an LLM agent can call.</summary>
public static class TypeSafeAIFunctions
{
    /// <summary>
    /// Creates a tool with a single <c>state</c> parameter that runs the given questions and returns the answers as JSON.
    /// Register it in <see cref="ChatOptions.Tools"/> so a model can ask TypeSafe for a calibrated judgment mid-conversation.
    /// </summary>
    /// <param name="client">The TypeSafe client.</param>
    /// <param name="questions">The questions asked of the state the model supplies.</param>
    /// <param name="name">The tool name shown to the model.</param>
    /// <param name="description">What the tool judges; write it for the model.</param>
    /// <param name="stateDescription">Describes what the model should pass as <c>state</c>.</param>
    /// <param name="requestOptions">Per-request overrides for the TypeSafe call.</param>
    public static AIFunction Create(
        ITypeSafeClient client,
        IReadOnlyDictionary<string, Question> questions,
        string name,
        string description,
        string stateDescription = "The text to judge.",
        RequestOptions? requestOptions = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (questions.Count == 0)
        {
            throw new ArgumentException("At least one question is required.", nameof(questions));
        }

        return new JudgeFunction(client, questions, name, description, stateDescription, requestOptions);
    }

    private sealed class JudgeFunction : AIFunction
    {
        private const string StateParameter = "state";

        private readonly ITypeSafeClient _client;
        private readonly IReadOnlyDictionary<string, Question> _questions;
        private readonly RequestOptions? _requestOptions;

        public JudgeFunction(
            ITypeSafeClient client,
            IReadOnlyDictionary<string, Question> questions,
            string name,
            string description,
            string stateDescription,
            RequestOptions? requestOptions)
        {
            _client = client;
            _questions = questions;
            _requestOptions = requestOptions;
            Name = name;
            Description = description;
            JsonSchema = BuildSchema(stateDescription);
        }

        public override string Name { get; }

        public override string Description { get; }

        public override JsonElement JsonSchema { get; }

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            if (!arguments.TryGetValue(StateParameter, out var raw) || raw is null)
            {
                throw new ArgumentException($"The '{StateParameter}' argument is required.", nameof(arguments));
            }

            var state = raw switch
            {
                string text => TypeSafeContent.FromText(text),
                JsonElement element => TypeSafeContent.FromElement(element),
                JsonNode node => TypeSafeContent.FromNode(node),
                TypeSafeContent content => content,
                _ => TypeSafeContent.FromText(raw.ToString() ?? string.Empty),
            };

            var response = await _client.SystemOneAsync(state, _questions, _requestOptions, cancellationToken).ConfigureAwait(false);
            return ToJson(response);
        }

        private static JsonElement BuildSchema(string stateDescription)
        {
            var schema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    [StateParameter] = new JsonObject { ["type"] = "string", ["description"] = stateDescription },
                },
                ["required"] = new JsonArray(StateParameter),
            };
            return JsonSerializer.SerializeToElement(schema, FunctionJsonContext.Default.JsonObject);
        }

        // A compact, model-friendly rendering of the answers.
        private static JsonElement ToJson(SystemOneResponse response)
        {
            var answers = new JsonObject();
            foreach (var pair in response.Answers)
            {
                answers[pair.Key] = pair.Value switch
                {
                    NoulAnswer noul => new JsonObject { ["type"] = "noul", ["probability"] = noul.Probability },
                    ChoiceAnswer choice => new JsonObject
                    {
                        ["type"] = "choice",
                        ["choice"] = choice.Choice,
                        ["confidence"] = choice.Confidence,
                        ["probabilities"] = ToObject(choice.Probabilities),
                    },
                    ScoreAnswer score => new JsonObject
                    {
                        ["type"] = "score",
                        ["score"] = score.Score,
                        ["confidence"] = score.Confidence,
                        ["probabilities"] = ToObject(score.Probabilities.ToDictionary(p => p.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), p => p.Value)),
                    },
                    _ => new JsonObject { ["type"] = pair.Value.Type },
                };
            }

            var result = new JsonObject { ["model"] = response.Model, ["answers"] = answers };
            return JsonSerializer.SerializeToElement(result, FunctionJsonContext.Default.JsonObject);
        }

        private static JsonObject ToObject(IReadOnlyDictionary<string, double> values)
        {
            var obj = new JsonObject();
            foreach (var pair in values)
            {
                obj[pair.Key] = pair.Value;
            }

            return obj;
        }
    }
}
