# Module 07 - Synchronization: NamedExclusiveScope

## Задание

Создать `IDisposable`-обёртку вокруг `Mutex`, обеспечивающую exclusive region через `using`:
- Локальная блокировка (в пределах процесса)
- Системная блокировка (между процессами ОС)
- При попытке захватить занятый глобальный scope - `InvalidOperationException`

---

## Решение

```csharp
public class NamedExclusiveScope : IDisposable
{
    private readonly Mutex _mutex;

    public NamedExclusiveScope(string name, bool isSystemWide)
    {
        if (isSystemWide)
        {
            _mutex = new Mutex(false, $"Global\\{name}");
            if (!_mutex.WaitOne(0))
            {
                _mutex.Dispose();
                throw new InvalidOperationException($"Unable to get a global lock {name}.");
            }
        }
        else
        {
            _mutex = new Mutex(false, name);
            _mutex.WaitOne();
        }
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
```

---

## Разбор

### Mutex - что это?

`Mutex` (Mutual Exclusion) - примитив синхронизации ОС, который гарантирует, что только один поток/процесс владеет ресурсом одновременно.

В отличие от `lock` (который работает только внутри процесса), **именованный Mutex** может синхронизировать **разные процессы** в ОС.

### Конструктор Mutex

```csharp
new Mutex(false, $"Global\\{name}")
```

- **`false`** - не захватывать mutex при создании (захватим через `WaitOne`)
- **`$"Global\\{name}"`** - глобальное имя. Префикс `Global\` делает mutex видимым для всех сессий ОС (system-wide)

Без `Global\` mutex виден только в текущей сессии пользователя.

### WaitOne(0) - неблокирующая попытка захвата

```csharp
if (!_mutex.WaitOne(0))
{
    _mutex.Dispose();
    throw new InvalidOperationException(...);
}
```

- `WaitOne(0)` - попытка захватить mutex **без ожидания** (timeout = 0 мс)
- Возвращает `true` если захват успешен, `false` если mutex уже занят
- Если не удалось - освобождаем ресурсы и бросаем исключение

Для **локальной** блокировки используем `WaitOne()` без таймаута - ждём сколько нужно.

### Паттерн IDisposable + using

```csharp
using (new NamedExclusiveScope("name", true))
{
    // Exclusive region - только один процесс может быть здесь
    Console.WriteLine("Hello world!");
    Thread.Sleep(300);
}
// Dispose() → ReleaseMutex() + Dispose()
```

`using` гарантирует вызов `Dispose()` при выходе из блока (включая исключения). В `Dispose()` мы:
1. `ReleaseMutex()` - отпускаем владение mutex'ом
2. `Dispose()` - освобождаем handle ОС

### Тест с двумя процессами

Тест запускает **два отдельных процесса** с одинаковым именем scope:
1. Процесс A захватывает `Global\someScopeName` и ждёт 300мс
2. Процесс B пытается захватить тот же mutex → `WaitOne(0)` возвращает `false` → `InvalidOperationException`

```
Процесс A: ─── WaitOne(0) ✓ ─── Sleep(300ms) ─── ReleaseMutex ───
Процесс B:          ─── WaitOne(0) ✗ → throw InvalidOperationException
```

---

## Mutex vs Semaphore vs lock

| Примитив | Область | Кол-во владельцев | Рекурсивный? |
|----------|---------|-------------------|-------------|
| `lock` | Один процесс | 1 | Да |
| `Mutex` | Система (если named) | 1 | Да |
| `Semaphore` | Система (если named) | N (настраивается) | Нет |
| `SemaphoreSlim` | Один процесс | N | Нет |

Для данной задачи Mutex - естественный выбор: нам нужен **один** владелец с возможностью inter-process синхронизации.
