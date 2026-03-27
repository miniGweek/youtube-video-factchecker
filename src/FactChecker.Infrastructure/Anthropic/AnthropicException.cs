using System.Net;

namespace FactChecker.Infrastructure.Anthropic;

public sealed class AnthropicException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? ErrorType { get; }

    public AnthropicException()
        : this(HttpStatusCode.InternalServerError, "An Anthropic API error occurred.") { }

    public AnthropicException(string message)
        : this(HttpStatusCode.InternalServerError, message) { }

    public AnthropicException(string message, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = HttpStatusCode.InternalServerError;
    }

    public AnthropicException(HttpStatusCode statusCode, string message, string? errorType = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorType = errorType;
    }
}
