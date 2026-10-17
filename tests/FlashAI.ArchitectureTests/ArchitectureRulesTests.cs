using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FlashAI.ArchitectureTests;

public sealed class ArchitectureRulesTests
{
    private static readonly string[] TargetProjectPaths =
    [
        @"FLASH ROUTER\FLASH ROUTER.csproj",
        @"src\FlashAI.Cli\FlashAI.Cli.csproj",
        @"src\FlashAI.Foundation\FlashAI.Foundation.csproj",
        @"src\Modules\Accounts\FlashAI.Modules.Accounts.csproj",
        @"src\Modules\Connections\FlashAI.Modules.Connections.csproj",
        @"src\Modules\Operations\FlashAI.Modules.Operations.csproj",
        @"src\Modules\Workspace\FlashAI.Modules.Workspace.csproj",
        @"tests\FlashAI.ArchitectureTests\FlashAI.ArchitectureTests.csproj",
        @"tests\FlashAI.IntegrationTests\FlashAI.IntegrationTests.csproj"
    ];

    private static readonly string[] BusinessModuleNames =
    [
        "Dashboard", "Accounts", "Connections", "Subscriptions", "Authenticator",
        "ImportExport", "Reminders", "BackupRestore", "Activity", "Settings", "Diagnostics"
    ];

