namespace DuckAi;

/// <summary>Клиент Duck.ai.</summary>
public interface IDuckAiClient : IAsyncDisposable
{
    /// <summary>Отправляет промпт и возвращает ответ.</summary>
    /// <param name="prompt">Текст промпта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<DuckAiResponse> AskAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>Отправляет запрос и возвращает ответ.</summary>
    /// <param name="request">Параметры запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<DuckAiResponse> AskAsync(DuckAiRequest request, CancellationToken cancellationToken = default);
}
