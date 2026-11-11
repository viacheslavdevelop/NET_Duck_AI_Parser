using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DuckAi;

namespace DuckAi.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "install", StringComparison.OrdinalIgnoreCase))
        {
            return InstallBrowser();
        }

        CliArguments parsed;
        try
        {
            parsed = CliArguments.Parse(args);
        }
        catch (ArgumentException ex)
        {
            if (args.Contains("--json", StringComparer.Ordinal))
            {
                return Fail(true, ex.Message, "invalid-arguments", 2);
            }

            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            PrintUsage(Console.Error);
            return 2;
        }

        if (parsed.ShowHelp)
        {
            PrintUsage(Console.Out);
            return 0;
        }

        CliRequest? jsonRequest = null;
        if (parsed.ReadStdinJson)
        {
            try
            {
                var json = await Console.In.ReadToEndAsync();
                jsonRequest = JsonSerializer.Deserialize<CliRequest>(json, JsonOptions)
                              ?? throw new ArgumentException("Пустой JSON во входном потоке.");
            }
            catch (JsonException ex)
            {
                return Fail(parsed.JsonOutput, $"Некорректный JSON: {ex.Message}", "invalid-json", 2);
            }
            catch (ArgumentException ex)
            {
                return Fail(parsed.JsonOutput, ex.Message, "invalid-json", 2);
            }
        }

        var prompt = parsed.Prompt;
        if (string.IsNullOrWhiteSpace(prompt) && parsed.PromptFile is not null)
        {
            try
            {
                prompt = await File.ReadAllTextAsync(parsed.PromptFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Fail(parsed.JsonOutput, $"Не удалось прочитать файл промпта: {ex.Message}", "prompt-file", 2);
            }
        }

        prompt ??= jsonRequest?.Prompt;

        if (string.IsNullOrWhiteSpace(prompt))
        {
            const string message =
                "Не задан промпт. Укажи его позиционным аргументом, через --prompt, --prompt-file или --stdin-json.";

            if (parsed.JsonOutput)
            {
                return Fail(true, message, "missing-prompt", 2);
            }

            Console.Error.WriteLine(message);
            Console.Error.WriteLine();
            PrintUsage(Console.Error);
            return 2;
        }

        var defaults = new DuckAiOptions();
        var timeoutSeconds = parsed.TimeoutSeconds ?? jsonRequest?.TimeoutSeconds;

        if (timeoutSeconds is <= 0)
        {
            return Fail(parsed.JsonOutput, "timeout/timeoutSeconds должен быть больше нуля.", "invalid-timeout", 2);
        }

        var options = new DuckAiOptions
        {
            Headless = parsed.Headless ?? jsonRequest?.Headless ?? defaults.Headless,
            ProfileDir = parsed.ProfileDir ?? jsonRequest?.ProfileDir ?? defaults.ProfileDir,
            TargetUrl = parsed.TargetUrl ?? jsonRequest?.TargetUrl ?? defaults.TargetUrl,
            Timeout = timeoutSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : defaults.Timeout,
            EnableLogging = parsed.EnableLogging,
            Log = Console.Error.WriteLine
        };

        try
        {
            await using var client = new DuckAiClient(options);
            var result = await client.AskAsync(new DuckAiRequest { Prompt = prompt });

            if (parsed.JsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(new CliSuccess
                {
                    Ok = true,
                    Text = result.Text,
                    ElapsedMs = (long)result.Elapsed.TotalMilliseconds
                }, JsonOptions));
            }
            else
            {
                Console.WriteLine(result.Text);
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            return Fail(parsed.JsonOutput, "Запрос отменён.", "canceled", 130);
        }
        catch (DuckAiTimeoutException ex)
        {
            return Fail(parsed.JsonOutput, ex.Message, "timeout", 1, ex.PartialText);
        }
        catch (DuckAiException ex)
        {
            var details = ex.InnerException is null
                ? null
                : $"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
            return Fail(parsed.JsonOutput, ex.Message, "duckai", 1, details: details);
        }
        catch (Exception ex)
        {
            return Fail(parsed.JsonOutput, ex.ToString(), ex.GetType().Name, 1);
        }
    }

    private static int InstallBrowser()
    {
        try
        {
            Console.Error.WriteLine("[*] Устанавливаю Chromium для Playwright...");
            var playwright = new Microsoft.Playwright.Program();
            var exitCode = playwright.Run(new[] { "install", "chromium" });
            Console.Error.WriteLine(exitCode == 0 ? "[*] Готово." : $"[*] Установка завершилась с кодом {exitCode}.");
            return exitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Не удалось установить браузер: {ex.Message}");
            return 1;
        }
    }

    private static int Fail(
        bool jsonOutput,
        string message,
        string? errorType,
        int exitCode,
        string? partialText = null,
        string? details = null)
    {
        if (jsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(new CliError
            {
                Ok = false,
                Error = message,
                ErrorType = errorType,
                Details = details,
                PartialText = partialText
            }, JsonOptions));
        }
        else
        {
            Console.Error.WriteLine($"Ошибка: {message}");

            if (!string.IsNullOrWhiteSpace(details))
            {
                Console.Error.WriteLine($"Детали: {details}");
            }

            if (!string.IsNullOrWhiteSpace(partialText))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Частичный ответ:");
                Console.Error.WriteLine(partialText);
            }
        }

        return exitCode;
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("""
                          duckai — клиент Duck.ai

                          Использование:
                            duckai "текст промпта" [параметры]
                            duckai --prompt-file prompt.txt [параметры]
                            echo '{"prompt":"..."}' | duckai --stdin-json [параметры]
                            duckai install

                          Параметры:
                            -p, --prompt <text>       Текст промпта
                                --prompt-file <path>  Прочитать промпт из файла
                                --stdin-json          Прочитать JSON-запрос из stdin
                                --timeout <sec>       Таймаут в секундах (по умолчанию 120)
                                --no-headless         Показывать окно браузера
                                --profile-dir <path>  Каталог профиля браузера
                                --url <url>           URL чата (по умолчанию https://duck.ai/chat)
                                --json                Вывести ответ в JSON
                                --log / --no-log      Включить/выключить логи (по умолчанию включены)
                            -h, --help                Справка

                          JSON-запрос:
                            {"prompt":"...","timeoutSeconds":90,"headless":false,
                             "profileDir":"./profile","targetUrl":"https://duck.ai/chat"}

                          JSON-ответ:
                            {"ok":true,"text":"...","elapsedMs":3210}
                            {"ok":false,"error":"...","errorType":"timeout"}
                          """);
    }
}

