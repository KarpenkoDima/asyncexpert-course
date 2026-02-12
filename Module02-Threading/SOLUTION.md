# Module 02 - Threading: ExecuteOnThread & ExecuteOnThreadPool

## Задание

Реализовать два метода:
1. `ExecuteOnThread` - выполнение действия на **отдельном потоке** (`new Thread`)
2. `ExecuteOnThreadPool` - выполнение действия на **потоке из пула** (`ThreadPool`)

Оба метода должны:
- Выполнять `action` заданное количество раз (`repeats`)
- Проверять `CancellationToken` перед каждой итерацией
- При исключении вызывать `errorAction` (если передана)
- **Ждать** завершения работы перед возвратом

---

## Решение

### ExecuteOnThread

```csharp
public static void ExecuteOnThread(Action action, int repeats,
    CancellationToken token = default, Action<Exception>? errorAction = null)
{
    var thread = new Thread(() =>
    {
        try
        {
            for (int i = 0; i < repeats; i++)
            {
                token.ThrowIfCancellationRequested();
                action();
            }
        }
        catch (Exception ex)
        {
            errorAction?.Invoke(ex);
        }
    });

    thread.Start();
    thread.Join();
}
```

**Ключевые моменты:**

1. **`new Thread(...)`** - создаёт новый поток ОС. Это "тяжёлая" операция: каждый поток требует ~1 МБ стека и время на создание/уничтожение. Свойство `Thread.CurrentThread.IsThreadPoolThread` будет `false`.

2. **`thread.Join()`** - блокирует вызывающий поток до завершения созданного. Это гарантирует, что метод вернёт управление только после полного выполнения работы.

3. **`token.ThrowIfCancellationRequested()`** - проверяет флаг отмены **перед** каждой итерацией. Если токен отменён, выбрасывает `OperationCanceledException`, которая ловится в `catch` и передаётся в `errorAction`.

4. **`errorAction?.Invoke(ex)`** - оператор `?.` гарантирует, что при `null`-значении вызова не будет (тест `ExceptionHandlingWhenErrorActionMissing` проверяет этот сценарий).

### ExecuteOnThreadPool

```csharp
public static void ExecuteOnThreadPool(Action action, int repeats,
    CancellationToken token = default, Action<Exception>? errorAction = null)
{
    using var resetEvent = new AutoResetEvent(false);

    ThreadPool.QueueUserWorkItem(_ =>
    {
        try
        {
            for (int i = 0; i < repeats; i++)
            {
                token.ThrowIfCancellationRequested();
                action();
            }
        }
        catch (Exception ex)
        {
            errorAction?.Invoke(ex);
        }
        finally
        {
            resetEvent.Set();
        }
    });

    resetEvent.WaitOne();
}
```

**Ключевые моменты:**

1. **`ThreadPool.QueueUserWorkItem`** - ставит работу в очередь пула потоков. Пул потоков переиспользует уже созданные потоки, избегая overhead на создание/уничтожение. `IsThreadPoolThread` будет `true`.

2. **`AutoResetEvent`** - примитив синхронизации для ожидания сигнала:
   - Инициализируется в состоянии `false` (несигнальном)
   - `resetEvent.Set()` - устанавливает сигнал (из рабочего потока)
   - `resetEvent.WaitOne()` - блокирует текущий поток до получения сигнала
   - "Auto" означает, что после `WaitOne()` событие автоматически сбрасывается в `false`

3. **`finally` блок** - гарантирует, что `Set()` будет вызван даже при исключении. Без этого `WaitOne()` заблокировал бы вызывающий поток навсегда (deadlock).

---

## Thread vs ThreadPool: когда что использовать?

| Критерий | `new Thread` | `ThreadPool` |
|----------|-------------|--------------|
| Создание | Дорогое (~1 МБ стек) | Переиспользует готовые |
| Контроль | Полный (приоритет, имя, IsBackground) | Ограниченный |
| Подходит для | Долгоживущих задач | Коротких асинхронных операций |
| Масштабируемость | Плохая (сотни потоков = проблемы) | Хорошая (автоматическое управление) |
| `IsThreadPoolThread` | `false` | `true` |

**Правило:** в современном C# `ThreadPool` (через `Task.Run`) - предпочтительный выбор для большинства задач. `new Thread` используется когда нужен полный контроль или очень долгоживущая операция.
