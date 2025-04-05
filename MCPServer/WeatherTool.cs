using ModelContextProtocol.Server;
using System.ComponentModel;

namespace MCPServer
{
    [McpServerToolType]
    public class WeatherTool
    {
        [McpServerTool(Name = "Weather"), Description("获取指定城市的天气，返回temperature温度和weather天气情况组成的json信息。")]
        public static string GetCurrentWeather([Description("城市名称")] string city)
        {
            //随机温度
            var temperature = new Random().Next(-20, 50);
            //天气组
            var weatherList = new string[] { "晴", "多云", "大雨", "小雨", "大雪" };
            //随机天气
            var weather = weatherList[new Random(Guid.NewGuid().GetHashCode()).Next(0, weatherList.Length - 1)];

            //模仿json格式返回
            return "{\"temperature\":" + temperature + ",\"weather\":\"" + weather + "\"}";

        }
    }
}
