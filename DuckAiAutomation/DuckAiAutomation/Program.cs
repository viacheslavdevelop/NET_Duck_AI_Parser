using DuckAiAutomation;

namespace MyApp;

internal class Program
{
    public static async Task Main(string[] args)
    {
        var options = new DuckAiOptions
        {
            Headless = true,
            ProfileDir = "./duck_custom_profile",
            TimeoutSeconds = 90,
            LogAction = message => Console.WriteLine(message)
        };

        var client = new DuckAiClient(options);

        string prompt = "Объясни в трех предложениях, как устроена архитектура Transformer";

        string response = await client.AskAsync(prompt);

        Console.WriteLine("\nОТВЕТ ОТ DUCK.AI");
        Console.WriteLine(response);
    }
}