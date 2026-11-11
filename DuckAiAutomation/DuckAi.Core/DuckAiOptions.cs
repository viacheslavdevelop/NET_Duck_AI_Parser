namespace DuckAi;

/// <summary>
/// Настройки клиента Duck.ai. Задаются один раз при создании <see cref="DuckAiClient"/>.
/// </summary>
public sealed record DuckAiOptions
{
    /// <summary>Адрес чата Duck.ai.</summary>
    public string TargetUrl { get; init; } = "https://duck.ai/chat";

    /// <summary>
    /// Каталог постоянного профиля браузера. По умолчанию лежит в каталоге данных
    /// пользователя, чтобы библиотека не писала в рабочий каталог приложения.
    /// </summary>
    public string ProfileDir { get; init; } = GetDefaultProfileDir();

    /// <summary>Запускать браузер без окна. По умолчанию <see langword="true"/>.</summary>
    public bool Headless { get; init; } = true;

    /// <summary>
    /// Использовать полноценный Chromium в новом headless-режиме вместо облегчённого
    /// headless shell. Duck.ai хуже относится к headless shell, поэтому по умолчанию включено.
    /// </summary>
    public bool UseNewHeadless { get; init; } = true;

    /// <summary>Таймаут ожидания ответа по умолчанию.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>Включить логирование. По умолчанию выключено, чтобы не засорять вывод приложения.</summary>
    public bool EnableLogging { get; init; }

    /// <summary>Куда писать логи. По умолчанию — <see cref="Console.Error"/>.</summary>
    public Action<string>? Log { get; init; }

    /// <summary>Возвращает каталог профиля по умолчанию для текущей платформы.</summary>
    public static string GetDefaultProfileDir()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(root, "DuckAi", "profile");
    }
}
