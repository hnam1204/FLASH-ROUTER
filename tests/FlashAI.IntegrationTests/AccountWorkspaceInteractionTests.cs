using FlashAI.Modules.Accounts.Application;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Authenticator.Application;
using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.ImportExport.Application;
using FlashAI.Modules.Reminders.Application;
using FlashAI.Modules.Settings.Application;
using FlashAI.Modules.Settings.Contracts;
using FlashAI.Modules.Subscriptions.Application;

namespace FlashAI.IntegrationTests;

public sealed class AccountWorkspaceInteractionTests
{
    [Fact]
    public async Task InitialAccountsListMatchesApprovedDesign()
    {
        var service = new PreviewAccountWorkspaceService();
        var all = await service.GetAccountsAsync();

        Assert.Equal(12, all.Count);
        Assert.Equal(6, all.Count(a => a.Status.Equals("Connected", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(3, all.Count(a => a.Status.Equals("Unverified", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2, all.Count(a => a.Status.Equals("ReauthNeeded", StringComparison.OrdinalIgnoreCase) || a.Status.Equals("ConnectionError", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(1, all.Count(a => a.IsFavorite));

        var first = all.First(a => a.NumericId == 1);
        Assert.Equal("account01@example.com", first.Email);
        Assert.Equal("Tài khoản cá nhân", first.DisplayName);
        Assert.Equal("Plus", first.RecordedPlan);
        Assert.True(first.IsFavorite);
    }

    [Theory]
    [InlineData("account01", 1)]
    [InlineData("cá nhân", 1)]
    [InlineData("Plus", 6)]
    [InlineData("chatgpt", 6)]
    [InlineData("khong-ton-tai-12345", 0)]
    public async Task SearchReturnsMatchingAccounts(string query, int expectedCount)
    {
        var service = new PreviewAccountWorkspaceService();
        var results = await service.GetAccountsAsync(search: query);
        Assert.Equal(expectedCount, results.Count);
    }

    [Theory]
    [InlineData("connected", 6)]
    [InlineData("unverified", 3)]
    [InlineData("attention", 2)]
    [InlineData("favorite", 1)]
    public async Task FilterReturnsCorrectAccounts(string filter, int expectedCount)
    {
        var service = new PreviewAccountWorkspaceService();
        var results = await service.GetAccountsAsync(filter: filter);
        Assert.Equal(expectedCount, results.Count);
    }

    [Fact]
    public async Task SortingOrdersCorrectly()
    {
        var service = new PreviewAccountWorkspaceService();

        var newest = await service.GetAccountsAsync(sort: "newest");
        Assert.True(newest[0].TrackingDate >= newest[^1].TrackingDate);

        var oldest = await service.GetAccountsAsync(sort: "oldest");
        Assert.True(oldest[0].TrackingDate <= oldest[^1].TrackingDate);
    }

    [Fact]
    public async Task CreateAccountPreviewAddsAccountAndPreventsDuplicates()
    {
        var service = new PreviewAccountWorkspaceService();
        var newModel = new AccountEditorVm
        {
            Email = "newuser@example.com",
            DisplayName = "Tài khoản kiểm thử mới",
            Provider = "openai",
            RecordedPlan = "Plus",
            Status = "Connected",
            Tags = ["test", "integration"]
        };

        var createRes = await service.CreateAccountAsync(newModel);
        Assert.True(createRes.IsSuccess);

        var accounts = await service.GetAccountsAsync();
        Assert.Equal(13, accounts.Count);
        Assert.Contains(accounts, a => a.Email == "newuser@example.com");

        // Duplicate rejection
        var dupRes = await service.CreateAccountAsync(newModel);
        Assert.False(dupRes.IsSuccess);
        Assert.Equal("DUPLICATE_EMAIL", dupRes.ErrorCode);
    }

    [Fact]
    public async Task UpdateAccountPreviewModifiesRecordAndTracksDirtyState()
    {
        var service = new PreviewAccountWorkspaceService();
        var all = await service.GetAccountsAsync();
        var target = all.First(a => a.NumericId == 1);

        var editModel = new AccountEditorVm
        {
            AccountId = target.AccountId,
            Email = target.Email,
            DisplayName = "Tên cập nhật mới",
            Notes = "Ghi chú đã sửa đổi",
            Provider = target.Provider,
            Tags = ["updated", "tag"],
            RecordedPlan = "Pro",
            Status = "Connected"
        };

        var updateRes = await service.UpdateAccountAsync(target.AccountId, editModel);
        Assert.True(updateRes.IsSuccess);

        var updatedDetail = await service.GetAccountDetailAsync(target.AccountId);
        Assert.NotNull(updatedDetail);
        Assert.Equal("Tên cập nhật mới", updatedDetail.DisplayName);
        Assert.Equal("Ghi chú đã sửa đổi", updatedDetail.Notes);
        Assert.Equal("Pro", updatedDetail.RecordedPlan);
    }

    [Fact]
    public async Task ArchiveAndRestoreOperatesCorrectly()
    {
        var service = new PreviewAccountWorkspaceService();
        var all = await service.GetAccountsAsync();
        var target = all.First(a => a.NumericId == 2);

        var archiveRes = await service.ArchiveAccountAsync(target.AccountId);
        Assert.True(archiveRes.IsSuccess);

        var afterArchive = await service.GetAccountsAsync();
        Assert.Equal(11, afterArchive.Count);
        Assert.DoesNotContain(afterArchive, a => a.AccountId == target.AccountId);

        var restoreRes = await service.RestoreAccountAsync(target.AccountId);
        Assert.True(restoreRes.IsSuccess);

        var afterRestore = await service.GetAccountsAsync();
        Assert.Equal(12, afterRestore.Count);
    }

    [Fact]
    public async Task AuthenticatorPreviewVaultLockAndCodeGeneration()
    {
        var authPort = new PreviewAuthenticatorUiPort();
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111101");

        // Initially unlocked
        Assert.False(await authPort.IsVaultLockedAsync());
        var creds = await authPort.GetCredentialsAsync(accountId);
        Assert.NotEmpty(creds);

        var cred = creds[0];
        var code = await authPort.GetCurrentCodeAsync(cred.CredentialId);
        Assert.NotNull(code);
        Assert.Equal(6, code.Code.Length);
        Assert.True(code.IsDemo);
        Assert.InRange(code.RemainingSeconds, 1, 30);

        // Lock vault
        await authPort.LockVaultAsync();
        Assert.True(await authPort.IsVaultLockedAsync());
        Assert.Empty(await authPort.GetCredentialsAsync(accountId));
        Assert.Null(await authPort.GetCurrentCodeAsync(cred.CredentialId));

        // Unlock vault
        var unlockRes = await authPort.UnlockVaultAsync("any-passphrase");
        Assert.True(unlockRes.IsSuccess);
        Assert.False(await authPort.IsVaultLockedAsync());
    }

    [Fact]
    public async Task ConnectionsPortReturnsTruthfulPendingStatus()
    {
        var connPort = new PreviewConnectionUiPort();
        var accountId = Guid.NewGuid();

        var status = await connPort.GetStatusAsync(accountId);
        Assert.NotNull(status);
        Assert.False(status.IsBackendIntegrated);

        var reconnectRes = await connPort.RequestReconnectAsync(accountId);
        Assert.False(reconnectRes.IsSuccess);
        Assert.Equal("BACKEND_PENDING", reconnectRes.ErrorCode);
    }

    [Fact]
    public async Task SubscriptionsPortRecordsObservations()
    {
        var subPort = new PreviewSubscriptionUiPort();
        var accountId = Guid.NewGuid();

        var initial = await subPort.GetSubscriptionAsync(accountId);
        Assert.NotNull(initial);
        Assert.Equal("Plus", initial.Plan);

        var recordRes = await subPort.RecordManualObservationAsync(accountId, "Pro", DateTimeOffset.Now.AddDays(30), "Nâng cấp");
        Assert.True(recordRes.IsSuccess);

        var updated = await subPort.GetSubscriptionAsync(accountId);
        Assert.NotNull(updated);
        Assert.Equal("Pro", updated.Plan);
    }

    [Fact]
    public async Task RemindersPortHandlesLifecycle()
    {
        var reminderPort = new PreviewReminderUiPort();
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111101");

        var initial = await reminderPort.GetRemindersAsync(accountId);
        Assert.NotEmpty(initial);

        var addRes = await reminderPort.CreateReminderAsync(accountId, "Nhắc nhở kiểm tra", DateTimeOffset.Now.AddDays(5), "Gia hạn");
        Assert.True(addRes.IsSuccess);

        var afterAdd = await reminderPort.GetRemindersAsync(accountId);
        Assert.Equal(initial.Count + 1, afterAdd.Count);

        var newReminder = afterAdd.First(r => r.Title == "Nhắc nhở kiểm tra");
        await reminderPort.ToggleCompleteAsync(newReminder.ReminderId);

        var afterToggle = await reminderPort.GetRemindersAsync(accountId);
        var toggled = afterToggle.First(r => r.ReminderId == newReminder.ReminderId);
        Assert.True(toggled.IsCompleted);

        await reminderPort.DeleteReminderAsync(newReminder.ReminderId);
        var afterDelete = await reminderPort.GetRemindersAsync(accountId);
        Assert.Equal(initial.Count, afterDelete.Count);
    }

    [Fact]
    public async Task ImportExportPortPreviewsCsvWithoutDatabaseCommit()
    {
        var port = new PreviewImportExportUiPort();
        var csv = "email,ten,provider,plan,tags\ntest1@example.com,Test 1,openai,Plus,import\ninvalid-email,Test 2,chatgpt,Free,test";

        var preview = await port.ParseAndPreviewCsvAsync("test.csv", csv);
        Assert.Equal(2, preview.TotalRows);
        Assert.Equal(1, preview.ValidRows);
        Assert.Equal(1, preview.ErrorRows);

        var commitRes = await port.CommitImportAsync(preview, "skip");
        Assert.False(commitRes.IsSuccess);
        Assert.Equal("BACKEND_PENDING", commitRes.ErrorCode);
    }

    [Fact]
    public async Task SettingsPortPersistsPreferences()
    {
        var port = new PreviewSettingsUiPort();
        var initial = await port.GetSettingsAsync();
        Assert.Equal("light", initial.Theme);

        initial.Theme = "dark";
        initial.MaskAccountEmails = true;

        var saveRes = await port.SaveSettingsAsync(initial);
        Assert.True(saveRes.IsSuccess);

        var updated = await port.GetSettingsAsync();
        Assert.Equal("dark", updated.Theme);
        Assert.True(updated.MaskAccountEmails);
    }
}
