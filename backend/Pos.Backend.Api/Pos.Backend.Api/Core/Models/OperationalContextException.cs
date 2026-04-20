namespace Pos.Backend.Api.Core.Models;

public class OperationalContextException : Exception
{
    public OperationalContextException(string errorCode, int statusCode, string? details = null)
        : base(details)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
        Details = details;
    }

    public string ErrorCode { get; }
    public int StatusCode { get; }
    public string? Details { get; }
}
