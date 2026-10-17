namespace FlashAI.Foundation.Security;

public sealed class SecretVaultException : Exception
{
    public SecretVaultException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
