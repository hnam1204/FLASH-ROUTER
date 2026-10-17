namespace FlashAI.Foundation.Contracts;

public readonly record struct Result(bool IsSuccess, string? ErrorCode = null, string? ErrorMessage = null)
{
    public static Result Success() => new(true);

    public static Result Failure(string code, string message) => new(false, code, message);
}

public readonly record struct Result<T>(bool IsSuccess, T? Value, string? ErrorCode = null, string? ErrorMessage = null)
{
    public static Result<T> Success(T value) => new(true, value);

    public static Result<T> Failure(string code, string message) => new(false, default, code, message);
}
