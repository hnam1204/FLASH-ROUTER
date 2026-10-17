using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Persistence;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Accounts.Persistence;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.CredentialResolution;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Providers.OpenAIPlatform;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Providers.OpenRouter;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Providers.Anthropic;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Providers.DeepSeek;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Providers.Gemini;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Providers.Groq;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Verification;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Registry;
using FlashAI.Modules.Connections.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.IntegrationTests;

public sealed class Be04ProviderTests
{
    [Fact]
    public void ProviderDescriptorsDeclareFixedOfficialHostsAndCapabilities()
    {
        var descriptors = new IAiProviderAdapter[]
        {
            CreateOpenRouter(new FixtureHandler(_ => JsonResponse("{\"data\":{\"label\":\"test\",\"usage\":\"1.2\"}}"))),
            CreateOpenAi(new FixtureHandler(_ => JsonResponse("{\"data\":[{\"id\":\"gpt-test\"}]}")))
        }.Select(adapter => adapter.Descriptor).ToArray();

        Assert.Equal(new[] { "openai-platform", "openrouter" }, descriptors.Select(item => item.ProviderId).OrderBy(item => item));
        Assert.All(descriptors, descriptor => Assert.All(descriptor.EndpointAllowlist, host => Assert.DoesNotContain("/", host)));
        Assert.Contains(ProviderCapability.Credits, descriptors.Single(item => item.ProviderId == "openrouter").SupportedCapabilities);
        Assert.Contains(ProviderCapability.Costs, descriptors.Single(item => item.ProviderId == "openai-platform").SupportedCapabilities);
    }

    [Fact]
    public void OpenRouterStandardKeyKeepsOfficialKeyUsageButNotManagementCredits()
    {
        var adapter = CreateOpenRouter(new FixtureHandler(_ => JsonResponse("{\"data\":{\"usage\":\"1.0\"}}")));
        var connection = Context("openrouter", ProviderCredentialKind.ApiKey);
        var capabilities = new DefaultProviderCapabilityResolver().Resolve(connection, adapter.Descriptor);

        Assert.Contains(ProviderCapability.Usage, capabilities);
        Assert.DoesNotContain(ProviderCapability.Credits, capabilities);
        Assert.DoesNotContain(ProviderCapability.Balance, capabilities);
    }

