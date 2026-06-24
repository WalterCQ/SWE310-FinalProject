namespace TaskFlow.Api.Helpers;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public IReadOnlyCollection<string> Errors { get; set; } = [];
    public int StatusCode { get; set; } = StatusCodes.Status200OK;
}

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string message = "Success")
    {
        return new ApiResponse<T>
        {
            Success = true,
            Message = message,
            Data = data,
            StatusCode = StatusCodes.Status200OK
        };
    }

    public static ApiResponse<T> Created<T>(T data, string message = "Created")
    {
        return new ApiResponse<T>
        {
            Success = true,
            Message = message,
            Data = data,
            StatusCode = StatusCodes.Status201Created
        };
    }

    public static ApiResponse<bool> NoData(string message = "Success")
    {
        return Ok(true, message);
    }

    public static ApiResponse<T> Fail<T>(
        string message,
        int statusCode = StatusCodes.Status400BadRequest,
        IReadOnlyCollection<string>? errors = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            Errors = errors ?? [message],
            StatusCode = statusCode
        };
    }
}
