using FlashAI.Foundation.Hosting;

namespace FlashAI.IntegrationTests;

public sealed class LocalBoundaryTests
{
    [Fact]
    public void LocalBindingGuardAcceptsLocalhostUrls()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["urls"] = "http://localhost:5270;https://127.0.0.1:7146"
            })
            .Build();

        LocalBindingGuard.ValidateConfiguration(configuration);
    }

    [Fact]
    public void LocalBindingGuardRejectsWildcardUrls()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["urls"] = "http://0.0.0.0:5270"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => LocalBindingGuard.ValidateConfiguration(configuration));
    }
}
