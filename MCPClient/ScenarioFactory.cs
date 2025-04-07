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
        var saveFileTool = GetToolByName(tools, "SaveFile");

        return new Dictionary<string, ChatScenarioConfig>
        {
            ["常规对话"] = CreateDefaultConversation(),
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

    private static ChatScenarioConfig CreateCodingAssistant(AITool? tool) => new()
    {
        SystemPrompt = BuildCodingSystemPrompt(tool),
        Tool = tool,
        OutputHandler = Console.Write,
        InputHandler = Console.ReadLine
    };

    private static string BuildCodingSystemPrompt(AITool? tool)
    {
        return $$"""
            你是一个编码助手。用中文回答用户问题。你回答完用户问题后需要加入tool_call标签调用工具保存回答内容。
            内容包含<tool_call>和</tool_call>字样才能调用工具。
            工具标签必须严格遵循以下结构：
            '''
            <tool_call>
            {
                "name": "SaveFile",
                "parameters": {
                    "filePath": "d:/test.txt",
                    "fileContent": "[回答的内容]"
                }
            }
            </tool_call>
            '''
            ### 强制规则
            1. 路径固定: "filePath"固定为"d:/test.txt"
            2. 内容规范：
               - 必须将完整回答内容放入 "fileContent",特殊字符需要转义,确保tool_call标签内容是正确json格式。
            3. 调用限制：
               - 每个响应只能包含一个工具调用
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
        _ => new List<ToolParameterInfo>()
    };

    private class ToolParameterInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "string";
        public string Description { get; set; } = "";
    }
}
