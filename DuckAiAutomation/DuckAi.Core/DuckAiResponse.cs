namespace DuckAi;

/// <summary>Результат запроса к Duck.ai.</summary>
public sealed record DuckAiResponse
{
    /// <summary>Полный текст ответа.</summary>
    public required string Text { get; init; }

    /// <summary>Сколько времени занял запрос.</summary>
    public required TimeSpan Elapsed { get; init; }
}
