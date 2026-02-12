# Module 05 - Async Part 3: Task Combinators (ConcurrentDownload)

## Задание

Реализовать метод `ConcurrentDownloadAsync`, который:
- Параллельно запрашивает несколько URL
- Возвращает ответ первого успешного запроса
- Отменяет остальные запросы
- Поддерживает таймаут и внешнюю отмену

---

## Решение

```csharp
public static async Task<string> ConcurrentDownloadAsync(this HttpClient httpClient,
    string[] urls, int millisecondsTimeout, CancellationToken token)
{
    using var timeoutCts = new CancellationTokenSource(millisecondsTimeout);
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
    var linkedToken = linkedCts.Token;

    var tasks = urls.Select(url => httpClient.GetAsync(url, linkedToken)).ToList();

    while (tasks.Count > 0)
    {
        var completedTask = await Task.WhenAny(tasks);
        tasks.Remove(completedTask);

        try
        {
            var response = await completedTask;
            if (response.IsSuccessStatusCode)
            {
                linkedCts.Cancel();
                return await response.Content.ReadAsStringAsync();
            }
        }
        catch
        {
            if (tasks.Count == 0)
                throw;
        }
    }

    throw new InvalidOperationException("All downloads failed.");
}
```

---

## Разбор по шагам

### 1. Объединение токенов отмены (Linked CancellationTokenSource)

```csharp
using var timeoutCts = new CancellationTokenSource(millisecondsTimeout);
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
```

Здесь создаются **два уровня** отмены:
- `timeoutCts` - срабатывает через `millisecondsTimeout` миллисекунд
- `linkedCts` - срабатывает если **любой** из связанных токенов отменён (внешний `token` ИЛИ таймаут)

**`CreateLinkedTokenSource`** - ключевой метод для объединения нескольких причин отмены в одну. Когда любой из исходных токенов отменяется, `linkedCts.Token` тоже отменяется.

```
          token (внешний)
               ↘
linkedCts.Token → все HTTP-запросы используют этот токен
               ↗
    timeoutCts.Token (по таймеру)
```

### 2. Параллельный запуск всех запросов

```csharp
var tasks = urls.Select(url => httpClient.GetAsync(url, linkedToken)).ToList();
```

Все HTTP-запросы запускаются **одновременно**. `.ToList()` материализует коллекцию, немедленно запуская все задачи. Каждый `GetAsync` принимает `linkedToken` для возможности отмены.

### 3. Task.WhenAny - "гонка" задач

```csharp
var completedTask = await Task.WhenAny(tasks);
tasks.Remove(completedTask);
```

`Task.WhenAny` возвращает задачу, которая завершается, когда **любая** из переданных задач завершится. Это паттерн "first wins" - кто быстрее ответил, тот и победил.

После получения завершённой задачи удаляем её из списка (чтобы не проверять повторно).

### 4. Обработка результата и отмена остальных

```csharp
var response = await completedTask;
if (response.IsSuccessStatusCode)
{
    linkedCts.Cancel();  // отменяем все оставшиеся запросы
    return await response.Content.ReadAsStringAsync();
}
```

Если ответ успешный:
1. **`linkedCts.Cancel()`** - отменяем linked token, что приводит к отмене всех ещё не завершённых `GetAsync` вызовов
2. Читаем тело ответа и возвращаем

### 5. Обработка ошибок

```csharp
catch
{
    if (tasks.Count == 0)
        throw;  // последняя задача тоже упала - пробрасываем
}
```

Если задача завершилась ошибкой (сетевая проблема, отмена), но остались другие задачи - продолжаем ждать. Если это последняя задача - пробрасываем исключение.

---

## Паттерн "First Wins" на диаграмме

```
URL1 ──────────────────▶ 200 OK (700ms)
URL2 ────────▶ 200 OK (300ms)         ← ПЕРВЫЙ → return
URL3 ──▶ 200 OK (100ms)    ← САМЫЙ ПЕРВЫЙ → return
          ↑
    Task.WhenAny() возвращает задачу URL3
    linkedCts.Cancel() отменяет URL1 и URL2
```

## Важно: Dispose

`using var` гарантирует, что `CancellationTokenSource` будет корректно освобождён. Без `Dispose` CTS регистрирует таймер-callback, который может утечь.
