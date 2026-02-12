# Module 04 - Async Part 2: BoolAwaiter & TaskCompletionSource

## Задание 1: Custom Awaitable для `bool`

Создать возможность писать `await true` и `await false` в C#.

## Решение: BoolAwaiter

```csharp
public static class BoolExtensions
{
    public static BoolAwaiter GetAwaiter(this bool value)
    {
        return new BoolAwaiter(value);
    }
}

public class BoolAwaiter : INotifyCompletion
{
    private readonly bool _value;

    public BoolAwaiter(bool value)
    {
        _value = value;
    }

    public bool IsCompleted => true;

    public bool GetResult() => _value;

    public void OnCompleted(Action continuation)
    {
        continuation();
    }
}
```

### Как работает `await` изнутри

Когда компилятор видит `await x`, он ищет метод `GetAwaiter()` у объекта `x`. Awaiter должен иметь:

1. **`bool IsCompleted { get; }`** - завершён ли уже результат?
   - Если `true` → результат доступен синхронно, не нужно переключать контекст
   - Если `false` → нужно зарегистрировать продолжение через `OnCompleted`

2. **`T GetResult()`** - получить результат операции (или бросить исключение)

3. **`void OnCompleted(Action continuation)`** - зарегистрировать callback для вызова по завершении

**Для `bool`:** Значение уже "готово" (`IsCompleted = true`), поэтому `await true` просто возвращает `true` синхронно, без переключения потоков.

### Почему работает `await await false`?

1. Внутренний `await false` → вызывает `GetAwaiter()` на `false`, возвращает `BoolAwaiter`, `GetResult()` возвращает `false` (тип `bool`)
2. Внешний `await` → вызывает `GetAwaiter()` на `bool` значении `false`, снова `BoolAwaiter`, снова `false`

### Extension method vs instance method

`bool` - value type, у него нельзя добавить метод напрямую. Используем **extension method** `GetAwaiter(this bool value)` из статического класса `BoolExtensions`. Компилятор C# ищет `GetAwaiter` как extension method, если instance method не найден.

---

## Задание 2: RunProgramAsync с TaskCompletionSource

Запустить внешний процесс асинхронно, вернуть его вывод как `Task<string>`.

## Решение: AsyncTools.RunProgramAsync

```csharp
public static Task<string> RunProgramAsync(string path, string args = "")
{
    var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

    var process = new Process();
    process.EnableRaisingEvents = true;
    process.StartInfo = new ProcessStartInfo(path, args)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    process.Exited += async (sender, eventArgs) =>
    {
        var senderProcess = sender as Process;
        if (senderProcess is null) return;

        if (senderProcess.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            tcs.SetException(new Exception(error));
        }
        else
        {
            var output = await process.StandardOutput.ReadToEndAsync();
            tcs.SetResult(output);
        }

        process.Dispose();
    };

    process.Start();
    return tcs.Task;
}
```

### TaskCompletionSource - мост между callback и async/await

`TaskCompletionSource<T>` (TCS) - ключевой инструмент для преобразования callback-based API в async/await:

```
Event-based API (Process.Exited)
        ↓ SetResult / SetException
TaskCompletionSource<string>
        ↓ .Task
Task<string> (можно await'ить)
```

### Разбор по шагам

1. **`TaskCreationOptions.RunContinuationsAsynchronously`** - гарантирует, что `SetResult`/`SetException` не выполнят continuation синхронно в том же потоке. Без этого возможен deadlock.

2. **`EnableRaisingEvents = true`** - обязательно для срабатывания события `Exited`.

3. **`RedirectStandardOutput/Error = true`** - перехватываем потоки вывода процесса.

4. **`process.Exited` handler:**
   - `ExitCode != 0` → ошибка → `tcs.SetException(...)` → `await` бросит исключение
   - `ExitCode == 0` → успех → `tcs.SetResult(output)` → `await` вернёт строку

5. **`process.Start()`** - запускаем процесс. Метод сразу возвращает `tcs.Task`, не дожидаясь завершения.

### Важные нюансы

- **`SetResult` vs `TrySetResult`**: Используем `Set*` потому что `Exited` гарантированно вызывается один раз. Если бы были гонки, нужен `TrySet*`.
- **`process.Dispose()`** - освобождаем ресурсы ОС после завершения.
- Метод **не** `async` - он синхронно создаёт TCS и возвращает его Task. Сама работа происходит асинхронно через event.
