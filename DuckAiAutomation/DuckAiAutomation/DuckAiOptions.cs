namespace DuckAiAutomation;

public class DuckAiOptions
{
    public string ProfileDir { get; set; } = "./duck_profile";
    public string TargetUrl { get; set; } = "https://duck.ai/chat";
    public bool Headless { get; set; } = false;
    public int TimeoutSeconds { get; set; } = 120;
    public Action<string>? LogAction { get; set; }
}