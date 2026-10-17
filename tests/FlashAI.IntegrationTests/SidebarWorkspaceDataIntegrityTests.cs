using Xunit;

namespace FlashAI.IntegrationTests;

public sealed class SidebarWorkspaceDataIntegrityTests
{
    private static string WorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FLASH ROUTER.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("FLASH ROUTER solution not found.");
    }

    [Fact]
    public void AllSidebarSectionsHaveDistinctNonPlaceholderWorkspaceBranches()
    {
        var root = WorkspaceRoot();
        var component = File.ReadAllText(Path.Combine(root, "FLASH ROUTER", "Components", "Console",
            "Components", "SidebarWorkspace.razor"));
        var host = File.ReadAllText(Path.Combine(root, "FLASH ROUTER", "Components", "Console", "AutoLoginConsole.razor"));

        foreach (var tab in new[] {
            "overview", "automation", "activity", "configuration", "members",
            "reports", "notifications", "settings"
        })
            Assert.Contains("ActiveTab == \"" + tab + "\"", component, StringComparison.Ordinal);

        Assert.Contains("<SidebarWorkspace", host, StringComparison.Ordinal);
        Assert.Contains("_activeTab == \"accounts\"", host, StringComparison.Ordinal);
        Assert.Contains("OnNavigate=\"HandleTabSelected\"", host, StringComparison.Ordinal);
        Assert.Contains("GetAccountsAsync", host, StringComparison.Ordinal);
        Assert.Contains("GetDiagnosticsAsync", host, StringComparison.Ordinal);
    }

    [Fact]
    public void NoFabricatedSessionExpiryMfaGrowthOrSuccessfulLoginEvents()
    {
        var root = WorkspaceRoot();
        var componentRoot = Path.Combine(root, "FLASH ROUTER", "Components", "Console", "Components");
        var table = File.ReadAllText(Path.Combine(componentRoot, "AccountsTable.razor"));
        var inspector = File.ReadAllText(Path.Combine(componentRoot, "AccountInspector.razor"));
        var kpis = File.ReadAllText(Path.Combine(componentRoot, "KpiCardsSection.razor"));

        Assert.DoesNotContain("GetHashCode()", table, StringComparison.Ordinal);
        Assert.DoesNotContain("Còn 12 ngày", inspector, StringComparison.Ordinal);
        Assert.DoesNotContain("width: 80%", inspector, StringComparison.Ordinal);
        Assert.DoesNotContain("Gia hạn phiên tự động", inspector, StringComparison.Ordinal);
        Assert.DoesNotContain("+12%", kpis, StringComparison.Ordinal);
        Assert.Contains("Chưa xác minh", table, StringComparison.Ordinal);
        Assert.Contains("Chưa xác minh", inspector, StringComparison.Ordinal);
    }

    [Fact]
    public void RealMemberAuditAndProviderCapabilitiesAreNotClaimedWithoutBackend()
    {
        var root = WorkspaceRoot();
        var component = File.ReadAllText(Path.Combine(root, "FLASH ROUTER", "Components", "Console",
            "Components", "SidebarWorkspace.razor"));
        var header = File.ReadAllText(Path.Combine(root, "FLASH ROUTER", "Components", "Console",
            "Components", "AppTopbar.razor"));

        Assert.Contains("PreviewActivityUiPort", component, StringComparison.Ordinal);
        Assert.Contains("chưa có member directory", component, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProviderRegistry.List()", component, StringComparison.Ordinal);
        Assert.DoesNotContain("Nguyễn Minh", header, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"user-role\">Admin", header, StringComparison.Ordinal);
    }
}
