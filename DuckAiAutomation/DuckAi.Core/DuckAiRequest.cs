namespace DuckAi;

/// <summary>Параметры одного запроса к Duck.ai.</summary>
public sealed record DuckAiRequest
{
    /// <summary>Текст промпта. Обязательное поле.</summary>
    public required string Prompt { get; init; }

    /// <summary>
    /// Таймаут на этот запрос. Если не задан, используется <see cref="DuckAiOptions.Timeout"/>.
    /// </summary>
    public TimeSpan? Timeout { get; init; }
}
