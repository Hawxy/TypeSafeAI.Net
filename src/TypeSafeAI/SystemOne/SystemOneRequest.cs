namespace TypeSafeAI;

/// <summary>A System One request: one state evaluated against named questions.</summary>
public sealed class SystemOneRequest
{
    /// <summary>The content to judge: text, a JSON object, or a JSON array.</summary>
    public required TypeSafeContent State { get; init; }

    /// <summary>The questions keyed by an id of your choosing. Ids are for your code and are not shown to the model.</summary>
    public required IReadOnlyDictionary<string, Question> Questions { get; init; }

    /// <summary>The model to use. Defaults to the client's default model.</summary>
    public string? Model { get; init; }
}
