namespace SchoolErp.Domain.Common;

public class DomainException : Exception
{
    public string Code { get; }
    public int StatusCode { get; }

    public DomainException(string code, string message, int statusCode = 400) : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public static DomainException NotFound(string what) => new("not_found", $"{what} was not found.", 404);
    public static DomainException Forbidden(string message = "You do not have access to this resource.") => new("forbidden", message, 403);
    public static DomainException Conflict(string message) => new("conflict", message, 409);
    public static DomainException Validation(string message) => new("validation_error", message, 400);
    public static DomainException Unauthorized(string message = "Invalid credentials.") => new("unauthorized", message, 401);
}