    private static readonly Dictionary<string, string[]> BusinessModuleRoots =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Accounts"] = ["src/Modules/Accounts"],
            ["Authenticator"] = ["src/Modules/Accounts/Authenticator"],
            ["Connections"] = ["src/Modules/Connections"],
            ["Dashboard"] = ["src/Modules/Workspace/Dashboard"],
            ["Diagnostics"] = ["src/Modules/Workspace/Diagnostics"],
            ["Settings"] = ["src/Modules/Workspace/Settings"],
            ["Subscriptions"] = ["src/Modules/Workspace/Subscriptions"],
            ["Activity"] = ["src/Modules/Operations/Activity"],
            ["Reminders"] = ["src/Modules/Operations/Reminders"],
            ["BackupRestore"] = ["src/Modules/Operations/BackupRestore"],
            ["ImportExport"] = ["src/Modules/Operations/ImportExport"]
        };

    [Fact]
    public void SolutionContainsExactlyNineProjects()
    {
        var root = WorkspaceRoot();
        var solution = Path.Combine(root, "FLASH ROUTER.sln");
        var projects = File.ReadLines(solution)
            .Where(line => line.StartsWith("Project(", StringComparison.Ordinal))
            .Select(line => Regex.Match(line, "= \"[^\"]+\", \"([^\"]+\\.csproj)\""))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value.Replace('/', '\\'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(TargetProjectPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase), projects);
        Assert.Equal(9, SourceProjectFiles(root).Length);
    }

    [Fact]
    public void EveryBusinessModuleRetainsRegistrationReadmeAndFeatureBoundaries()
    {
        var root = WorkspaceRoot();

        foreach (var module in BusinessModuleNames)
        {
            foreach (var moduleRoot in ModuleRoots(root, module))
            {
                Assert.True(File.Exists(Path.Combine(moduleRoot, $"{module}ModuleRegistration.cs")), module);
                Assert.True(File.Exists(Path.Combine(moduleRoot, "README.md")), module);
                Assert.True(Directory.Exists(Path.Combine(moduleRoot, "Features")), module);
            }
        }
    }

    [Fact]
    public void ConsolidatedModuleProjectsReferenceFoundationOnly()
    {
        var root = WorkspaceRoot();
        var moduleProjects = new[]
        {
            Path.Combine(root, "src", "Modules", "Accounts", "FlashAI.Modules.Accounts.csproj"),
            Path.Combine(root, "src", "Modules", "Connections", "FlashAI.Modules.Connections.csproj"),
            Path.Combine(root, "src", "Modules", "Operations", "FlashAI.Modules.Operations.csproj"),
            Path.Combine(root, "src", "Modules", "Workspace", "FlashAI.Modules.Workspace.csproj")
        };

        foreach (var projectPath in moduleProjects)
        {
            var project = XDocument.Load(projectPath);
            var references = project.Descendants("ProjectReference")
                .Select(element => (string?)element.Attribute("Include"))
                .Where(reference => reference is not null)
                .Select(reference => reference!)
                .ToArray();

            Assert.Single(references);
            Assert.Contains("FlashAI.Foundation.csproj", references[0], StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("src\\Modules", references[0], StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void NoRetiredProjectReferencesOrCircularProjectReferencesRemain()
    {
        var root = WorkspaceRoot();
        var projects = SourceProjectFiles(root);
        var projectSet = projects.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var graph = projects.ToDictionary(
            project => project,
            project => ProjectReferences(project)
                .Select(reference => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, reference)))
                .Where(projectSet.Contains)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            var text = File.ReadAllText(project);
            Assert.DoesNotContain("FlashAI.Modules.Authenticator.csproj", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("FlashAI.Modules.Dashboard.csproj", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("FlashAI.Modules.Diagnostics.csproj", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("FlashAI.Modules.Settings.csproj", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("FlashAI.Modules.Subscriptions.csproj", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("FlashAI.Modules.Activity.csproj", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("FlashAI.Modules.Reminders.csproj", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("FlashAI.Modules.BackupRestore.csproj", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("FlashAI.Modules.ImportExport.csproj", text, StringComparison.OrdinalIgnoreCase);
        }

        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects)
            Assert.False(HasCycle(project, graph, visiting, visited), $"Circular reference found at {project}");
    }

    [Fact]
    public void FoundationAndModulesDoNotDependOnTheWebHost()
    {
        var root = WorkspaceRoot();
        var source = string.Join("\n", new[]
        {
            Path.Combine(root, "src", "FlashAI.Foundation"),
            Path.Combine(root, "src", "Modules")
        }.SelectMany(path => Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)))
            .Where(file => !IsBuildArtifact(file))
            .Select(File.ReadAllText));

        Assert.DoesNotContain("FLASH ROUTER.csproj", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FoundationAndHostDoNotContainForbiddenBulkAuthOrSecretPatterns()
    {
        var root = WorkspaceRoot();
        var sourceRoots = new[]
        {
            Path.Combine(root, "src", "FlashAI.Foundation"),
            Path.Combine(root, "FLASH ROUTER")
        };
        var source = string.Join("\n", sourceRoots.SelectMany(path => Directory.GetFiles(path, "*.*", SearchOption.AllDirectories))
            .Where(file => !IsBuildArtifact(file)
                && (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            .Select(File.ReadAllText));

        Assert.DoesNotContain("2fa.live", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwords", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bulk login", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccountCheckService", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FeatureStylesDoNotLeakIntoGlobalFoundationFiles()
    {
        var root = WorkspaceRoot();
        var globalFiles = new[]
        {
            Path.Combine(root, "FLASH ROUTER", "wwwroot", "app.css"),
            Path.Combine(root, "FLASH ROUTER", "wwwroot", "foundation", "shell.css"),
            Path.Combine(root, "FLASH ROUTER", "wwwroot", "foundation", "tokens.css")
        };
        var globalCss = string.Join("\n", globalFiles.Select(File.ReadAllText));

        foreach (var module in BusinessModuleNames)
        {
            var prefix = Regex.Replace(module, "([a-z])([A-Z])", "$1-$2").ToLowerInvariant();
            Assert.DoesNotContain($".{prefix}-", globalCss, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void RequiredRoutesExistExactlyOnceAndRetainIsolatedCss()
    {
        var root = WorkspaceRoot();
        var razorFiles = SourceFiles(Path.Combine(root, "src", "Modules"), "*.razor").ToArray();
        var contents = razorFiles.Select(File.ReadAllText).ToArray();
        var expectedRoutes = new[]
        {
            "/dashboard", "/connections", "/subscriptions", "/authenticator",
            "/import-export", "/reminders", "/backups", "/activity", "/settings", "/diagnostics"
        };

        foreach (var route in expectedRoutes)
            Assert.Single(contents, content => content.Contains($"@page \"{route}\"", StringComparison.Ordinal));

        foreach (var razorFile in razorFiles.Where(path => !Path.GetFileName(path).Equals("_Imports.razor", StringComparison.OrdinalIgnoreCase)))
            Assert.True(File.Exists(razorFile + ".css"), $"Razor component lacks isolated CSS: {razorFile}");
    }

    [Fact]
    public void ModuleAssemblyCatalogUsesEachConsolidatedAssemblyOnce()
    {
        var root = WorkspaceRoot();
        var catalog = File.ReadAllText(Path.Combine(root, "FLASH ROUTER", "Components", "ModuleAssemblyCatalog.cs"));
        var markers = new[]
        {
            "AccountsModuleMarker", "ConnectionsModuleMarker", "OperationsModuleMarker", "WorkspaceModuleMarker"
        };

        foreach (var marker in markers)
            Assert.Single(Regex.Matches(catalog, $"typeof\\({marker}\\)\\.Assembly"));

        Assert.DoesNotContain("DashboardModuleMarker", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("AuthenticatorModuleMarker", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivityModuleMarker", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyAccountManagerUiIsHardRemovedFromTheRouteGraph()
    {
        var root = WorkspaceRoot();
        var hostRoot = Path.Combine(root, "FLASH ROUTER");
        var accountModuleRoot = Path.Combine(root, "src", "Modules", "Accounts");
        var sourceFiles = Directory.GetFiles(hostRoot, "*.razor", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(accountModuleRoot, "*.razor", SearchOption.AllDirectories))
            .Where(file => !IsBuildArtifact(file))
            .ToArray();
        var source = string.Join("\n", sourceFiles.Select(File.ReadAllText));

        Assert.DoesNotContain("@page \"/legacy/accounts\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("@page \"/accounts\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("/legacy/accounts", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Account Manager legacy", source, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(hostRoot, "Components", "Pages", "LegacyAccounts.razor")));
        Assert.False(File.Exists(Path.Combine(accountModuleRoot, "UI", "AccountsPage.razor")));

        var home = File.ReadAllText(Path.Combine(hostRoot, "Components", "Pages", "Home.razor"));
        Assert.Contains("<AutoLoginConsole", home, StringComparison.Ordinal);
    }

    [Fact]
    public void HostLaunchSettingsAreLocalOnly()
    {
        var root = WorkspaceRoot();
        var launchSettings = File.ReadAllText(Path.Combine(root, "FLASH ROUTER", "Properties", "launchSettings.json"));

        Assert.DoesNotContain("0.0.0.0", launchSettings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("*:", launchSettings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("localhost", launchSettings, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ModuleRoots(string root, string module)
        => BusinessModuleRoots[module].Select(relative => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string[] SourceProjectFiles(string root)
        => Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(file => !IsBuildArtifact(file))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<string> SourceFiles(string root, string pattern)
        => Directory.GetFiles(root, pattern, SearchOption.AllDirectories).Where(file => !IsBuildArtifact(file));

    private static IEnumerable<string> ProjectReferences(string projectPath)
        => XDocument.Load(projectPath).Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(reference => reference is not null)
            .Select(reference => reference!);

    private static bool HasCycle(
        string project,
        IReadOnlyDictionary<string, string[]> graph,
        ISet<string> visiting,
        ISet<string> visited)
    {
        if (visiting.Contains(project)) return true;
        if (!visited.Add(project)) return false;

        visiting.Add(project);
        foreach (var reference in graph[project])
        {
            if (HasCycle(reference, graph, visiting, visited)) return true;
        }

        visiting.Remove(project);
        return false;
    }

    private static bool IsBuildArtifact(string path)
        => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || part.Equals("obj", StringComparison.OrdinalIgnoreCase));

    private static string WorkspaceRoot()
    {
        var candidates = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (var candidate in candidates)
        {
            var current = new DirectoryInfo(candidate);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "FLASH ROUTER.sln")))
                    return current.FullName;

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the FLASH ROUTER solution root.");
    }
}
