using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

public sealed class ChatService : IDisposable
{
    private readonly AIChatClientWrapper _aiClient;
    private readonly McpClientWrapper _mcpClient;
    private readonly List<ChatMessage> _chatHistory = new();

    public ChatService(AIChatClientWrapper aiClient, McpClientWrapper mcpClient)
    {
        _aiClient = aiClient;
        _mcpClient = mcpClient;
    }

    public void Dispose()
    {
        _aiClient.Dispose();
        _mcpClient.DisposeAsync().GetAwaiter().GetResult();
    }

    public async Task<IEnumerable<AITool>> GetToolsAsync()
    {
        if (_mcpClient == null) throw new InvalidOperationException("Client not initialized");
        return await _mcpClient.GetToolsAsync();
    }

    public async Task StartChatLoopAsync(ChatScenarioConfig config)
    {
        ValidateConfig(config);
        InitializeChatHistory(config.SystemPrompt);
        var userInput = GetUserInput(config);
        AddUserMessage(userInput!);
        var response = await ProcessAIResponseAsync(config);
        if (config.Tool != null && TryParseToolCall(response, out var toolName, out var arguments))
            await HandleToolCallAsync(toolName, arguments, config);
    }

    private async Task<string> ProcessAIResponseAsync(ChatScenarioConfig config)
    {
        var responseBuilder = new StringBuilder();
        await foreach (var text in GetAIResponse(config.Tool))
        {
            config.OutputHandler?.Invoke(text);
            responseBuilder.Append(text);
        }
        config.OutputHandler?.Invoke("\n");
        return responseBuilder.ToString();
    }

    private async IAsyncEnumerable<string> GetAIResponse(AITool tool)
    {
        var requestOptions = new ChatOptions
        {
            MaxOutputTokens = 4096,
            AdditionalProperties = new() { { "max_length", 2048 } },
            Tools = new List<AITool> { tool }
        };

        await foreach (var text in _aiClient.GetStreamingResponseAsync(_chatHistory, requestOptions))
        {
            yield return text;
        }
    }

    private async Task HandleToolCallAsync(string toolName, Dictionary<string, object> arguments, ChatScenarioConfig config)
    {
        try
        {
            config.OutputHandler?.Invoke($"\n[系统] 正在调用工具 {toolName}...");
            await _mcpClient.CallToolAsync(toolName, arguments);
        }
        catch (Exception ex)
        {
            var errorMsg = $"\n[系统]工具调用失败: {ex.Message}";
            config.OutputHandler?.Invoke($"\n[错误] {errorMsg}");
        }
    }

    private bool TryParseToolCall(string response, out string toolName, out Dictionary<string, object> arguments)
    {
        const string pattern = @"^(?:.*<tool_call>)?\s*({[\s\S]*?})\s*(?:</tool_call>.*)?$";
        toolName = string.Empty;
        arguments = new Dictionary<string, object>();
        try
        {
            var match = Regex.Match(response, pattern, RegexOptions.Multiline);
            if (!match.Success) return false;
            var jsonContent = match.Groups[1].Value;
            // 修复示例中的多余闭合括号问题
            jsonContent = FixJsonFormat(jsonContent);
            using JsonDocument doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;
            toolName = root.GetProperty("name").GetString() ?? string.Empty;
            var parameters = root.GetProperty("parameters");
            foreach (var param in parameters.EnumerateObject())
            {
                arguments[param.Name] = param.Value.ValueKind switch
                {
                    JsonValueKind.String => param.Value.GetString(),
                    JsonValueKind.Number => param.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => param.Value.ToString()
                };
            }
            // 处理特殊转义字符
            if (arguments.TryGetValue("fileContent", out var content))
            {
                arguments["fileContent"] = UnescapeContent(content.ToString());
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"解析失败: {ex.Message}");
            return false;
        }
    }

    // 辅助方法：修复JSON格式
    private static string FixJsonFormat(string json)
    {
        // 删除注释
        json = Regex.Replace(json, @"//.*", "");

        // 自动修复多余/缺失的括号
        var openBraces = json.Count(c => c == '{');
        var closeBraces = json.Count(c => c == '}');
        while (openBraces > closeBraces)
        {
            json += "}";
            closeBraces++;
        }

        return json;
    }

    // 辅助方法：处理转义字符
    private static string UnescapeContent(string content)
    {
        return content.Replace("\\n", "\n")
                      .Replace("\\t", "\t")
                      .Replace("\\\"", "\"")
                      .Replace("\\\\", "\\");
    }

    private void AddUserMessage(string userInput)
    {
        _chatHistory.Add(new ChatMessage(ChatRole.User, userInput!));
    }

    private static void ValidateConfig(ChatScenarioConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.SystemPrompt))
            throw new ArgumentException("System prompt cannot be empty");
    }

    private void InitializeChatHistory(string systemPrompt)
    {
        _chatHistory.Clear();
        _chatHistory.Add(new ChatMessage(ChatRole.System, systemPrompt));
    }

    private static string? GetUserInput(ChatScenarioConfig config)
    {
        return config.InputHandler?.Invoke()?.Trim();
    }
}