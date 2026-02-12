# Module 03 - Async Basics: GetStringWithRetries

## Задание

Реализовать асинхронный метод с логикой повторных попыток (retry) для HTTP-запросов:
- Минимум 2 попытки (`maxTries >= 2`)
- Экспоненциальная задержка: 1с, 2с, 4с, 8с...
- Повтор при HTTP-ошибке или сетевом исключении
- Поддержка `CancellationToken`
- При исчерпании попыток - пробросить последнее исключение

---

## Решение

```csharp
public static async Task<string> GetStringWithRetries(HttpClient client, string url,
    int maxTries = 3, CancellationToken token = default)
{
    if (maxTries < 2)
        throw new ArgumentException("maxTries must be at least 2", nameof(maxTries));

    int delayMs = 1000;

    for (int attempt = 1; attempt <= maxTries; attempt++)
    {
        try
        {
            var response = await client.GetAsync(url, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception) when (attempt < maxTries)
        {
            await Task.Delay(delayMs, token);
            delayMs *= 2;
        }
    }

    throw new InvalidOperationException("Unreachable");
}
```

---

## Разбор по шагам

### 1. Валидация входных параметров

```csharp
if (maxTries < 2)
    throw new ArgumentException(...);
```

Бросаем `ArgumentException` до начала async state machine. Это важно - исключение выбрасывается синхронно, а не заворачивается в `Task`.

### 2. Экспоненциальная задержка (Exponential Backoff)

```csharp
int delayMs = 1000;
// ...
await Task.Delay(delayMs, token);
delayMs *= 2;
```

Последовательность задержек: 1000мс → 2000мс → 4000мс → 8000мс...

Exponential backoff - стандартный паттерн для retry-логики. Он даёт перегруженному серверу время восстановиться, не заваливая его повторными запросами.

### 3. Exception filter: `when (attempt < maxTries)`

```csharp
catch (Exception) when (attempt < maxTries)
{
    await Task.Delay(delayMs, token);
    delayMs *= 2;
}
```

Это **exception filter** из C# 6. Ключевое отличие от обычного `catch`:
- Если `attempt < maxTries` → ловим исключение, делаем retry
- Если `attempt == maxTries` (последняя попытка) → **НЕ ловим**, исключение пролетает наверх

Это элегантный способ "пробросить последнее исключение" без `throw;` или сохранения в переменную. Stack trace сохраняется полностью.

### 4. Почему `GetAsync`, а не `GetStringAsync`?

```csharp
var response = await client.GetAsync(url, token);
```

`HttpClient.GetStringAsync` **не принимает** `CancellationToken` (в .NET Core 3.1). Поэтому используем `GetAsync(url, token)`, который:
- Принимает `CancellationToken` - можно отменить HTTP-запрос
- Возвращает `HttpResponseMessage` - можно проверить статус-код

### 5. EnsureSuccessStatusCode

```csharp
response.EnsureSuccessStatusCode();
```

Этот метод проверяет `StatusCode` и бросает `HttpRequestException`, если код не в диапазоне 200-299. Исключение ловится в `catch` блоке и запускает retry.

### 6. Отмена через CancellationToken

Токен отмены используется в двух местах:
- **`client.GetAsync(url, token)`** - отменяет сам HTTP-запрос (бросает `TaskCanceledException`)
- **`Task.Delay(delayMs, token)`** - отменяет ожидание между попытками (бросает `TaskCanceledException`)

Если токен отменён **до** первого вызова, `GetAsync` сразу бросит `TaskCanceledException`. Тест `AfterFirstTryCancellationTest` проверяет отмену во время `Task.Delay` после первой неудачной попытки.

---

## Паттерн Retry в продакшене

В реальных проектах вместо ручной retry-логики используют библиотеки типа **Polly**:

```csharp
// Эквивалент нашей реализации через Polly
var policy = Policy
    .Handle<HttpRequestException>()
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1))
    );
```

Но для понимания механики async/await ручная реализация - отличное упражнение.