    [Fact]
    public async Task OpenRouterMapsNullableKeyAndCreditMetadataWithoutLiveCalls()
    {
        var handler = new FixtureHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/key" => JsonResponse("{\"data\":{\"label\":\"local-test\",\"usage\":\"1.2500\",\"limit\":null,\"limit_remaining\":\"2.5000\"}}"),
            "/api/v1/credits" => JsonResponse("{\"data\":{\"total_credits\":\"10.0000\",\"total_usage\":\"2.2500\"}}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var adapter = CreateOpenRouter(handler);
        var connection = Context("openrouter", ProviderCredentialKind.ApiKey);
        var result = await adapter.CheckAsync(
            connection,
            Credential(connection, "synthetic-openrouter-key"),
            new ProviderCheckRequest(new HashSet<ProviderCapability> { ProviderCapability.Usage, ProviderCapability.Credits }));

        Assert.Equal(ProviderOperationStatus.Success, result.Status);
        Assert.Contains(result.Observations, item => item.MetricName == "usage" && item.Value == "1.2500");
        Assert.Contains(result.Observations, item => item.MetricName == "remaining_credits" && item.Value == "7.7500");
        Assert.All(handler.Requests, request => Assert.Contains("Bearer synthetic-openrouter-key", request.Headers.Authorization?.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpenAiStandardCredentialCannotRequestAdminUsage()
    {
        var handler = new FixtureHandler(_ => throw new InvalidOperationException("The standard key must not call an admin endpoint."));
        var adapter = CreateOpenAi(handler);
        var connection = Context("openai-platform", ProviderCredentialKind.ApiKey);
        var result = await adapter.CheckAsync(
            connection,
            Credential(connection, "synthetic-openai-key"),
            new ProviderCheckRequest(new HashSet<ProviderCapability> { ProviderCapability.Usage }));

        Assert.Equal(ProviderOperationStatus.Forbidden, result.Status);
        Assert.Equal("INSUFFICIENT_PERMISSION", result.SafeErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ProviderHttpMapsRateLimitAndMalformedResponsesSafely()
    {
        var rateLimited = new FixtureHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30)) }
        });
        var adapter = CreateOpenRouter(rateLimited);
        var connection = Context("openrouter", ProviderCredentialKind.ApiKey);
        var result = await adapter.CheckAsync(connection, Credential(connection, "synthetic-key"), new ProviderCheckRequest(new HashSet<ProviderCapability> { ProviderCapability.Usage }));
        Assert.Equal(ProviderOperationStatus.RateLimited, result.Status);
        Assert.Equal("PROVIDER_RATE_LIMITED", result.SafeErrorCode);
        Assert.DoesNotContain("synthetic-key", result.SafeUserMessage, StringComparison.Ordinal);

        var malformed = new FixtureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "application/json")
        });
        result = await CreateOpenRouter(malformed).CheckAsync(connection, Credential(connection, "synthetic-key"), new ProviderCheckRequest(new HashSet<ProviderCapability> { ProviderCapability.Usage }));
        Assert.Equal(ProviderOperationStatus.InvalidResponse, result.Status);
        Assert.Equal("PROVIDER_OPERATION_FAILED", result.SafeErrorCode);

        var mismatched = Credential(connection with { ProviderId = "openai-platform" }, "synthetic-key");
        result = await adapter.CheckAsync(connection, mismatched, new ProviderCheckRequest(new HashSet<ProviderCapability> { ProviderCapability.Usage }));
        Assert.Equal("PROVIDER_CREDENTIAL_MISMATCH", result.SafeErrorCode);
        Assert.Equal(1, malformed.Requests.Count);
    }

    [Fact]
    public async Task AllProviderAdaptersUseOfflineFixturesAndKeepUnsupportedMetricsTruthful()
    {
        var handler = new FixtureHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            return path.Contains("/v1beta/models", StringComparison.Ordinal)
                ? JsonResponse("{\"models\":[{\"name\":\"models/gemini-test\"}]}")
                : path.Contains("/user/balance", StringComparison.Ordinal)
                    ? JsonResponse("{\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"10.00\",\"granted_balance\":\"2.00\",\"topped_up_balance\":\"8.00\"}]}")
                    : JsonResponse("{\"data\":[{\"id\":\"synthetic-model\"}]}");
        });
        var clock = new SystemClock();
        var mapper = new ProviderErrorMapper(clock);
        var executor = new ProviderHttpExecutor(new FixtureHttpClientFactory(handler));
        var adapters = new IAiProviderAdapter[]
        {
            new AnthropicAdapter(clock, mapper, executor),
            new GeminiAdapter(clock, mapper, executor),
            new DeepSeekAdapter(clock, mapper, executor),
            new GroqAdapter(clock, mapper, executor)
        };

        foreach (var adapter in adapters)
        {
            var connection = Context(adapter.Descriptor.ProviderId, ProviderCredentialKind.AdminApiKey);
            var result = await adapter.CheckAsync(connection, Credential(connection, "synthetic-key"), new ProviderCheckRequest(new HashSet<ProviderCapability> { ProviderCapability.ConnectionHealth }));
            Assert.True(result.IsSuccess, $"{adapter.Descriptor.ProviderId}: {result.SafeErrorCode}");
            Assert.NotEmpty(result.Observations);
        }

        var gemini = adapters.Single(adapter => adapter.Descriptor.ProviderId == "gemini");
        var geminiConnection = Context("gemini", ProviderCredentialKind.ApiKey);
        var unsupported = await gemini.CheckAsync(geminiConnection, Credential(geminiConnection, "synthetic-key"), new ProviderCheckRequest(new HashSet<ProviderCapability> { ProviderCapability.Quota }));
        Assert.Equal(ProviderOperationStatus.Unsupported, unsupported.Status);
        Assert.Empty(unsupported.Observations);
    }

    [Fact]
    public async Task ProviderCredentialVaultUsesProviderAndConnectionIsolation()
    {
        var root = Path.Combine(WorkspaceRoot(), ".local", "test-data", "be04-vault-" + Guid.NewGuid().ToString("N"));
        var provider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root, "keys")), builder => builder.SetApplicationName("FLASH ROUTER BE-04 TEST"));
        using var vault = new FileSecretVault(Path.Combine(root, "vault"), provider);
        var store = new ProviderCredentialVaultStore(vault);
        var first = Context("openrouter", ProviderCredentialKind.ApiKey);
        var second = first with { ConnectionId = Guid.NewGuid() };
        await store.StoreAsync(first, "first-secret");
        await store.StoreAsync(second, "second-secret");

        Assert.Equal("first-secret", (await store.ResolveAsync(first))!.Value);
        Assert.Equal("second-secret", (await store.ResolveAsync(second))!.Value);
        Assert.Null(await vault.ReadAsync($"connections/api/openai-platform/{first.ConnectionId:N}/ApiKey"));

        await store.RemoveAsync(first);
        Assert.Null(await store.ResolveAsync(first));
        Assert.Equal("second-secret", (await store.ResolveAsync(second))!.Value);

        vault.Dispose();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProviderObservationMigrationCreatesSingleSharedSchemaInMemory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FlashAiDbContext>().UseSqlite(connection, sqlite => sqlite.MigrationsAssembly(typeof(FLASH_ROUTER.Persistence.FlashAiDesignTimeDbContextFactory).Assembly.GetName().Name)).Options;
        await using var db = new FlashAiDbContext(options, [new AccountModelContributor(), new ConnectionModelContributor()]);
        await db.Database.MigrateAsync();

        var tables = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'").ToListAsync();
        Assert.Contains("ProviderCredentialReferences", tables);
        Assert.Contains("ProviderCheckRuns", tables);
        Assert.Contains("ProviderObservations", tables);
        Assert.Contains("ProviderSyncStates", tables);
        Assert.Contains("ProviderConnections", tables);
    }

    [Fact]
    public async Task ExistingDatabaseCanMigrateOnDisposableCopyWithoutChangingSource()
    {
        var root = WorkspaceRoot();
        var source = Path.Combine(root, ".local", "data", "flashrouter.db");
        if (!File.Exists(source))
        {
            return;
        }

        var copy = Path.Combine(root, ".local", "test-data", "be04-migration-copy-" + Guid.NewGuid().ToString("N") + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(source, copy);
        var sourceLength = new FileInfo(source).Length;
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={copy};Foreign Keys=True;Pooling=False"))
            {
                await connection.OpenAsync();
                var options = new DbContextOptionsBuilder<FlashAiDbContext>().UseSqlite(connection, sqlite => sqlite.MigrationsAssembly(typeof(FLASH_ROUTER.Persistence.FlashAiDesignTimeDbContextFactory).Assembly.GetName().Name)).Options;
                await using (var db = new FlashAiDbContext(options, [new AccountModelContributor(), new ConnectionModelContributor()]))
                {
                    await db.Database.MigrateAsync();
                    var providerTables = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table' AND name LIKE 'Provider%'").ToListAsync();
                    Assert.Contains("ProviderObservations", providerTables);
                    Assert.Equal(sourceLength, new FileInfo(source).Length);
                }
            }
        }
        finally
        {
            foreach (var path in new[] { copy, copy + "-wal", copy + "-shm" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    private static OpenRouterAdapter CreateOpenRouter(FixtureHandler handler)
        => new(new SystemClock(), new ProviderErrorMapper(new SystemClock()), new ProviderHttpExecutor(new FixtureHttpClientFactory(handler)));

    private static OpenAiPlatformAdapter CreateOpenAi(FixtureHandler handler)
        => new(new SystemClock(), new ProviderErrorMapper(new SystemClock()), new ProviderHttpExecutor(new FixtureHttpClientFactory(handler)));

    private static ProviderConnectionContext Context(string providerId, ProviderCredentialKind credentialKind)
        => new(Guid.NewGuid(), Guid.NewGuid(), providerId, ConnectionKind.ApiKey, credentialKind, "cred_test", ConnectionStatus.CredentialStored, new HashSet<string>(), null, DateTimeOffset.UtcNow);

    private static ProviderCredential Credential(ProviderConnectionContext context, string value)
        => new(context.ProviderId, context.ConnectionId, context.CredentialId, context.CredentialKind, value, context.Scope);

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string WorkspaceRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "FLASH ROUTER.sln"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the solution root.");
    }

    private sealed class FixtureHttpClientFactory : IHttpClientFactory
    {
        private readonly FixtureHandler _handler;

        public FixtureHttpClientFactory(FixtureHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            var host = name switch
            {
                "provider.openrouter" => "openrouter.ai",
                "provider.openai-platform" => "api.openai.com",
                "provider.anthropic" => "api.anthropic.com",
                "provider.gemini" => "generativelanguage.googleapis.com",
                "provider.deepseek" => "api.deepseek.com",
                "provider.groq" => "api.groq.com",
                _ => "fixture.invalid"
            };
            return new HttpClient(_handler) { BaseAddress = new Uri($"https://{host}/") };
        }
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

        public FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _response = response;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var response = _response(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
