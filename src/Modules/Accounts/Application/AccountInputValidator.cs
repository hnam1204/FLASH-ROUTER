using System.ComponentModel.DataAnnotations;
using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Accounts.Contracts;

namespace FlashAI.Modules.Accounts.Application;

public sealed record ValidatedAccountInput(
    string Email,
    string NormalizedEmail,
    string DisplayName,
    string NormalizedDisplayName,
    string? Notes,
    string? NormalizedNotes,
    string Provider,
    string NormalizedProvider,
    string RecordedPlan,
    string NormalizedRecordedPlan,
    string Status,
    IReadOnlyList<string> Tags);

public static class AccountInputValidator
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connected",
        "Unverified",
        "ReauthNeeded",
        "ConnectionError",
        "Offline"
    };

    public static OperationResultVm? Validate(
        AccountEditorVm? model,
        Guid? expectedAccountId,
        out ValidatedAccountInput? input)
    {
        input = null;

        if (model is null)
        {
            return OperationResultVm.Failure("VALIDATION_FAILED", "Dữ liệu tài khoản không hợp lệ.");
        }

        if (model.AccountId.HasValue && expectedAccountId.HasValue && model.AccountId != expectedAccountId)
        {
            return OperationResultVm.Failure("VALIDATION_FAILED", "ID tài khoản không khớp.");
        }

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var email = model.Email.Trim();
        var displayName = model.DisplayName.Trim();
        var provider = model.Provider.Trim().ToLowerInvariant();
        var plan = model.RecordedPlan.Trim();
        var status = model.Status.Trim();
        var notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim();

        if (email.Length == 0)
        {
            errors["Email"] = ["Email không được để trống."];
        }
        else if (email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
        {
            errors["Email"] = ["Định dạng email không hợp lệ."];
        }

        if (displayName.Length == 0)
        {
            errors["DisplayName"] = ["Tên hiển thị không được để trống."];
        }
        else if (displayName.Length > 100)
        {
            errors["DisplayName"] = ["Tên hiển thị tối đa 100 ký tự."];
        }

        if (notes is not null && notes.Length > 1000)
        {
            errors["Notes"] = ["Ghi chú tối đa 1000 ký tự."];
        }

        if (provider.Length == 0 || provider.Length > 100 || provider.Any(char.IsControl))
        {
            errors["Provider"] = ["Nhà cung cấp không hợp lệ."];
        }

        if (plan.Length == 0 || plan.Length > 40 || plan.Any(char.IsControl))
        {
            errors["RecordedPlan"] = ["Gói ghi nhận không hợp lệ."];
        }

        if (!AllowedStatuses.Contains(status))
        {
            errors["Status"] = ["Trạng thái tài khoản không được hỗ trợ."];
        }

        var tags = new List<string>();
        foreach (var rawTag in model.Tags ?? [])
        {
            var tag = rawTag.Trim();
            if (tag.Length == 0)
            {
                continue;
            }

            if (tag.Length > 50 || tag.Any(char.IsControl))
            {
                errors["Tags"] = ["Mỗi thẻ tối đa 50 ký tự và không được chứa ký tự điều khiển."];
                break;
            }

            if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(tag);
            }
        }

        if (tags.Count > 50)
        {
            errors["Tags"] = ["Tối đa 50 thẻ cho một tài khoản."];
        }

        if (errors.Count > 0)
        {
            return OperationResultVm.Failure("VALIDATION_FAILED", "Dữ liệu tài khoản chưa hợp lệ.", errors);
        }

        input = new ValidatedAccountInput(
            email,
            NormalizeForSearch(email),
            displayName,
            NormalizeForSearch(displayName),
            notes,
            notes is null ? null : NormalizeForSearch(notes),
            provider,
            NormalizeForSearch(provider),
            plan,
            NormalizeForSearch(plan),
            status,
            tags);

        return null;
    }

    public static string NormalizeForSearch(string value) => value.Trim().ToUpperInvariant();

    public static string StatusLabel(string status) => status switch
    {
        "Connected" => "Đã kết nối",
        "Unverified" => "Chưa xác minh",
        "ReauthNeeded" => "Cần xác thực lại",
        "ConnectionError" => "Lỗi kết nối",
        _ => "Ngoại tuyến"
    };
}
