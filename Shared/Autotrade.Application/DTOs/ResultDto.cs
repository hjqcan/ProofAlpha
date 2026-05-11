namespace Autotrade.Application.DTOs;

/// <summary>
/// Shared operation result DTO.
/// </summary>
public class ResultDto
{
    public bool Success { get; set; }

    public string? Message { get; set; }

    public string? ErrorCode { get; set; }

    public static ResultDto SuccessResult(string? message = null)
    {
        return new ResultDto
        {
            Success = true,
            Message = message ?? "Operation succeeded"
        };
    }

    public static ResultDto FailureResult(string message, string? errorCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new ResultDto
        {
            Success = false,
            Message = message,
            ErrorCode = errorCode
        };
    }
}

/// <summary>
/// Shared operation result DTO carrying data.
/// </summary>
/// <typeparam name="T">The data type.</typeparam>
public class ResultDto<T> : ResultDto
{
    public T? Data { get; set; }

    public static ResultDto<T> SuccessResult(T data, string? message = null)
    {
        return new ResultDto<T>
        {
            Success = true,
            Message = message ?? "Operation succeeded",
            Data = data
        };
    }

    public new static ResultDto<T> FailureResult(string message, string? errorCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new ResultDto<T>
        {
            Success = false,
            Message = message,
            ErrorCode = errorCode
        };
    }
}
