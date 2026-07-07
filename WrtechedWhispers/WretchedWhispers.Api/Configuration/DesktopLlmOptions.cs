using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;

namespace WretchedWhispers.Api.Configuration;

/// <summary>
/// Mutable holder for the desktop user's own OpenAI key + model. The key is entered at runtime
/// (first-run settings screen) and can change, so it lives here rather than in immutable config.
/// Thread-safe: read via <see cref="Snapshot"/>, written via <see cref="Update"/> from POST /settings.
/// </summary>
public sealed class DesktopLlmOptions(string apiKey, string model)
{
    private readonly Lock _gate = new();
    private string _apiKey = apiKey;
    private string _model = string.IsNullOrWhiteSpace(model) ? "gpt-4o" : model;

    public bool HasKey
    {
        get { lock (_gate) return !string.IsNullOrWhiteSpace(_apiKey); }
    }

    public (string ApiKey, string Model) Snapshot()
    {
        lock (_gate) return (_apiKey, _model);
    }

    public void Update(string apiKey, string model)
    {
        lock (_gate)
        {
            _apiKey = apiKey ?? "";
            _model = string.IsNullOrWhiteSpace(model) ? "gpt-4o" : model.Trim();
        }
    }
}

/// <summary>
/// <see cref="IChatClient"/> that (re)builds the underlying OpenAI client lazily from the current
/// <see cref="DesktopLlmOptions"/>. Lets a freshly-pasted key take effect without restarting the app,
/// and throws a friendly error (not a raw 401) when no key is set yet. Same transport resilience as
/// the hosted Azure client: bounded per-request timeout + transport retries (never the tool loop).
/// </summary>
public sealed class ReloadableOpenAIChatClient : IChatClient
{
    private readonly DesktopLlmOptions _options;
    private readonly OpenAIClientOptions _clientOptions;
    private readonly Lock _gate = new();
    private IChatClient? _inner;
    private string _key = "";
    private string _model = "";

    public ReloadableOpenAIChatClient(DesktopLlmOptions options, TimeSpan timeout, int maxRetries)
    {
        _options = options;
        _clientOptions = new OpenAIClientOptions
        {
            NetworkTimeout = timeout,
            RetryPolicy = new ClientRetryPolicy(maxRetries)
        };
    }

    private IChatClient Inner()
    {
        var (key, model) = _options.Snapshot();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                "No OpenAI API key configured. Open Settings and paste your key to begin.");

        lock (_gate)
        {
            if (_inner is null || key != _key || model != _model)
            {
                _inner?.Dispose();
                _inner = new OpenAIClient(new ApiKeyCredential(key), _clientOptions)
                    .GetChatClient(model)
                    .AsIChatClient();
                _key = key;
                _model = model;
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
