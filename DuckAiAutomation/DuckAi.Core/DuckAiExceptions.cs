namespace DuckAi;

/// <summary>Базовая ошибка клиента Duck.ai.</summary>
public class DuckAiException : Exception
{
    /// <summary>Создаёт исключение с указанным сообщением.</summary>
    public DuckAiException(string message) : base(message)
    {
    }

    /// <summary>Создаёт исключение с указанным сообщением и внутренней ошибкой.</summary>
    public DuckAiException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>Duck.ai не успел начать или закончить генерацию ответа.</summary>
public sealed class DuckAiTimeoutException : DuckAiException
{
    /// <summary>Создаёт исключение с указанным сообщением.</summary>
    /// <param name="message">Описание ошибки.</param>
    /// <param name="partialText">Текст, который успел появиться до таймаута, если он есть.</param>
    public DuckAiTimeoutException(string message, string? partialText = null) : base(message)
    {
        PartialText = partialText;
    }

    /// <summary>Частично полученный ответ, если он был.</summary>
    public string? PartialText { get; }
}
