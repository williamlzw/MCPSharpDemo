using ModelContextProtocol.Client;
using ModelContextProtocol;
using Microsoft.ML.OnnxRuntimeGenAI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol.Types;


public sealed class McpClientWrapper : IAsyncDisposable
{
    private IMcpClient? _mcpClient;

    public McpClientWrapper(McpServerConfig serverConfig, McpClientOptions options)
    {
        ServerConfig = serverConfig;
        ClientOptions = options;
    }

    public McpServerConfig ServerConfig { get; }
    public McpClientOptions ClientOptions { get; }

    public async Task InitializeAsync() =>
        _mcpClient = await McpClientFactory.CreateAsync(ServerConfig, ClientOptions);

    public async Task<IEnumerable<AITool>> GetToolsAsync() =>
        await GetClient().ListToolsAsync();

    public async Task<CallToolResponse> CallToolAsync(string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken ct = default) =>
        await GetClient().CallToolAsync(toolName, arguments, ct);

    public async ValueTask DisposeAsync()
    {
        if (_mcpClient != null)
            await _mcpClient.DisposeAsync();
    }

    private IMcpClient GetClient() =>
        _mcpClient ?? throw new InvalidOperationException("Client not initialized");
}

public sealed class AIChatClientWrapper : IDisposable
{
    private readonly string _modelPath;
    private readonly Model _model;
    private readonly Tokenizer _tokenizer;
    private readonly Config _config;
    private readonly OgaHandle _ogaHandle;
    private readonly OnnxRuntimeGenAIChatClient _chatClient;

    public AIChatClientWrapper(string modelPath)
    {
        _modelPath = modelPath;
        _ogaHandle = new OgaHandle();
        _config = CreateModelConfig();
        _model = new Model(_config);
        _tokenizer = new Tokenizer(_model);
        _chatClient = CreateChatClient();
    }

    public async IAsyncEnumerable<string> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatHistory,
        ChatOptions requestOptions)
    {
        if (_chatClient == null) throw new InvalidOperationException("Client not initialized");

        var completion = _chatClient.GetStreamingResponseAsync(
            chatHistory,
            requestOptions);

        await foreach (var message in completion)
        {
            yield return message.Text;
        }
    }

    public void Dispose()
    {
        _chatClient.Dispose();
        _model.Dispose();
        _tokenizer.Dispose();
        _config.Dispose();
        _ogaHandle.Dispose();
    }

    private Config CreateModelConfig()
    {
        var config = new Config(_modelPath);
        config.ClearProviders();
        config.AppendProvider("cuda");
        return config;
    }

    private OnnxRuntimeGenAIChatClient CreateChatClient() =>
        new (_model, options:OnnxRuntimeGenAIChatClientOptionsGenerator.GetPhi4());
}