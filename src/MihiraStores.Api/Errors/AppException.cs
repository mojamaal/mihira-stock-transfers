namespace MihiraStores.Api.Errors;

/// <summary>
/// An expected business failure (bad input, missing resource, rule violation).
/// Converted into a ProblemDetails response by <see cref="ProblemDetailsExceptionHandler"/>, never a 500.
/// </summary>
public sealed class AppException : Exception
{
    private AppException(int statusCode, string title, string detail, IDictionary<string, string[]>? errors = null)
        : base(detail)
    {
        StatusCode = statusCode;
        Title = title;
        Errors = errors;
    }

    public int StatusCode { get; }
    public string Title { get; }
    public IDictionary<string, string[]>? Errors { get; }

    public static AppException NotFound(string detail) =>
        new(StatusCodes.Status404NotFound, "Not found", detail);

    public static AppException Conflict(string detail) =>
        new(StatusCodes.Status409Conflict, "Not allowed", detail);

    public static AppException Validation(IDictionary<string, string[]> errors) =>
        new(StatusCodes.Status400BadRequest, "The request is not valid",
            string.Join(" ", errors.Values.SelectMany(v => v)), errors);
}
