using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol.Types;

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
        bool doWhile = true;
        bool doTool = false;
        while (doWhile)
        {
            var response = await ProcessAIResponseAsync(config);
            if (doTool)
            {
                Console.WriteLine($"\n[工具已经调用过，退出]");
                break;
            }
            Console.WriteLine($"[系统]:{response}");
            if (config.Tool != null && TryParseToolCall(response, out var toolName, out var arguments))
            {
                var ret = await HandleToolCallAsync(toolName, arguments, config);
                if (ret.IsError == false)
                {
                    if (ret.Content.Count > 0)
                    {
                        Console.WriteLine($"\n[工具返回内容]： {ret.Content[0].Text}");
                        AddAssistantMessage(ret.Content[0].Text);
                        doTool = true;
                    }
                    else
                    {
                        Console.WriteLine($"\n[工具不返回内容]");
                        doWhile = false;
                    }
                }
                else
                {
                    Console.WriteLine($"\n[工具调用失败，退出]");
                    break;
                }
            }
            else
            {
                Console.WriteLine($"\n[无工具调用，退出]");
                break;
            }
        }
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

    private Task<CallToolResponse> HandleToolCallAsync(string toolName, Dictionary<string, object> arguments, ChatScenarioConfig config)
    {
        config.OutputHandler?.Invoke($"\n[系统] 正在调用工具 {toolName}...");
        return _mcpClient.CallToolAsync(toolName, arguments);
    }

    private bool TryParseToolCall(string response, out string toolName, out Dictionary<string, object> arguments)
    {
        Console.WriteLine($"\n[系统] 解析工具调用");
        const string pattern = @"<tool_call>(?>(?!</?tool_call>).)*</tool_call>";
        toolName = string.Empty;
        arguments = new Dictionary<string, object>();
        try
        {
            var match = Regex.Match(response, pattern, RegexOptions.Singleline);
            if (!match.Success) return false;
            var jsonContent = match.Value;
            string pattern2 = @"<tool_call>([\s\S]*?)</tool_call>";
            match = Regex.Match(jsonContent, pattern2, RegexOptions.Singleline);
            if (!match.Success) return false;
            jsonContent = match.Groups[1].Value;
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

    private void AddAssistantMessage(string input)
    {
        _chatHistory.Add(new ChatMessage(ChatRole.Assistant, input!));
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