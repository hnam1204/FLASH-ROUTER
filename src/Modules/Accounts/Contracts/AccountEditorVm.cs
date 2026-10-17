using System.ComponentModel.DataAnnotations;

namespace FlashAI.Modules.Accounts.Contracts;

public sealed class AccountEditorVm
{
    public Guid? AccountId { get; set; }

    [Required(ErrorMessage = "Email không được để trống")]
    [EmailAddress(ErrorMessage = "Định dạng email không hợp lệ")]
    [StringLength(254, ErrorMessage = "Email tối đa 254 ký tự")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên hiển thị không được để trống")]
    [StringLength(100, ErrorMessage = "Tên hiển thị tối đa 100 ký tự")]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Ghi chú tối đa 1000 ký tự")]
    public string? Notes { get; set; }

    public string Provider { get; set; } = "openai";

    public List<string> Tags { get; set; } = [];

    public string RecordedPlan { get; set; } = "Chưa rõ";

    public string Status { get; set; } = "Unverified";

    public long? Version { get; set; }

    public AccountEditorVm Clone() => new()
    {
        AccountId = AccountId,
        Email = Email,
        DisplayName = DisplayName,
        Notes = Notes,
        Provider = Provider,
        Tags = [.. Tags],
        RecordedPlan = RecordedPlan,
        Status = Status,
        Version = Version
    };
}

public enum AccountConnectionMethod
{
    OfficialOAuth,
    Credentials
}

public sealed class CreateAccountRequest
{
    public AccountConnectionMethod Method { get; set; } = AccountConnectionMethod.Credentials;
    public AccountEditorVm Profile { get; set; } = new()
    {
        Provider = "openai",
        RecordedPlan = "Chưa rõ",
        Status = "Unverified"
    };
    public string Password { get; set; } = string.Empty;
    public string TotpSeed { get; set; } = string.Empty;
    public string CredentialLine { get; set; } = string.Empty;
    public bool OpenBrowserAfterSave { get; set; }

    public void ClearSensitive()
    {
        Password = string.Empty;
        TotpSeed = string.Empty;
        CredentialLine = string.Empty;
    }
}
