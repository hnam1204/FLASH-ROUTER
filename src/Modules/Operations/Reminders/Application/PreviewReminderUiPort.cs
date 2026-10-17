using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Reminders.Contracts;

namespace FlashAI.Modules.Reminders.Application;

public sealed class PreviewReminderUiPort : IReminderUiPort
{
    private readonly List<ReminderVm> _reminders = [];

    public PreviewReminderUiPort()
    {
        _reminders.AddRange([
            new ReminderVm(
                Guid.NewGuid(),
                Guid.Parse("11111111-1111-1111-1111-111111111101"),
                "Kiểm tra chu kỳ gia hạn gói ChatGPT Plus",
                new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.FromHours(7)),
                "Gia hạn",
                false,
                "Đến hạn"
            ),
            new ReminderVm(
                Guid.NewGuid(),
                Guid.Parse("11111111-1111-1111-1111-111111111101"),
                "Đổi khóa dự phòng 2FA theo định kỳ",
                new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(7)),
                "Bảo mật",
                false,
                "Sắp tới"
            )
        ]);
    }

    public Task<IReadOnlyList<ReminderVm>> GetRemindersAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var items = _reminders.Where(r => r.AccountId == accountId).ToList();
        return Task.FromResult<IReadOnlyList<ReminderVm>>(items);
    }

    public Task<OperationResultVm> CreateReminderAsync(Guid accountId, string title, DateTimeOffset dueDate, string type, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Task.FromResult(OperationResultVm.Failure("REQUIRED", "Tiêu đề nhắc nhở không được để trống."));
        }

        _reminders.Add(new ReminderVm(
            Guid.NewGuid(),
            accountId,
            title.Trim(),
            dueDate,
            type,
            false,
            "Sắp tới"
        ));

        return Task.FromResult(OperationResultVm.Success("Đã thêm nhắc nhở vào bộ nhớ xem trước."));
    }

    public Task<OperationResultVm> ToggleCompleteAsync(Guid reminderId, CancellationToken cancellationToken = default)
    {
        var index = _reminders.FindIndex(r => r.ReminderId == reminderId);
        if (index != -1)
        {
            var cur = _reminders[index];
            _reminders[index] = cur with
            {
                IsCompleted = !cur.IsCompleted,
                Status = !cur.IsCompleted ? "Hoàn thành" : "Sắp tới"
            };
        }

        return Task.FromResult(OperationResultVm.Success());
    }

    public Task<OperationResultVm> DeleteReminderAsync(Guid reminderId, CancellationToken cancellationToken = default)
    {
        _reminders.RemoveAll(r => r.ReminderId == reminderId);
        return Task.FromResult(OperationResultVm.Success("Đã xóa nhắc nhở."));
    }
}
