using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Registry;

namespace FlashAI.Modules.Connections.Features.ProviderIntegration.Verification;

public sealed class ProviderHttpExecutor
{
    private const int MaximumResponseBytes = 1_048_576;
    private static readonly IReadOnlyDictionary<string, string> OfficialHosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["provider.openrouter"] = "openrouter.ai",
        ["provider.openai-platform"] = "api.openai.com",
        ["provider.anthropic"] = "api.anthropic.com",
        ["provider.gemini"] = "generativelanguage.googleapis.com",
        ["provider.deepseek"] = "api.deepseek.com",
        ["provider.groq"] = "api.groq.com"
    };
    private readonly IHttpClientFactory _httpClientFactory;

    public ProviderHttpExecutor(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ProviderHttpPayload> GetJsonAsync(
        string clientName,
        string providerId,
        string path,
        string headerName,
        string credential,
        IReadOnlyDictionary<string, string>? additionalHeaders = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || path[0] != '/')
        {
            throw new InvalidOperationException("Provider paths must be fixed absolute paths.");
        }

        var client = _httpClientFactory.CreateClient(clientName);
        if (!OfficialHosts.TryGetValue(clientName, out var expectedHost)
            || client.BaseAddress is null
            || !string.Equals(client.BaseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(client.BaseAddress.Host, expectedHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProviderConfigurationException("PROVIDER_ENDPOINT_INVALID", "The provider endpoint is not an approved official HTTPS host.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation(headerName, credential);
        if (additionalHeaders is not null)
        {
            foreach (var header in additionalHeaders)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var retryAfter = ParseRetryAfter(response.Headers.RetryAfter);
        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderHttpException(response.StatusCode, retryAfter);
        }

        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new JsonException("The provider response exceeded the supported size.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var limited = new LimitedReadStream(stream, MaximumResponseBytes);
        var document = await JsonDocument.ParseAsync(limited, cancellationToken: cancellationToken);
        return new ProviderHttpPayload(document, response.Headers);
    }

    public async Task<ProviderHttpPayload> GetJsonWithQueryAsync(
        string clientName,
        string providerId,
        string path,
        IReadOnlyDictionary<string, string> query,
        string headerName,
        string credential,
        IReadOnlyDictionary<string, string>? additionalHeaders = null,
        CancellationToken cancellationToken = default)
    {
        var queryText = string.Join(
            '&',
            query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return await GetJsonAsync(
            clientName,
            providerId,
            string.IsNullOrEmpty(queryText) ? path : $"{path}?{queryText}",
            headerName,
            credential,
            additionalHeaders,
            cancellationToken);
    }

    private static TimeSpan? ParseRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is not null && retryAfter.Delta.Value >= TimeSpan.Zero)
        {
            return retryAfter.Delta.Value;
        }

        if (retryAfter?.Date is not null)
        {
            var delta = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            return delta >= TimeSpan.Zero ? delta : TimeSpan.Zero;
        }

        return null;
    }
}

public sealed class ProviderHttpPayload : IDisposable
{
    public ProviderHttpPayload(JsonDocument document, HttpResponseHeaders headers)
    {
        Document = document;
        Headers = headers;
    }

    public JsonDocument Document { get; }
    public HttpResponseHeaders Headers { get; }

    public void Dispose() => Document.Dispose();
}

internal sealed class LimitedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _limit;
    private long _read;

    public LimitedReadStream(Stream inner, long limit)
    {
        _inner = inner;
        _limit = limit;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _read;
    public override long Position { get => _read; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        Track(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken);
        Track(read);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => throw new NotSupportedException();
    public override Task FlushAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private void Track(int read)
    {
        _read += read;
        if (_read > _limit)
        {
            throw new InvalidDataException("The provider response exceeded the supported size.");
        }
    }
}
