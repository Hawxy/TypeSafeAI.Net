namespace TypeSafeAI;

/// <summary>Base class for every failure raised by the SDK.</summary>
public class TypeSafeException : Exception
{
    /// <summary>Creates the exception.</summary>
    public TypeSafeException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with an inner cause.</summary>
    public TypeSafeException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The <c>x-typesafe-request-id</c> of the failed request, when one was received.</summary>
    public string? RequestId { get; init; }
}

/// <summary>A successful HTTP response whose body could not be understood, or an answer that does not match its question.</summary>
public sealed class TypeSafeResponseValidationException : TypeSafeException
{
    /// <summary>Creates the exception.</summary>
    public TypeSafeResponseValidationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with an inner cause.</summary>
    public TypeSafeResponseValidationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The raw response body, when available.</summary>
    public string? Body { get; init; }
}

/// <summary>The request could not reach the API or the connection failed mid-response.</summary>
public class TypeSafeConnectionException : TypeSafeException
{
    /// <summary>Creates the exception.</summary>
    public TypeSafeConnectionException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>How many attempts were made before giving up.</summary>
    public int Attempts { get; init; }
}

/// <summary>Every attempt exceeded the per-attempt timeout.</summary>
public sealed class TypeSafeTimeoutException : TypeSafeConnectionException
{
    /// <summary>Creates the exception.</summary>
    public TypeSafeTimeoutException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The per-attempt timeout that was exceeded.</summary>
    public TimeSpan Timeout { get; init; }
}
