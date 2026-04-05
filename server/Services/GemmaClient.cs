using System.Net.Http;

namespace NdoMcp.Server.Services;

public class GemmaClient
{
    private readonly HttpClient _client;

    public GemmaClient(HttpClient client)
    {
        _client = client;
    }

    // This class exists to allow typed HttpClient wiring if needed in future.
    public HttpClient Client => _client;
}
