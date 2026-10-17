namespace FlashAI.Foundation.Contracts;

public sealed record OperationResultVm(
    bool IsSuccess,
    string? Message = null,
    string? ErrorCode = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null)
{
    public static OperationResultVm Success(string? message = null) =>
        new(true, message);

    public static OperationResultVm Failure(string errorCode, string message, IReadOnlyDictionary<string, string[]>? fieldErrors = null) =>
        new(false, message, errorCode, fieldErrors);
}
