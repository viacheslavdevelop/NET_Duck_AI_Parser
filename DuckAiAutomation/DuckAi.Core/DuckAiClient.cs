using System.Diagnostics;
using Microsoft.Playwright;

namespace DuckAi;

/// <summary>
/// Клиент Duck.ai на базе Playwright. Один экземпляр переиспользует браузер
/// и выполняет запросы последовательно, так как постоянный профиль нельзя
/// открыть дважды одновременно.
/// </summary>
public sealed class DuckAiClient : IDuckAiClient
{
    private const string CombinedAcceptSelector =
        "button:has-text('Принять'), button:has-text('Согласен'), button:has-text('Продолжить'), " +
        "button:has-text('Начать'), button:has-text('Accept'), button:has-text('I Agree'), " +
        "button:has-text('Agree'), button:has-text('Continue'), button:has-text('Get Started')";

    private const string AnswerSelector = "div.space-y-4.whitespace-normal";

    private readonly DuckAiOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _profileDir;

    private IPlaywright? _playwright;
    private IBrowserContext? _context;
    private bool _disposed;

    /// <summary>Создаёт клиент с указанными настройками.</summary>
    /// <param name="options">Настройки клиента. Если не заданы, используются значения по умолчанию.</param>
    public DuckAiClient(DuckAiOptions? options = null)
    {
        _options = options ?? new DuckAiOptions();

        if (string.IsNullOrWhiteSpace(_options.TargetUrl))
        {
            throw new ArgumentException("TargetUrl не может быть пустым.", nameof(options));
        }

        if (_options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Timeout должен быть больше нуля.");
        }

        if (string.IsNullOrWhiteSpace(_options.ProfileDir))
        {
            throw new ArgumentException("ProfileDir не может быть пустым.", nameof(options));
        }

        _profileDir = Path.GetFullPath(_options.ProfileDir);
        Directory.CreateDirectory(_profileDir);
    }

    /// <inheritdoc />
    public Task<DuckAiResponse> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        return AskAsync(new DuckAiRequest { Prompt = prompt }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DuckAiResponse> AskAsync(DuckAiRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var timeout = request.Timeout ?? _options.Timeout;
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Timeout должен быть больше нуля.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var stopwatch = Stopwatch.StartNew();
            var text = await AskCoreAsync(request.Prompt, timeout, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            return new DuckAiResponse
            {
                Text = text,
                Elapsed = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DuckAiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DuckAiException("Не удалось получить ответ от Duck.ai.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> AskCoreAsync(string prompt, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var page = await CreatePageAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int initialAnswersCount = await page.Locator(AnswerSelector).CountAsync().ConfigureAwait(false);

            var textarea = page.Locator("textarea[name='user-prompt'], textarea").First;
            await textarea.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 15000
            }).ConfigureAwait(false);

            await textarea.FillAsync(prompt).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            await textarea.PressAsync("Enter").ConfigureAwait(false);
            Log("[*] Промпт отправлен.");

            await Task.Delay(800, cancellationToken).ConfigureAwait(false);
            if (await ClickModalsIfPresentAsync(page, cancellationToken).ConfigureAwait(false))
            {
                Log("[*] Условия использования приняты.");
                if (await textarea.IsVisibleAsync().ConfigureAwait(false) &&
                    (await textarea.InputValueAsync().ConfigureAwait(false)).Trim() == prompt.Trim())
                {
                    await textarea.PressAsync("Enter").ConfigureAwait(false);
                }
            }

            Log("[*] Ожидание генерации ответа...");
            return await WaitForStreamedAnswerAsync(page, initialAnswersCount, timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await page.CloseAsync().ConfigureAwait(false);
            }
            catch
            {
                // Страница могла быть уже закрыта при отмене или падении браузера.
            }
        }
    }

    private async Task<IPage> CreatePageAsync(CancellationToken cancellationToken)
    {
        var context = await EnsureContextAsync(cancellationToken).ConfigureAwait(false);
        var page = await context.NewPageAsync().ConfigureAwait(false);

        try
        {
            Log("[*] Открываем Duck.ai...");
            await page.GotoAsync(_options.TargetUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            }).ConfigureAwait(false);

            await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
            await ClickModalsIfPresentAsync(page, cancellationToken).ConfigureAwait(false);
            return page;
        }
        catch
        {
            try
            {
                await page.CloseAsync().ConfigureAwait(false);
            }
            catch
            {
                // Игнорируем ошибку закрытия, исходное исключение важнее.
            }

            throw;
        }
    }

    private async Task<IBrowserContext> EnsureContextAsync(CancellationToken cancellationToken)
    {
        if (_context is not null)
        {
            return _context;
        }

        cancellationToken.ThrowIfCancellationRequested();

        Log("[*] Запускаем браузер...");
        _playwright ??= await Playwright.CreateAsync().ConfigureAwait(false);

        var args = new List<string>
        {
            "--disable-blink-features=AutomationControlled",
            "--disable-background-timer-throttling",
            "--disable-backgrounding-occluded-windows",
            "--disable-renderer-backgrounding"
        };

        _context = await _playwright.Chromium.LaunchPersistentContextAsync(
            _profileDir,
            new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = _options.Headless,
                // "chromium" включает новый headless-режим полноценного Chromium.
                // Без него Playwright использует headless shell, который Duck.ai часто не принимает.
                Channel = _options.Headless && _options.UseNewHeadless ? "chromium" : null,
                Args = args,
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
                Locale = "ru-RU"
            }).ConfigureAwait(false);

        await _context.AddInitScriptAsync(
            "Object.defineProperty(navigator, 'webdriver', {get: () => undefined})").ConfigureAwait(false);

        return _context;
    }

    private async Task<bool> ClickModalsIfPresentAsync(IPage page, CancellationToken cancellationToken)
    {
        bool clickedAny = false;
        var button = page.Locator(CombinedAcceptSelector).First;

        for (int i = 0; i < 3; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await button.IsVisibleAsync().ConfigureAwait(false))
            {
                break;
            }

            try
            {
                await button.ClickAsync(new LocatorClickOptions { Timeout = 1000 }).ConfigureAwait(false);
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                clickedAny = true;
            }
            catch (PlaywrightException)
            {
                break;
            }
        }

        return clickedAny;
    }

    private async Task<string> WaitForStreamedAnswerAsync(
        IPage page,
        int prevCount,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var answerBlocks = page.Locator(AnswerSelector);
        bool started = false;

        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await answerBlocks.CountAsync().ConfigureAwait(false) > prevCount)
            {
                started = true;
                break;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        if (!started)
        {
            throw new DuckAiTimeoutException(
                $"Duck.ai не начал генерировать ответ за {timeout.TotalSeconds:0.#} с.");
        }

        var targetBlock = answerBlocks.Last;
        string lastText = "";
        int stableTicks = 0;
        const int requiredStableTicks = 4;

        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string currentText;
            try
            {
                currentText = (await targetBlock.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 500 })
                    .ConfigureAwait(false)).Trim();
            }
            catch (PlaywrightException)
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

            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        throw new DuckAiTimeoutException(
            $"Duck.ai не закончил ответ за {timeout.TotalSeconds:0.#} с.",
            string.IsNullOrEmpty(lastText) ? null : lastText);
    }

    private void Log(string message)
    {
        if (!_options.EnableLogging)
        {
            return;
        }

        (_options.Log ?? Console.Error.WriteLine)(message);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_context is not null)
        {
            try
            {
                await _context.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Браузер мог уже завершиться сам.
            }

            _context = null;
        }

        _playwright?.Dispose();
        _playwright = null;
    }
}