internal sealed class CliArguments
{
    public string? Prompt { get; private set; }
    public string? PromptFile { get; private set; }
    public bool ReadStdinJson { get; private set; }
    public double? TimeoutSeconds { get; private set; }
    public bool? Headless { get; private set; }
    public string? ProfileDir { get; private set; }
    public string? TargetUrl { get; private set; }
    public bool EnableLogging { get; private set; } = true;
    public bool JsonOutput { get; private set; }
    public bool ShowHelp { get; private set; }

    public static CliArguments Parse(string[] args)
    {
        var result = new CliArguments();
        var positional = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--")
            {
                positional.AddRange(args.Skip(i + 1));
                break;
            }

            switch (arg)
            {
                case "-h":
                case "--help":
                    result.ShowHelp = true;
                    break;

                case "-p":
                case "--prompt":
                    result.Prompt = RequireValue(args, ref i, arg);
                    break;

                case "--prompt-file":
                    result.PromptFile = RequireValue(args, ref i, arg);
                    break;

                case "--stdin-json":
                    result.ReadStdinJson = true;
                    break;

                case "--timeout":
                {
                    var raw = RequireValue(args, ref i, arg);
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
                        seconds <= 0)
                    {
                        throw new ArgumentException($"Некорректное значение --timeout: {raw}");
                    }

                    result.TimeoutSeconds = seconds;
                    break;
                }

                case "--headless":
                    result.Headless = true;
                    break;

                case "--no-headless":
                case "--headed":
                    result.Headless = false;
                    break;

                case "--profile-dir":
                    result.ProfileDir = RequireValue(args, ref i, arg);
                    break;

                case "--url":
                    result.TargetUrl = RequireValue(args, ref i, arg);
                    break;

                case "--json":
                    result.JsonOutput = true;
                    break;

                case "--log":
                    result.EnableLogging = true;
                    break;

                case "--no-log":
                    result.EnableLogging = false;
                    break;

                default:
                    if (arg.StartsWith('-'))
                    {
                        throw new ArgumentException($"Неизвестный аргумент: {arg}");
                    }

                    positional.Add(arg);
                    break;
            }
        }

        if (result.Prompt is null && positional.Count > 0)
        {
            result.Prompt = string.Join(' ', positional);
        }

        return result;
    }

    private static string RequireValue(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Аргумент {name} требует значения.");
        }

        return args[++index];
    }
}

internal sealed class CliRequest
{
    public string? Prompt { get; init; }
    public double? TimeoutSeconds { get; init; }
    public bool? Headless { get; init; }
    public string? ProfileDir { get; init; }
    public string? TargetUrl { get; init; }
}

internal sealed class CliSuccess
{
    public bool Ok { get; init; }
    public string Text { get; init; } = "";
    public long ElapsedMs { get; init; }
}

internal sealed class CliError
{
    public bool Ok { get; init; }
    public string Error { get; init; } = "";
    public string? ErrorType { get; init; }
    public string? Details { get; init; }
    public string? PartialText { get; init; }
}
