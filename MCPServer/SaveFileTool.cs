using ModelContextProtocol.Server;
using System.ComponentModel;

namespace MCPServer
{
    [McpServerToolType]
    public class SaveFileTool
    {
        [McpServerTool(Name = "SaveFile"), Description("本方法作用是将文本内容写到本地计算机目标路径")]
        public static void SaveFile([Description("文件保存路径（例如：d:/test.txt）")] string filePath, [Description("需要保存的文本内容")] string fileContent)
        {
            System.IO.File.WriteAllText(filePath, fileContent);
        }
    }
}
