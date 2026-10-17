using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Accounts.Contracts;

namespace FlashAI.Modules.Accounts.Application;

public sealed class PreviewAccountWorkspaceService : IAccountWorkspaceReader, IAccountCommandPort, IAccountCatalog
{
    private readonly List<AccountDetailVm> _accounts = [];

    public PreviewAccountWorkspaceService()
    {
        SeedPreviewData();
    }

    public Task<IReadOnlyList<AccountListItemVm>> GetAccountsAsync(
        string? search = null,
        string? filter = null,
        string? sort = null,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<AccountDetailVm> query = _accounts.Where(a => !a.IsArchived);

        // Search
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(a =>
                a.Email.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                a.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                a.Provider.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (a.Notes != null && a.Notes.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                a.RecordedPlan.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                a.Tags.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        // Filter
        if (!string.IsNullOrWhiteSpace(filter) && !filter.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            query = filter.ToLowerInvariant() switch
            {
                "connected" => query.Where(a => a.Status.Equals("Connected", StringComparison.OrdinalIgnoreCase)),
                "unverified" => query.Where(a => a.Status.Equals("Unverified", StringComparison.OrdinalIgnoreCase)),
                "attention" => query.Where(a => a.Status.Equals("ReauthNeeded", StringComparison.OrdinalIgnoreCase) || a.Status.Equals("ConnectionError", StringComparison.OrdinalIgnoreCase)),
                "favorite" => query.Where(a => a.IsFavorite),
                _ => query
            };
        }

        // Sort
        query = (sort?.ToLowerInvariant()) switch
        {
            "oldest" => query.OrderBy(a => a.TrackingDate),
            "name_asc" => query.OrderBy(a => a.DisplayName),
            "name_desc" => query.OrderByDescending(a => a.DisplayName),
            "plan" => query.OrderBy(a => a.RecordedPlan),
            _ => query.OrderByDescending(a => a.TrackingDate) // default: newest
        };

        var listItems = query.Select(a => new AccountListItemVm(
            a.AccountId,
            a.NumericId,
            a.Email,
            a.DisplayName,
            a.Provider,
            a.IsFavorite,
            a.Status,
            a.StatusLabel,
            a.RecordedPlan,
            a.TrackingDate,
            a.Tags,
            a.IsArchived
        )).ToList();

        return Task.FromResult<IReadOnlyList<AccountListItemVm>>(listItems);
    }

    public Task<AccountDetailVm?> GetAccountDetailAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var item = _accounts.FirstOrDefault(a => a.AccountId == accountId);
        return Task.FromResult(item);
    }

    public ValueTask<bool> ExistsAsync(Guid accountId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_accounts.Any(account => account.AccountId == accountId));

    public ValueTask<AccountSummary?> FindAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = _accounts.FirstOrDefault(item => item.AccountId == accountId);
        return ValueTask.FromResult(account is null
            ? null
            : new AccountSummary(account.AccountId, account.DisplayName, account.IsArchived));
    }

    public Task<OperationResultVm> CreateAccountAsync(AccountEditorVm model, CancellationToken cancellationToken = default)
    {
        if (_accounts.Any(a => a.Email.Equals(model.Email.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(OperationResultVm.Failure(
                "DUPLICATE_EMAIL",
                "Email này đã tồn tại trong danh sách tài khoản.",
                new Dictionary<string, string[]> { ["Email"] = ["Email đã tồn tại"] }));
        }

        var newId = Guid.NewGuid();
        var maxNumericId = _accounts.Count > 0 ? _accounts.Max(a => a.NumericId) + 1 : 1;
        var now = DateTimeOffset.Now;

        var statusLabel = model.Status switch
        {
            "Connected" => "Đã kết nối",
            "Unverified" => "Chưa xác minh",
            "ReauthNeeded" => "Cần xác thực lại",
            "ConnectionError" => "Lỗi kết nối",
            _ => "Ngoại tuyến"
        };

        var newAccount = new AccountDetailVm(
            newId,
            maxNumericId,
            model.Email.Trim(),
            model.DisplayName.Trim(),
            model.Notes?.Trim(),
            model.Provider,
            false,
            model.Status,
            statusLabel,
            model.RecordedPlan,
            now,
            "Người dùng",
            now,
            now,
            now,
            "Người dùng",
            model.Tags,
            false
        );

        _accounts.Insert(0, newAccount);
        return Task.FromResult(OperationResultVm.Success("Tạo tài khoản thành công trong bộ nhớ xem trước."));
    }

    public Task<OperationResultVm> UpdateAccountAsync(Guid accountId, AccountEditorVm model, CancellationToken cancellationToken = default)
    {
        var index = _accounts.FindIndex(a => a.AccountId == accountId);
        if (index == -1)
        {
            return Task.FromResult(OperationResultVm.Failure("NOT_FOUND", "Không tìm thấy tài khoản."));
        }

        var current = _accounts[index];
        var now = DateTimeOffset.Now;

        var statusLabel = model.Status switch
        {
            "Connected" => "Đã kết nối",
            "Unverified" => "Chưa xác minh",
            "ReauthNeeded" => "Cần xác thực lại",
            "ConnectionError" => "Lỗi kết nối",
            _ => "Ngoại tuyến"
        };

        _accounts[index] = current with
        {
            DisplayName = model.DisplayName.Trim(),
            Notes = model.Notes?.Trim(),
            Provider = model.Provider,
            Tags = model.Tags,
            RecordedPlan = model.RecordedPlan,
            Status = model.Status,
            StatusLabel = statusLabel,
            UpdatedAt = now
        };

        return Task.FromResult(OperationResultVm.Success("Đã lưu thay đổi vào bộ nhớ xem trước."));
    }

    public Task<OperationResultVm> ToggleFavoriteAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var index = _accounts.FindIndex(a => a.AccountId == accountId);
        if (index == -1)
        {
            return Task.FromResult(OperationResultVm.Failure("NOT_FOUND", "Không tìm thấy tài khoản."));
        }

        var current = _accounts[index];
        _accounts[index] = current with { IsFavorite = !current.IsFavorite };
        return Task.FromResult(OperationResultVm.Success());
    }

    public Task<OperationResultVm> ArchiveAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var index = _accounts.FindIndex(a => a.AccountId == accountId);
        if (index == -1)
        {
            return Task.FromResult(OperationResultVm.Failure("NOT_FOUND", "Không tìm thấy tài khoản."));
        }

        var current = _accounts[index];
        _accounts[index] = current with { IsArchived = true };
        return Task.FromResult(OperationResultVm.Success("Đã lưu trữ tài khoản vào bộ nhớ xem trước."));
    }

