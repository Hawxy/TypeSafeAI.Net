namespace TypeSafeAI.Extensions.AI;

internal static class Internal
{
    public static IReadOnlyList<T> AsReadOnlyList<T>(this IEnumerable<T> source) =>
        source as IReadOnlyList<T> ?? source.ToList();

    public static void RequireQuestions(IReadOnlyDictionary<string, Question> questions, string paramName)
    {
        ArgumentNullException.ThrowIfNull(questions, paramName);
        if (questions.Count == 0)
        {
            throw new ArgumentException("At least one question is required.", paramName);
        }
    }
}
