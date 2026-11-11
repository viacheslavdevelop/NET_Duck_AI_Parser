# NETDuckAIParser

Библиотека и CLI для получения ответов [Duck.ai](https://duck.ai) через Playwright.
Проект разделён на два пакета:

| Проект | Назначение |
|---|---|
| `DuckAiAutomation/DuckAi.Core` | Библиотека с публичным API `DuckAiClient` |
| `DuckAiAutomation/DuckAi.Cli` | Консольная обёртка `duckai` (флаги + JSON) |

## Требования

- .NET 10 SDK;
- Chromium для Playwright (скачивается отдельно, в NuGet-пакет не входит).

## Установка браузера

Один раз после сборки:

```bash
dotnet run --project DuckAiAutomation/DuckAi.Cli -- install
```

Либо вручную:

```bash
pwsh DuckAiAutomation/DuckAi.Cli/bin/Release/net10.0/playwright.ps1 install chromium
```

## Использование как библиотеки

Подключи проект:

```bash
dotnet add reference ../NETDuckAIParser/DuckAiAutomation/DuckAi.Core/DuckAi.Core.csproj
```

или собери и подключи NuGet-пакет:

```bash
dotnet pack DuckAiAutomation/DuckAi.Core -c Release
dotnet add package DuckAi.Core --source DuckAiAutomation/DuckAi.Core/bin/Release
```

Пример:

```csharp
using DuckAi;

await using var client = new DuckAiClient(new DuckAiOptions
{
    Headless = true,
    Timeout = TimeSpan.FromSeconds(120)
});

DuckAiResponse response = await client.AskAsync("Объясни архитектуру Transformer");
Console.WriteLine(response.Text);
Console.WriteLine($"Заняло: {response.Elapsed.TotalSeconds:0.0} с");
```

Промпт — единственный обязательный аргумент. Остальные параметры опциональны:

```csharp
await using var client = new DuckAiClient(new DuckAiOptions
{
    TargetUrl = "https://duck.ai/chat",
    ProfileDir = DuckAiOptions.GetDefaultProfileDir(),
    Headless = true,
    Timeout = TimeSpan.FromSeconds(120),
    EnableLogging = true,
    Log = Console.Error.WriteLine
});

var response = await client.AskAsync(new DuckAiRequest
{
    Prompt = "Привет!",
    Timeout = TimeSpan.FromSeconds(60) // переопределение на один запрос
});
```

### Настройки

| Свойство | По умолчанию | Описание |
|---|---|---|
| `TargetUrl` | `https://duck.ai/chat` | Адрес чата |
| `ProfileDir` | каталог данных пользователя | Постоянный профиль Chromium |
| `Headless` | `true` | Запуск без окна браузера |
| `UseNewHeadless` | `true` | Полный Chromium в новом headless-режиме вместо headless shell |
| `Timeout` | 120 секунд | Таймаут ожидания ответа |
| `EnableLogging` | `false` | Включить логи |
| `Log` | `Console.Error` | Куда писать логи |

### Ошибки

| Исключение | Когда возникает |
|---|---|
| `DuckAiTimeoutException` | Duck.ai не начал или не закончил ответ; содержит `PartialText` |
| `DuckAiException` | Прочие ошибки браузера/навигации; внутри `InnerException` |

`OperationCanceledException` пробрасывается как есть при отмене через `CancellationToken`.

### Важно про параллельность

Постоянный профиль Chromium нельзя открыть дважды. Один экземпляр `DuckAiClient`
выполняет запросы последовательно через `SemaphoreSlim`. Если нужно несколько
процессов одновременно — задайте каждому свой `ProfileDir`.

## CLI

```bash
# Промпт позиционным аргументом
dotnet run --project DuckAiAutomation/DuckAi.Cli -- "Столица Франции?"

# С параметрами
dotnet run --project DuckAiAutomation/DuckAi.Cli -- \
  "Столица Франции?" --timeout 90 --json --no-log

# Промпт из файла
dotnet run --project DuckAiAutomation/DuckAi.Cli -- --prompt-file prompt.txt

# JSON из stdin (удобно для вызова из Python/Node)
echo '{"prompt":"Столица Франции?","timeoutSeconds":90}' \
  | dotnet run --project DuckAiAutomation/DuckAi.Cli -- --stdin-json --json

# Показать окно браузера для диагностики
dotnet run --project DuckAiAutomation/DuckAi.Cli -- "тест" --no-headless
```

### Флаги

```
-p, --prompt <text>       Текст промпта
    --prompt-file <path>  Прочитать промпт из файла
    --stdin-json          Прочитать JSON-запрос из stdin
    --timeout <sec>       Таймаут в секундах (по умолчанию 120)
    --no-headless         Показывать окно браузера
    --profile-dir <path>  Каталог профиля браузера
    --url <url>           URL чата
    --json                Вывести ответ в JSON
    --log / --no-log      Включить/выключить логи
-h, --help                Справка
```

### JSON-контракт

Запрос:

```json
{
  "prompt": "Столица Франции?",
  "timeoutSeconds": 90,
  "headless": false,
  "profileDir": "./profile",
  "targetUrl": "https://duck.ai/chat"
}
```

Успешный ответ:

```json
{"ok":true,"text":"Париж","elapsedMs":3210}
```

Ошибка:

```json
{"ok":false,"error":"...","errorType":"timeout","details":"...","partialText":"..."}
```

Коды возврата: `0` — успех, `1` — ошибка, `2` — ошибка аргументов, `130` — отмена.

## Как передавать аргументы

- **Внутри .NET** — типизированный объект (`DuckAiOptions`, `DuckAiRequest`), без JSON.
  Это даёт проверку на этапе компиляции и автодополнение.
- **Между процессами/языками** — JSON на границе (stdin CLI или тело HTTP-запроса),
  с десериализацией в DTO. Не передавайте JSON-строку внутрь библиотечного метода.
- **Простые случаи в shell** — позиционный промпт + флаги. JSON нужен, когда
  промпт длинный, содержит кавычки/переводы строк или параметров много.

## Диагностика

Если Duck.ai не отвечает:

1. Проверьте сеть с той же машины:
   ```bash
   curl -I https://duck.ai/chat
   curl -s https://duck.ai/chat | head
   ```
   Должен быть ответ `200`/редирект, а не таймаут или `403`.
2. **VPN должен быть системным.** VPN-расширение в обычном браузере не влияет на
   Chromium, который запускает Playwright. Включите системный VPN или настройте
   прокси на уровне ОС.
3. Запустите CLI с `--no-headless` и посмотрите, что открывается: CAPTCHA,
   гео-блокировка, страница согласия или обычный чат.
4. Убедитесь, что Chromium установлен: `duckai install` (или `playwright.ps1 install chromium`).
   В headless-режиме библиотека запускает полный Chromium (`Channel = "chromium"`),
   а не облегчённый headless shell: Duck.ai часто не генерирует ответ в headless shell.
   Отключить это поведение можно через `UseNewHeadless = false`.
5. Проверьте, что профиль не занят другим процессом. Если в `ProfileDir` уже
   работает Chromium, закройте его или укажите другой каталог.
6. Если используете свежий профиль, при первом запуске может потребоваться
   вручную принять условия — сделайте это в режиме `--no-headless`.

## Сборка

```bash
dotnet build DuckAiAutomation/DuckAiAutomation.sln -c Release
```