    public Task<OperationResultVm> RestoreAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var index = _accounts.FindIndex(a => a.AccountId == accountId);
        if (index == -1)
        {
            return Task.FromResult(OperationResultVm.Failure("NOT_FOUND", "Không tìm thấy tài khoản."));
        }

        var current = _accounts[index];
        _accounts[index] = current with { IsArchived = false };
        return Task.FromResult(OperationResultVm.Success("Đã khôi phục tài khoản."));
    }

    public Task<OperationResultVm> DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var index = _accounts.FindIndex(a => a.AccountId == accountId);
        if (index == -1)
        {
            return Task.FromResult(OperationResultVm.Failure("NOT_FOUND", "Không tìm thấy tài khoản."));
        }

        _accounts.RemoveAt(index);
        return Task.FromResult(OperationResultVm.Success("Đã xóa tài khoản khỏi bộ nhớ xem trước."));
    }

    private void SeedPreviewData()
    {
        var now = new DateTimeOffset(2026, 10, 15, 9, 12, 0, TimeSpan.FromHours(7));

        _accounts.AddRange([
            new AccountDetailVm(
                Guid.Parse("11111111-1111-1111-1111-111111111101"),
                1,
                "account01@example.com",
                "Tài khoản cá nhân",
                "Tài khoản sử dụng chính\nCó kết nối ổn định, dùng cho công việc.",
                "openai",
                true,
                "Connected",
                "Đã kết nối",
                "Plus",
                new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.FromHours(7)),
                "Đã ghi nhận",
                new DateTimeOffset(2026, 10, 1, 14, 30, 0, TimeSpan.FromHours(7)),
                now,
                now,
                "Người dùng",
                ["chatgpt", "chính"]
            ),
            new AccountDetailVm(
                Guid.Parse("11111111-1111-1111-1111-111111111102"),
                2,
                "account02@example.com",
                "Tài khoản phụ",
                "Dùng để tra cứu nhanh thông tin và so sánh kết quả.",
                "chatgpt",
                false,
                "Unverified",
                "Chưa xác minh",
                "Free",
                new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.FromHours(7)),
                "Người dùng",
                new DateTimeOffset(2026, 10, 2, 10, 15, 0, TimeSpan.FromHours(7)),
                now,
                now,
                "Người dùng",
                ["free", "nghiên cứu"]
            ),
            new AccountDetailVm(
                Guid.Parse("11111111-1111-1111-1111-111111111103"),
                3,
                "account03@example.com",
                "Tài khoản nghiên cứu",
                "Cần xác thực lại phiên làm việc sau đợt cập nhật bảo mật.",
                "chatgpt",
                false,
                "ReauthNeeded",
                "Cần xác thực lại",
                "Chưa rõ",
                new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.FromHours(7)),
                "Người dùng",
                new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.FromHours(7)),
                now,
                now,
                "Người dùng",
                ["test", "dự phòng"]
            ),
            new AccountDetailVm(
                Guid.Parse("11111111-1111-1111-1111-111111111104"),
                4,
                "account04@example.com",
                "Tài khoản dự phòng",
                "Tài khoản liên kết SSO Microsoft cho dự án văn phòng.",
                "microsoft",
                false,
                "Connected",
                "Đã kết nối",
                "Plus",
                new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.FromHours(7)),
                "Đã ghi nhận",
                new DateTimeOffset(2026, 10, 4, 13, 20, 0, TimeSpan.FromHours(7)),
                now,
                now,
                "Người dùng",
                ["ms-login"]
            ),
            new AccountDetailVm(
                Guid.Parse("11111111-1111-1111-1111-111111111105"),
                5,
                "account05@example.com",
                "Tài khoản test",
                "Đăng ký bằng Google để thử nghiệm các tính năng beta.",
                "google",
                false,
                "Unverified",
                "Chưa xác minh",
                "Free",
                new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.FromHours(7)),
                "Người dùng",
                new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.FromHours(7)),
                now,
                now,
                "Người dùng",
                ["google"]
            ),
            new AccountDetailVm(
                Guid.Parse("11111111-1111-1111-1111-111111111106"),
                6,
                "account06@example.com",
                "Tài khoản dài hạn",
                "Tài khoản gói năm, dự trữ quota cho các tháng cao điểm.",
                "chatgpt",
                false,
                "Connected",
                "Đã kết nối",
                "Plus",
                new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.FromHours(7)),
                "Đã ghi nhận",
                new DateTimeOffset(2026, 10, 6, 11, 10, 0, TimeSpan.FromHours(7)),
                now,
                now,
                "Người dùng",
                ["plus", "quan trọng"]
            ),
            new AccountDetailVm(
                Guid.Parse("11111111-1111-1111-1111-111111111107"),
                7,
                "account07@example.com",
                "Tài khoản tạm",
                "Gặp sự cố kết nối mạng trong lần kiểm tra phiên gần nhất.",
                "chatgpt",
                false,
                "ConnectionError",
                "Lỗi kết nối",
                "Chưa rõ",
                new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(7)),
                "Người dùng",
                new DateTimeOffset(2026, 10, 7, 14, 0, 0, TimeSpan.FromHours(7)),
                now,
                now,
                "Người dùng",
                ["khác"]
            ),
            new AccountDetailVm(
                Guid.Parse("11111111-1111-1111-1111-111111111108"),
                8,
                "account08@example.com",
                "Tài khoản dịch thuật",
                "Chuyên dùng cho xử lý tài liệu đa ngữ.",
                "openai",
                false,
                "Connected",
                "Đã kết nối",
                "Plus",
                new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.FromHours(7)),
                "Đã ghi nhận",
                new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.FromHours(7)),
                now,
                now,
                "Người dùng",
                ["translation"]
            ),
            new AccountDetailVm(
                Guid.Parse("11111111-1111-1111-1111-111111111109"),
                9,
                "account09@example.com",
                "Tài khoản viết content",
                "Hỗ trợ soạn thảo nội dung báo chí và bài viết blog.",
                "openai",
                false,
                "Connected",
                "Đã kết nối",
                "Plus",
                new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.FromHours(7)),
                "Đã ghi nhận",
                new DateTimeOffset(2026, 9, 12, 9, 30, 0, TimeSpan.FromHours(7)),
                now,
                now,
                "Người dùng",
                ["content"]
            ),
            new AccountDetailVm(
                Guid.Parse("11111111-1111-1111-1111-111111111110"),
                10,
                "account10@example.com",
                "Tài khoản lập trình",
                "Tài khoản phục vụ coding và hỗ trợ giải thuật.",
                "openai",
                false,
                "Connected",
                "Đã kết nối",
                "Plus",
                new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.FromHours(7)),
                "Đã ghi nhận",
                new DateTimeOffset(2026, 9, 15, 15, 40, 0, TimeSpan.FromHours(7)),
                now,
                now,
                "Người dùng",
                ["dev"]
            ),
            new AccountDetailVm(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                11,
                "account11@example.com",
                "Tài khoản thử nghiệm mới",
                "Đang chờ kiểm tra đăng nhập phiên đầu tiên.",
                "google",
                false,
                "Unverified",
                "Chưa xác minh",
                "Free",
                new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.FromHours(7)),
                "Người dùng",
                new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.FromHours(7)),
                now,
                now,
                "Người dùng",
                ["sandbox"]
            ),
            new AccountDetailVm(
                Guid.Parse("11111111-1111-1111-1111-111111111112"),
                12,
                "account12@example.com",
                "Tài khoản lưu trữ",
                "Tài khoản đã hoàn thành giai đoạn thử nghiệm.",
                "chatgpt",
                false,
                "Offline",
                "Ngoại tuyến",
                "Chưa rõ",
                new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.FromHours(7)),
                "Người dùng",
                new DateTimeOffset(2026, 9, 17, 8, 30, 0, TimeSpan.FromHours(7)),
                now,
                now,
                "Người dùng",
                ["archive"]
            )
        ]);
    }
}
