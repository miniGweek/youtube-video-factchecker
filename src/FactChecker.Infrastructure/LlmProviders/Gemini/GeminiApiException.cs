using System.Net;

namespace FactChecker.Infrastructure.LlmProviders.Gemini;

/// <summary>
/// Thrown for non-transient Gemini API errors (4xx other than 429).
/// </summary>
public sealed class GeminiApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? ErrorStatus { get; }

    public GeminiApiException()
        : this(HttpStatusCode.InternalServerError, "A Gemini API error occurred.") { }

    public GeminiApiException(string message)
        : this(HttpStatusCode.InternalServerError, message) { }

    public GeminiApiException(string message, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = HttpStatusCode.InternalServerError;
    }

    public GeminiApiException(HttpStatusCode statusCode, string message, string? errorStatus = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorStatus = errorStatus;
    }
}
