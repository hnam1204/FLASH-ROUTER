using System.Text.RegularExpressions;

namespace FlashAI.ArchitectureTests;

public sealed class ArchitectureRulesTests
{
    private static readonly string[] ModuleNames =
    [
        "Dashboard", "Accounts", "Connections", "Subscriptions", "Authenticator",
        "ImportExport", "Reminders", "BackupRestore", "Activity", "Settings", "Diagnostics"
    ];

    private static readonly string[] InteractiveModuleNames =
    [
        "Dashboard", "Connections", "Subscriptions", "Authenticator",
        "ImportExport", "Reminders", "BackupRestore", "Activity", "Settings", "Diagnostics"
    ];

    [Fact]
    public void EveryModuleHasRegistrationReadmeRouteAndIsolatedCss()
    {
        var root = WorkspaceRoot();

        foreach (var module in ModuleNames)
        {
            var moduleRoot = Path.Combine(root, "src", "Modules", module);
            Assert.True(File.Exists(Path.Combine(moduleRoot, $"{module}ModuleRegistration.cs")), module);
            Assert.True(File.Exists(Path.Combine(moduleRoot, "README.md")), module);
        }

        foreach (var module in InteractiveModuleNames)
        {
            var moduleRoot = Path.Combine(root, "src", "Modules", module);
            var routePage = Directory.GetFiles(Path.Combine(moduleRoot, "UI"), "*.razor")
                .Single(path => !Path.GetFileName(path).Equals("_Imports.razor", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(routePage + ".css"), routePage);
        }
    }

    [Fact]
    public void ModuleProjectsReferenceFoundationOnly()
    {
        var root = WorkspaceRoot();

        foreach (var module in ModuleNames)
        {
            var projectPath = Directory.GetFiles(Path.Combine(root, "src", "Modules", module), "*.csproj").Single();
            var project = File.ReadAllText(projectPath);
            var references = Regex.Matches(project, "ProjectReference Include=\"([^\"]+)\"")
                .Select(match => match.Groups[1].Value)
                .ToArray();

            Assert.Single(references);
            Assert.Contains("FlashAI.Foundation.csproj", references[0], StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("src\\Modules", references[0], StringComparison.OrdinalIgnoreCase);
        }
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
        var source = string.Join("\n", sourceRoots.SelectMany(path => Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText)));

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

        foreach (var module in ModuleNames)
        {
            var prefix = Regex.Replace(module, "([a-z])([A-Z])", "$1-$2").ToLowerInvariant();
            Assert.DoesNotContain($".{prefix}-", globalCss, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AllRequiredRoutesExistExactlyOnce()
    {
        var root = WorkspaceRoot();
        var expectedRoutes = new[]
        {
            "/dashboard", "/connections", "/subscriptions", "/authenticator",
            "/import-export", "/reminders", "/backups", "/activity", "/settings", "/diagnostics"
        };
        var razorFiles = Directory.GetFiles(Path.Combine(root, "src", "Modules"), "*.razor", SearchOption.AllDirectories);
        var contents = razorFiles.Select(File.ReadAllText).ToArray();

        foreach (var route in expectedRoutes)
        {
            Assert.Equal(1, contents.Count(content => content.Contains($"@page \"{route}\"", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void LegacyAccountManagerUiIsHardRemovedFromTheRouteGraph()
    {
        var root = WorkspaceRoot();
        var hostRoot = Path.Combine(root, "FLASH ROUTER");
        var accountModuleRoot = Path.Combine(root, "src", "Modules", "Accounts");
        var sourceFiles = Directory.GetFiles(hostRoot, "*.razor", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(accountModuleRoot, "*.razor", SearchOption.AllDirectories))
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

    [Fact]
    public void AllModulesHaveFeatureFirstStructure()
    {
        var root = WorkspaceRoot();

        foreach (var module in ModuleNames)
        {
            var featuresRoot = Path.Combine(root, "src", "Modules", module, "Features");
            Assert.True(Directory.Exists(featuresRoot), $"Module {module} must have a Features directory.");

            var featureDirs = Directory.GetDirectories(featuresRoot);
            Assert.True(featureDirs.Length > 0, $"Module {module} must have at least one feature under Features/.");

            foreach (var featureDir in featureDirs)
            {
                var subDirs = Directory.GetDirectories(featureDir)
                    .Select(Path.GetFileName)
                    .ToArray();

                Assert.True(subDirs.Contains("UI") || subDirs.Contains("Application") || subDirs.Contains("Contracts"),
                    $"Feature {featureDir} in module {module} must contain a UI, Application, or Contracts directory.");
            }
        }
    }

    [Fact]
    public void FeatureRazorComponentsHaveIsolatedCss()
    {
        var root = WorkspaceRoot();

        foreach (var module in ModuleNames)
        {
            var featuresRoot = Path.Combine(root, "src", "Modules", module, "Features");
            if (!Directory.Exists(featuresRoot)) continue;

            var razorFiles = Directory.GetFiles(featuresRoot, "*.razor", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).Equals("_Imports.razor", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (var razorFile in razorFiles)
            {
                var cssFile = razorFile + ".css";
                Assert.True(File.Exists(cssFile), $"Razor component {razorFile} must have isolated CSS file: {cssFile}");
            }
        }
    }

    private static string WorkspaceRoot()
    {
        var candidates = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (var candidate in candidates)
        {
            var current = new DirectoryInfo(candidate);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "FLASH ROUTER.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the FLASH ROUTER solution root.");
    }
}
