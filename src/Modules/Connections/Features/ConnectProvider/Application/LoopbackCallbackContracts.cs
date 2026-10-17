using System.Net;

namespace FlashAI.Modules.Connections.Features.ConnectProvider.Application;

public interface ILoopbackCallbackEndpointFactory
{
    string CreateRedirectUri();
}

public sealed class FixedLoopbackCallbackEndpointFactory : ILoopbackCallbackEndpointFactory
{
    private readonly int _port;

    public FixedLoopbackCallbackEndpointFactory(int port = 1455)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        _port = port;
    }

    public string CreateRedirectUri() => $"http://127.0.0.1:{_port}{OpenAiOAuthOptions.CallbackPath}";
}

public static class LoopbackCallbackUri
{
    public static bool IsValid(string redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || !IPAddress.TryParse(uri.Host, out var address)
            || !IPAddress.IsLoopback(address)
            || uri.AbsolutePath != OpenAiOAuthOptions.CallbackPath
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        return uri.Port is > 0 and <= 65535;
    }
}
