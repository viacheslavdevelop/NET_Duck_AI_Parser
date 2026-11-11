using System.Diagnostics;
using Microsoft.Playwright;

namespace DuckAiAutomation;

public class DuckAiClient
{
    private readonly DuckAiOptions _options;

    private const string CombinedAcceptSelector =
        "button:has-text('Принять'), button:has-text('Согласен'), button:has-text('Продолжить'), " +
        "button:has-text('Начать'), button:has-text('Accept'), button:has-text('I Agree'), " +
        "button:has-text('Agree'), button:has-text('Continue'), button:has-text('Get Started')";

    private const string AnswerSelector = "div.space-y-4.whitespace-normal";

    public DuckAiClient(DuckAiOptions? options = null)
    {
        _options = options ?? new DuckAiOptions();
    }

    private void Log(string message) => _options.LogAction?.Invoke(message);

    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        using var playwright = await Playwright.CreateAsync();

        var args = new List<string>
        {
            "--disable-blink-features=AutomationControlled",
            "--disable-background-timer-throttling",
            "--disable-backgrounding-occluded-windows",
            "--disable-renderer-backgrounding"
        };

        if (_options.Headless)
        {
            args.Add("--headless=new");
        }

        await using var context = await playwright.Chromium.LaunchPersistentContextAsync(
            _options.ProfileDir,
            new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = false,
                Args = args,
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
                Locale = "ru-RU"
            });

        await context.AddInitScriptAsync("Object.defineProperty(navigator, 'webdriver', {get: () => undefined})");

        var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();

        Log("[*] Открываем Duck.ai...");
        await page.GotoAsync(_options.TargetUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForTimeoutAsync(1500);

        await ClickModalsIfPresentAsync(page);

        int initialAnswersCount = await page.Locator(AnswerSelector).CountAsync();

        var textarea = page.Locator("textarea[name='user-prompt'], textarea").First;
        await textarea.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        await textarea.FillAsync(prompt);
        await page.WaitForTimeoutAsync(300);
        await textarea.PressAsync("Enter");
        Log("[*] Промпт отправлен.");

        await page.WaitForTimeoutAsync(800);
        if (await ClickModalsIfPresentAsync(page))
        {
            Log("[*] Условия использования приняты.");
            if (await textarea.IsVisibleAsync() && (await textarea.InputValueAsync()).Trim() == prompt.Trim())
            {
                await textarea.PressAsync("Enter");
            }
        }

        Log("[*] Ожидание генерации ответа...");
        string answer = await WaitForStreamedAnswerAsync(page, initialAnswersCount, _options.TimeoutSeconds, cancellationToken);

        await context.CloseAsync();
        return answer;
    }

    private static async Task<bool> ClickModalsIfPresentAsync(IPage page)
    {
        bool clickedAny = false;
        var button = page.Locator(CombinedAcceptSelector).First;

        for (int i = 0; i < 3; i++)
        {
            if (!await button.IsVisibleAsync())
                break;

            try
            {
                await button.ClickAsync(new LocatorClickOptions { Timeout = 1000 });
                await page.WaitForTimeoutAsync(300);
                clickedAny = true;
            }
            catch
            {
                break;
            }
        }

        return clickedAny;
    }

    private async Task<string> WaitForStreamedAnswerAsync(
        IPage page,
        int prevCount,
        int timeoutSec,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var answerBlocks = page.Locator(AnswerSelector);
        bool started = false;

        while (stopwatch.Elapsed.TotalSeconds < timeoutSec && !cancellationToken.IsCancellationRequested)
        {
            if (await answerBlocks.CountAsync() > prevCount)
            {
                started = true;
                break;
            }
            await page.WaitForTimeoutAsync(250);
        }

        if (!started)
        {
            return "Не удалось дождаться начала генерации ответа (таймаут).";
        }

        var targetBlock = answerBlocks.Last;
        string lastText = "";
        int stableTicks = 0;
        const int requiredStableTicks = 4;

        while (stopwatch.Elapsed.TotalSeconds < timeoutSec && !cancellationToken.IsCancellationRequested)
        {
            string currentText;
            try
            {
                currentText = (await targetBlock.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 500 })).Trim();
            }
            catch
            {
                currentText = lastText;
            }

            if (!string.IsNullOrEmpty(currentText) && currentText == lastText)
            {
                stableTicks++;
                if (stableTicks >= requiredStableTicks)
                {
                    return currentText;
                }
            }
            else
            {
                stableTicks = 0;
                lastText = currentText;
            }

            await page.WaitForTimeoutAsync(300);
        }

        return !string.IsNullOrEmpty(lastText)
            ? lastText
            : "Превышено время ожидания полного ответа.";
    }
}
