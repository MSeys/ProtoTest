namespace ProtoTest.SampleApp.Domain;

using System.Net;
using ProtoTest.SampleApp.Contracts;

/// <summary>A business-rule failure that is rendered as <c>application/problem+json</c>.</summary>
internal sealed class NorthstarException(string code, string message, HttpStatusCode statusCode)
    : Exception(message)
{
    public string Code { get; } = code;

    public HttpStatusCode StatusCode { get; } = statusCode;

    public IReadOnlyDictionary<string, string>? Details { get; init; }

    public static NorthstarException Unauthorized(string message = "A valid API token is required.")
        => new(ProblemCodes.Unauthorized, message, HttpStatusCode.Unauthorized);

    public static NorthstarException Forbidden(string message = "This token is not permitted to perform the action.")
        => new(ProblemCodes.Forbidden, message, HttpStatusCode.Forbidden);

    public static NorthstarException NotFound(string resource)
        => new(ProblemCodes.NotFound, $"The requested {resource} does not exist.", HttpStatusCode.NotFound);

    public static NorthstarException Validation(string message, IReadOnlyDictionary<string, string>? details = null)
        => new(ProblemCodes.ValidationFailed, message, HttpStatusCode.BadRequest) { Details = details };

    public static NorthstarException Conflict(string message)
        => new(ProblemCodes.Conflict, message, HttpStatusCode.Conflict);

    public static NorthstarException PlanLimit(string message, IReadOnlyDictionary<string, string>? details = null)
        => new(ProblemCodes.PlanLimitExceeded, message, HttpStatusCode.PaymentRequired) { Details = details };

    public static NorthstarException FeatureUnavailable(string feature)
        => new(
            ProblemCodes.PlanFeatureUnavailable,
            $"The current plan does not include '{feature}'.",
            HttpStatusCode.PaymentRequired);

    public static NorthstarException PaymentRequired(string message)
        => new(ProblemCodes.PaymentRequired, message, HttpStatusCode.PaymentRequired);

    public static NorthstarException InvoiceNotPayable(string message)
        => new(ProblemCodes.InvoiceNotPayable, message, HttpStatusCode.Conflict);

    public static NorthstarException RateLimited(TimeSpan retryAfter)
        => new(ProblemCodes.RateLimited, "The rate limit for this token was exceeded.", HttpStatusCode.TooManyRequests)
        {
            Details = new Dictionary<string, string> { ["retryAfterSeconds"] = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString() }
        };
}
