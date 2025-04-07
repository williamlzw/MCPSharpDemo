using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol;

public class Program
{
    public static async Task Main()
    {
        var config = new AppConfig(
            ModelPath: "d:\\model\\Phi-4-mini-instruct-onnx-int4",
            McpLocation: "d:\\MCPServer\\bin\\Debug\\net9.0\\MCPServer.exe"
        );

        var (mcpClient, aiClient) = await InitializeClientsAsync(config);
        var chatService = new ChatService(aiClient, mcpClient);
        await RunInteractiveLoopAsync(chatService);
    }

    private static async Task<(McpClientWrapper, AIChatClientWrapper)> InitializeClientsAsync(AppConfig config)
    {
        var serviceConfig = new McpServerConfig
        {
            Id = "MCP Server",
            Name = "MCP Server",
            TransportType = TransportTypes.StdIo,
            Location = config.McpLocation
        };
        var clientOptions = new McpClientOptions
        {
            ClientInfo = new() { Name = "MCP Client", Version = "1.0.0" }
        };
        var mcpClient = new McpClientWrapper(
            serviceConfig,
            clientOptions
        );

        var aiClient = new AIChatClientWrapper(config.ModelPath);
        await mcpClient.InitializeAsync();
        return (mcpClient, aiClient);
    }

    private static async Task RunInteractiveLoopAsync(ChatService service)
    {
        // 获取所有工具
        var allTools = await service.GetToolsAsync();
        // 创建场景配置
        var scenarios = ScenarioFactory.CreateScenarios(allTools);
        // 运行场景选择
        while (true)
        {
            Console.WriteLine("\n请选择模式：");
            foreach (var key in scenarios.Keys)
            {
                Console.WriteLine($"{Array.IndexOf(scenarios.Keys.ToArray(), key) + 1}. {key}");
            }
            Console.WriteLine("exit 退出程序");

            var input = Console.ReadLine();
            if (IsExitCommand(input)) break;

            if (int.TryParse(input, out var index) && index > 0 && index <= scenarios.Count)
            {
                var selectedKey = scenarios.Keys.ElementAt(index - 1);
                await service.StartChatLoopAsync(scenarios[selectedKey]);
            }
        }
        service.Dispose();
    }

    private static bool IsExitCommand(string? input)
    {
        if (input?.Equals("exit", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }
        return false;
    }
}

public record AppConfig(string ModelPath, string McpLocation);