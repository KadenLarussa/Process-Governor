namespace ProcessGovernor.Models;

public sealed class ProcessActionResult
{
    public bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;

    public Exception? Exception { get; init; }

    public static ProcessActionResult Success(string message) => new()
    {
        Succeeded = true,
        Message = message
    };

    public static ProcessActionResult Failure(string message, Exception? exception = null) => new()
    {
        Succeeded = false,
        Message = message,
        Exception = exception
    };
}
