using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;

namespace WretchedWhispers.Api.Configuration;

/// <summary>
/// Mutable holder for the desktop user's own OpenAI-compatible credentials: key, model, and an optional
/// base URL (leave empty for OpenAI itself; set e.g. https://openrouter.ai/api/v1 for OpenRouter). Entered
/// at runtime on the first-run settings screen, so it lives here rather than in immutable config.
/// Thread-safe: read via <see cref="Snapshot"/>, written via <see cref="Update"/> from POST /settings.
/// </summary>
public sealed class DesktopLlmOptions(string apiKey, string model, string baseUrl = "")
{
    private readonly Lock _gate = new();
    private string _apiKey = apiKey;
    private string _model = string.IsNullOrWhiteSpace(model) ? "gpt-4o" : model;
    private string _baseUrl = baseUrl ?? "";

    public bool HasKey
    {
        get { lock (_gate) return !string.IsNullOrWhiteSpace(_apiKey); }
    }

    public (string ApiKey, string Model, string BaseUrl) Snapshot()
    {
        lock (_gate) return (_apiKey, _model, _baseUrl);
    }

    public void Update(string apiKey, string model, string baseUrl)
    {
        lock (_gate)
        {
            _apiKey = apiKey ?? "";
            _model = string.IsNullOrWhiteSpace(model) ? "gpt-4o" : model.Trim();
            _baseUrl = baseUrl?.Trim() ?? "";
        }
    }
}

/// <summary>
/// <see cref="IChatClient"/> that (re)builds the underlying OpenAI-compatible client lazily from the
/// current <see cref="DesktopLlmOptions"/>. Lets a freshly-pasted key / model / base URL take effect
/// without restarting the app, and throws a friendly error (not a raw 401) when no key is set yet. Same
/// transport resilience as the hosted Azure client: bounded per-request timeout + transport retries.
/// </summary>
public sealed class ReloadableOpenAIChatClient : IChatClient
{
    private readonly DesktopLlmOptions _options;
    private readonly TimeSpan _timeout;
    private readonly int _maxRetries;
    private readonly Lock _gate = new();
    private IChatClient? _inner;
    private (string Key, string Model, string BaseUrl) _built;

    public ReloadableOpenAIChatClient(DesktopLlmOptions options, TimeSpan timeout, int maxRetries)
    {
        _options = options;
        _timeout = timeout;
        _maxRetries = maxRetries;
    }

    private IChatClient Inner()
    {
        var current = _options.Snapshot();
        if (string.IsNullOrWhiteSpace(current.ApiKey))
            throw new InvalidOperationException(
                "No API key configured. Open Settings and paste your key to begin.");

        lock (_gate)
        {
            if (_inner is null || current != _built)
            {
                _inner?.Dispose();

                var clientOptions = new OpenAIClientOptions
                {
                    NetworkTimeout = _timeout,
                    RetryPolicy = new ClientRetryPolicy(_maxRetries)
                };
                // Empty base URL → OpenAI's default endpoint. Set it for OpenAI-compatible gateways
                // (OpenRouter, Together, a local server, …).
                if (!string.IsNullOrWhiteSpace(current.BaseUrl))
                    clientOptions.Endpoint = new Uri(current.BaseUrl);

                _inner = new OpenAIClient(new ApiKeyCredential(current.ApiKey), clientOptions)
                    .GetChatClient(current.Model)
                    .AsIChatClient();
                _built = current;
            }
            return _inner;
        }
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => Inner().GetResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => Inner().GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null)
        => _inner?.GetService(serviceType, serviceKey);

    public void Dispose()
    {
        lock (_gate) _inner?.Dispose();
    }
}
