using System.Net.Http;

namespace NdoMcp.Server.Services;

/// <summary>
/// Lightweight wrapper for a typed <see cref="HttpClient"/> used to call the Gemma LLM API.
///
/// Presently the class exposes the underlying HttpClient for use by services; keeping a dedicated
/// type makes future DI configuration and typed client settings easier to apply.
/// </summary>
public class GemmaClient
{
    private readonly HttpClient _client;

    /// <summary>
/// Create a new GemmaClient that uses the provided HTTP client instance.
/// </summary>
/// <param name="client">The injected <see cref="HttpClient"/>.</param>
public GemmaClient(HttpClient client)
    {
        _client = client;
    }

    // This class exists to allow typed HttpClient wiring if needed in future.
    /// <summary>
/// Expose the underlying <see cref="HttpClient"/> instance for API calls.
/// </summary>
public HttpClient Client => _client;
}
