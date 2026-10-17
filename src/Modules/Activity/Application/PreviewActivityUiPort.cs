using FlashAI.Modules.Activity.Contracts;

namespace FlashAI.Modules.Activity.Application;

public sealed class PreviewActivityUiPort : IActivityUiPort
{
    private readonly List<ActivityEventVm> _events = [];

    public PreviewActivityUiPort()
    {
        var now = DateTimeOffset.Now;
        _events.AddRange([
            new ActivityEventVm(
                Guid.NewGuid(),
                Guid.Parse("11111111-1111-1111-1111-111111111101"),
                now.AddMinutes(-18),
                "Kiểm tra kết nối",
                "Success",
                "Xác minh trạng thái kết nối tài khoản thành công",
                "Đã đối soát phiên làm việc với cổng an toàn. Thời gian phản hồi 210ms."
            ),
            new ActivityEventVm(
                Guid.NewGuid(),
                Guid.Parse("11111111-1111-1111-1111-111111111101"),
                now.AddHours(-3),
                "Cập nhật gói",
                "Info",
                "Ghi nhận thông tin gói ChatGPT Plus",
                "Nguồn dữ liệu: Người dùng xác nhận chu kỳ gia hạn tiếp theo."
            ),
            new ActivityEventVm(
                Guid.NewGuid(),
                Guid.Parse("11111111-1111-1111-1111-111111111103"),
                now.AddDays(-1),
                "Cảnh báo phiên",
                "Warning",
                "Yêu cầu xác thực lại phiên làm việc",
                "Phát hiện phiên làm việc hết hạn sau 14 ngày. Cần mở trình duyệt để xác thực."
            ),
            new ActivityEventVm(
                Guid.NewGuid(),
                Guid.Parse("11111111-1111-1111-1111-111111111107"),
                now.AddDays(-2),
                "Lỗi kết nối",
                "Error",
                "Không thể kết nối đến máy chủ xác minh",
                "Lỗi kết nối mạng cục bộ. Thao tác an toàn không ảnh hưởng dữ liệu."
            )
        ]);
    }

    public Task<IReadOnlyList<ActivityEventVm>> GetEventsAsync(Guid? accountId = null, string? severity = null, CancellationToken cancellationToken = default)
    {
        IEnumerable<ActivityEventVm> query = _events;

        if (accountId.HasValue)
        {
            query = query.Where(e => e.AccountId == accountId.Value || e.AccountId == null);
        }

        if (!string.IsNullOrWhiteSpace(severity) && !severity.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(e => e.Severity.Equals(severity, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult<IReadOnlyList<ActivityEventVm>>(query.OrderByDescending(e => e.Timestamp).ToList());
    }
}
