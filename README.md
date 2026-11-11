<h1 align="center">NETDuckAIParser</h1>

<p align="center">
  <strong>Ask Duck.ai from your .NET code or terminal.</strong><br/>
  A small Playwright-powered library with a ready-to-use CLI.
</p>

<p align="center">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white"/>
  <img alt="Playwright" src="https://img.shields.io/badge/Playwright-1.64-2EAD33?logo=playwright&logoColor=white"/>
  <img alt="License: GPL-3.0" src="https://img.shields.io/badge/License-GPLv3-blue.svg"/>
</p>

---

## Overview

**NETDuckAIParser** sends a prompt to [Duck.ai](https://duck.ai) by driving a real
Chromium browser with [Playwright](https://playwright.dev/dotnet/), then returns the
generated answer.

The solution contains two projects:

| Project | Purpose |
|---|---|
| `DuckAiAutomation/DuckAi.Core` | Class library with the public `DuckAiClient` API |
| `DuckAiAutomation/DuckAi.Cli` | Console wrapper named `duckai` (flags + JSON I/O) |

**Highlights**

- One required argument — the prompt — everything else is optional.
- Typed .NET API with `CancellationToken` support.
- CLI with flags, JSON input/output, and predictable exit codes.
- Persistent browser profile, so consent and cookies survive between runs.
- Requests are serialized per client to avoid profile-lock conflicts.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Chromium for Playwright (installed separately; it is not bundled with the NuGet package)

## Getting Started

Install the Playwright browser once:

```bash
dotnet run --project DuckAiAutomation/DuckAi.Cli -- install
```

Build the solution:

```bash
dotnet build DuckAiAutomation/DuckAiAutomation.sln -c Release
```

Run your first prompt from the repository root:

```bash
dotnet run --project DuckAiAutomation/DuckAi.Cli -- "Explain the Transformer architecture in three sentences"
```

> The examples below use `duckai` for brevity. When running from source, replace it with
> `dotnet run --project DuckAiAutomation/DuckAi.Cli --`.

## CLI Usage

```text
duckai "your prompt" [options]
duckai --prompt-file prompt.txt [options]
echo '{"prompt":"..."}' | duckai --stdin-json [options]
duckai install
```

### Flags

| Flag | Argument | Default | Description |
|---|---|---|---|
| `-p`, `--prompt` | `<text>` | — | Prompt text. Can also be passed as the first positional argument. |
| `--prompt-file` | `<path>` | — | Read the prompt from a text file. |
| `--stdin-json` | — | off | Read a JSON request from standard input. |
| `--timeout` | `<seconds>` | `120` | Maximum time to wait for an answer. Must be greater than zero. |
| `--headless` / `--no-headless` / `--headed` | — | headless | Run without a window or show it. A visible browser is useful for debugging, CAPTCHA, or first-run consent. |
| `--profile-dir` | `<path>` | OS user-data directory | Persistent Chromium profile directory. |
| `--url` | `<url>` | `https://duck.ai/chat` | Chat page URL. |
| `--json` | — | off | Print the result as JSON instead of plain text. |
| `--log` / `--no-log` | — | on | Enable or disable progress logs. Logs are written to stderr. |
| `-h`, `--help` | — | — | Show help. |
| `install` | — | — | Subcommand: download the Chromium build used by Playwright. |

### Examples

```bash
# Positional prompt
duckai "What is the capital of France?"

# Machine-readable output for another program
duckai "What is the capital of France?" --timeout 90 --json --no-log

# Prompt from a file
duckai --prompt-file prompt.txt

# JSON request over stdin
echo '{"prompt":"What is the capital of France?","timeoutSeconds":90}' \
  | duckai --stdin-json --json

# Visible browser for debugging
duckai "hello" --no-headless --log

# Custom profile and URL
duckai "hello" --profile-dir ./profile --url https://duck.ai/chat
```

## Library Usage

Reference the core project:

```bash
dotnet add reference path/to/DuckAiAutomation/DuckAi.Core/DuckAi.Core.csproj
```

Or build a NuGet package:

```bash
dotnet pack DuckAiAutomation/DuckAi.Core -c Release
```

Minimal example:

```csharp
using DuckAi;

await using var client = new DuckAiClient(new DuckAiOptions
{
    Headless = true,
    Timeout = TimeSpan.FromSeconds(120)
});

DuckAiResponse response = await client.AskAsync(
    "Explain the Transformer architecture in three sentences");

Console.WriteLine(response.Text);
Console.WriteLine($"Elapsed: {response.Elapsed.TotalSeconds:0.0}s");
```

Per-request overrides are supported:

```csharp
var response = await client.AskAsync(new DuckAiRequest
{
    Prompt = "Summarize this article.",
    Timeout = TimeSpan.FromSeconds(60)
}, cancellationToken);
```

### Options

| Option | Default | Description |
|---|---|---|
| `TargetUrl` | `https://duck.ai/chat` | Chat page URL. |
| `ProfileDir` | OS user-data directory | Persistent Chromium profile. |
| `Headless` | `true` | Run without a visible browser window. |
| `UseNewHeadless` | `true` | Use full Chromium in the new headless mode instead of headless shell. |
| `Timeout` | `120s` | Default answer timeout. |
| `EnableLogging` | `false` | Enable progress logging. |
| `Log` | `Console.Error` | Log sink. |

### Exceptions

| Exception | When it is thrown |
|---|---|
| `DuckAiTimeoutException` | Duck.ai did not start or finish the answer. Exposes `PartialText` when available. |
| `DuckAiException` | Browser, navigation, or other unexpected failures. Contains the original error in `InnerException`. |
| `OperationCanceledException` | The operation was cancelled through `CancellationToken`. |

## JSON Interface

### Request

```json
{
  "prompt": "What is the capital of France?",
  "timeoutSeconds": 90,
  "headless": false,
  "profileDir": "./profile",
  "targetUrl": "https://duck.ai/chat"
}
```

Only `prompt` is required.

### Response

Success:

```json
{"ok":true,"text":"Paris","elapsedMs":3210}
```

Failure:

```json
{"ok":false,"error":"...","errorType":"timeout","details":"...","partialText":"..."}
```

### Exit Codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Runtime error |
| `2` | Invalid arguments |
| `130` | Cancelled |

## Notes

- **Headless mode uses full Chromium.** Playwright's lightweight headless shell is
  often rejected by Duck.ai, so `Headless = true` launches the full Chromium build in
  the new headless mode (`Channel = "chromium"`). Set `UseNewHeadless = false` to opt out.
- **One request at a time per client.** A persistent Chromium profile cannot be opened
  twice. `DuckAiClient` serializes calls with a semaphore. For parallel processes, give
  each process its own `ProfileDir`.
- **Browser installation is required.** `dotnet ... -- install` or
  `playwright.ps1 install chromium`.

## Troubleshooting

1. **Check connectivity:**
   ```bash
   curl -I https://duck.ai/chat
   ```
   A timeout or `403` means the problem is network/VPN related, not the parser.

2. **Use a system-wide VPN.** A VPN browser extension does not affect the Chromium
   instance launched by Playwright.

3. **Run with `--no-headless`** to see whether Duck.ai shows a CAPTCHA, a consent
   dialog, or a regional block. Solving consent once is enough: the persistent profile
   keeps the session.

4. **Reinstall the browser** if Playwright reports that Chromium is missing:
   ```bash
   duckai install
   ```

5. **Use a fresh profile** to rule out stale browser state:
   ```bash
   duckai "test" --no-headless --profile-dir ./tmp_profile
   ```

## License

This project is licensed under the **GNU General Public License v3.0**.
See [LICENSE](LICENSE) for details.
