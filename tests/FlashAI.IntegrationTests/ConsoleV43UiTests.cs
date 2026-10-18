namespace FlashAI.IntegrationTests;

public sealed class ConsoleV43UiTests
{
    private static string WorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "FLASH ROUTER.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Workspace root not found.");
    }

    [Fact]
    public void SuppliedBrandAssetsExistAndPlatformLogoUsesLocalPaths()
    {
        var root = WorkspaceRoot();
        var assetRoot = Path.Combine(root, "FLASH ROUTER", "wwwroot", "assets", "brands");

        foreach (var asset in new[] { "flash-router.png", "chatgpt.png", "claude.png", "gemini.png" })
        {
            var path = Path.Combine(assetRoot, asset);
            Assert.True(File.Exists(path), $"Missing local brand asset: {path}");
            Assert.NotEqual(0, new FileInfo(path).Length);
        }

        var logo = File.ReadAllText(Path.Combine(root, "FLASH ROUTER", "Components", "Console", "Components", "PlatformLogo.razor"));
        Assert.Contains("assets/brands/chatgpt.png", logo, StringComparison.Ordinal);
        Assert.Contains("assets/brands/claude.png", logo, StringComparison.Ordinal);
        Assert.Contains("assets/brands/gemini.png", logo, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", logo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", logo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConsoleUsesOnePageHeaderAddAccountActionWithoutSidebar()
    {
        var root = WorkspaceRoot();
        var console = File.ReadAllText(Path.Combine(root, "FLASH ROUTER", "Components", "Console", "AutoLoginConsole.razor"));
        var toolbar = File.ReadAllText(Path.Combine(root, "FLASH ROUTER", "Components", "Console", "Components", "AccountFilterToolbar.razor"));

        Assert.DoesNotContain("<AppSidebar", console, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(console, "class=\"page-primary-action\""));
        Assert.DoesNotContain("class=\"toolbar-add-btn\"", toolbar, StringComparison.Ordinal);
        Assert.Contains("Quản lý tài khoản", console, StringComparison.Ordinal);
        Assert.Contains("Thêm tài khoản", console, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountConsoleDoesNotFabricateSessionMfaOrActivityData()
    {
        var root = WorkspaceRoot();
        var table = File.ReadAllText(Path.Combine(root, "FLASH ROUTER", "Components", "Console", "Components", "AccountsTable.razor"));
        var inspector = File.ReadAllText(Path.Combine(root, "FLASH ROUTER", "Components", "Console", "Components", "AccountInspector.razor"));
        var console = File.ReadAllText(Path.Combine(root, "FLASH ROUTER", "Components", "Console", "AutoLoginConsole.razor"));

        foreach (var source in new[] { table, inspector, console })
        {
            Assert.DoesNotContain("+12%", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GetHashCode", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AddMinutes", source, StringComparison.Ordinal);
        }

        Assert.Contains("Chưa xác minh", table, StringComparison.Ordinal);
        Assert.Contains("Chưa có sự kiện hoạt động", inspector, StringComparison.Ordinal);
        Assert.Contains("ActivityEvents.Count > 0", inspector, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsoleLogBackfillsHistoryExplainsEmptyFiltersAndDeduplicatesLiveEvents()
    {
        var root = WorkspaceRoot();
        var console = File.ReadAllText(Path.Combine(root, "FLASH ROUTER", "Components", "Console", "AutoLoginConsole.razor"));
        var activityPort = File.ReadAllText(Path.Combine(root, "src", "Modules", "Operations", "Activity", "Application", "SqliteActivityUiPort.cs"));
        var activityContract = File.ReadAllText(Path.Combine(root, "src", "Modules", "Operations", "Activity", "Contracts", "IActivityUiPort.cs"));

        Assert.Contains("ActivityPort.GetEventsAsync(accountId)", console, StringComparison.Ordinal);
        Assert.Contains("GroupBy(item => item.EventId)", console, StringComparison.Ordinal);
        Assert.Contains("GetConsoleLogEmptyMessage", console, StringComparison.Ordinal);
        Assert.Contains("Xóa bộ lọc", console, StringComparison.Ordinal);
        Assert.Contains("ClearConsoleLogView", console, StringComparison.Ordinal);
        Assert.Contains("Guid? attemptId = null", activityContract, StringComparison.Ordinal);
        Assert.Contains("item.AttemptId == attemptId.Value", activityPort, StringComparison.Ordinal);
        Assert.Contains("Attempt:", console, StringComparison.Ordinal);
    }

    [Fact]
    public void V43ConsoleKeepsVietnameseLabelsReadable()
    {
        var root = WorkspaceRoot();
        var files = new[]
        {
            Path.Combine(root, "FLASH ROUTER", "Components", "Console", "AutoLoginConsole.razor"),
            Path.Combine(root, "FLASH ROUTER", "Components", "Console", "Components", "AccountsTable.razor"),
            Path.Combine(root, "FLASH ROUTER", "Components", "Console", "Components", "AccountInspector.razor"),
            Path.Combine(root, "FLASH ROUTER", "Components", "Console", "Components", "KpiCardsSection.razor")
        };

        var source = string.Join(Environment.NewLine, files.Select(File.ReadAllText));

        Assert.Contains("Quản lý tài khoản", source, StringComparison.Ordinal);
        Assert.Contains("Đã xác minh", source, StringComparison.Ordinal);
        Assert.Contains("Chưa xác minh", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Ã", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Â", source, StringComparison.Ordinal);
        Assert.DoesNotContain("â€", source, StringComparison.Ordinal);
        Assert.DoesNotContain("�", source, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string needle)
        => value.Split(needle, StringSplitOptions.None).Length - 1;
}
