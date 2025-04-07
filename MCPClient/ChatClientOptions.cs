using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntimeGenAI;

public static class OnnxRuntimeGenAIChatClientOptionsGenerator
{
    public static OnnxRuntimeGenAIChatClientOptions GetPhi4() => new()
    {
        StopSequences = ["<|system|>", "<|user|>", "<|assistant|>", "<|end|>"],
        PromptFormatter = static (messages, _) =>
        {
            var prompt = new StringBuilder();
            foreach (var message in messages)
                foreach (var content in message.Contents.OfType<TextContent>())
                    prompt.Append($"<|{message.Role.Value}|>\n{content.Text}<|end|>\n");
            return prompt.Append("<|assistant|>\n").ToString();
        }
    };
}
