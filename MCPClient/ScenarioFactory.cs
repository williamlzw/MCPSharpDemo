using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text;

// 场景配置类
public class ChatScenarioConfig
{
    public required string SystemPrompt { get; set; }
    public AITool Tool { get; set; }
    public Action<string>? OutputHandler { get; set; }
    public Func<string?>? InputHandler { get; set; }
}

public static class ScenarioFactory
{
    public static Dictionary<string, ChatScenarioConfig> CreateScenarios(IEnumerable<AITool> tools)
    {
        var weatherTool = GetToolByName(tools, "Weather");
        var saveFileTool = GetToolByName(tools, "SaveFile");

        return new Dictionary<string, ChatScenarioConfig>
        {
            ["常规对话"] = CreateDefaultConversation(),
            ["天气助手"] = CreateWeatherAssistant(weatherTool),
            ["编码助手"] = CreateCodingAssistant(saveFileTool)
        };
    }

    private static ChatScenarioConfig CreateDefaultConversation() => new()
    {
        SystemPrompt = "你是一个助手，用中文回答用户的问题",
        Tool = null,
        OutputHandler = Console.Write,
        InputHandler = Console.ReadLine
    };

    private static ChatScenarioConfig CreateWeatherAssistant(AITool? tool) => new()
    {
        SystemPrompt = BuildWeatherSystemPrompt(tool),
        Tool = tool,
        OutputHandler = Console.Write,
        InputHandler = Console.ReadLine
    };

    private static ChatScenarioConfig CreateCodingAssistant(AITool? tool) => new()
    {
        SystemPrompt = BuildCodingSystemPrompt(tool),
        Tool = tool,
        OutputHandler = Console.Write,
        InputHandler = Console.ReadLine
    };

    private static string BuildWeatherSystemPrompt(AITool? tool)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("你是一个专业的天气分析助手，可以使用以下工具获取实时天气数据：");

        if (tool != null)
        {
            prompt.AppendLine(FormatToolInfo(tool));
        }

        prompt.AppendLine("### 使用规范");
        prompt.AppendLine("1. 当用户询问天气时，必须调用天气查询工具");
        prompt.AppendLine("2. 严格按照工具参数要求获取城市名称");
        return prompt.ToString();
    }

    private static string BuildCodingSystemPrompt(AITool? tool)
    {
        return $$"""
            <|system|>
            你是一个编码助手。
            ## 操作规范
            ### 响应格式
            必须严格遵循以下结构：
            ```
            [回答内容]
            <tool_call>
            {
                "name": "SaveFile",
                "parameters": {
                    "filePath": "d:/test.txt",
                    "fileContent": "[转义后的内容]"
                }
            }
            </tool_call>
            ```

            {{(tool != null ? FormatToolInfo(tool) : "")}}
            
            ### 强制规则
            1. 路径固定：必须使用 `"filePath": "d:/test.txt"`
            2. 内容规范：
               - 必须将完整回答内容放入 `fileContent`
               - 特殊字符需要转义
            3. 调用限制：
               - 每个响应只能包含一个工具调用
               - 必须位于响应末尾
            <|end|>
            """;
    }

    private static AITool? GetToolByName(IEnumerable<AITool> tools, string name) =>
        tools.FirstOrDefault(t => t.Name == name);

    private static string FormatToolInfo(AITool tool)
    {
        var parameters = GetToolParameters(tool.Name);
        var sb = new StringBuilder();

        sb.AppendLine("<|tool|>");
        sb.AppendLine(JsonSerializer.Serialize(new[]
        {
            new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = parameters.ToDictionary(
                    p => p.Name,
                    p => new { description = p.Description, type = MapType(p.Type) })
            }
        }, new JsonSerializerOptions { WriteIndented = true }));
        sb.AppendLine("<|/tool|>");

        return sb.ToString();
    }

    private static string MapType(string type) => type.ToLower() switch
    {
        "string" => "str",
        "int" => "int",
        "bool" => "bool",
        _ => "object"
    };

    private static List<ToolParameterInfo> GetToolParameters(string toolName) => toolName switch
    {
        "SaveFile" => new List<ToolParameterInfo>
        {
            new() { Name = "filePath", Type = "string", Description = "文件保存路径" },
            new() { Name = "fileContent", Type = "string", Description = "文件内容" }
        },
        "Weather" => new List<ToolParameterInfo>
        {
            new() { Name = "location", Type = "string", Description = "城市名称" }
        },
        _ => new List<ToolParameterInfo>()
    };

    private class ToolParameterInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "string";
        public string Description { get; set; } = "";
    }
}
