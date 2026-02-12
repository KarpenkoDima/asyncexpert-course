# Async Expert Course — Полное руководство: теория, разбор и решения

> Курс **Async Expert** от Dotnetos — глубокое погружение в асинхронное и многопоточное программирование на C#/.NET.
> Этот документ охватывает **всю теорию**, пошаговый разбор каждого задания и полные решения с объяснениями.

---

## Оглавление

- [Часть I — Теоретическая база](#часть-i--теоретическая-база)
  - [1. Потоки и процессы](#1-потоки-и-процессы)
  - [2. ThreadPool](#2-threadpool)
  - [3. Async/Await — как это работает внутри](#3-asyncawait--как-это-работает-внутри)
  - [4. Task и Task\<T\>](#4-task-и-taskt)
  - [5. CancellationToken](#5-cancellationtoken)
  - [6. TaskCompletionSource](#6-taskcompletionsource)
  - [7. Custom Awaitables](#7-custom-awaitables)
  - [8. Task Combinators](#8-task-combinators)
  - [9. Модель памяти .NET](#9-модель-памяти-net)
  - [10. Lock-free программирование](#10-lock-free-программирование)
  - [11. Примитивы синхронизации](#11-примитивы-синхронизации)
  - [12. Concurrent Collections](#12-concurrent-collections)
  - [13. System.IO.Pipelines](#13-systemiopipelines)
- [Часть II — Решения заданий](#часть-ii--решения-заданий)
  - [Module 01 — Fibonacci Benchmarking](#module-01--fibonacci-benchmarking)
  - [Module 02 — Threading](#module-02--threading)
  - [Module 03 — Async Basics](#module-03--async-basics)
  - [Module 04 — BoolAwaiter и TaskCompletionSource](#module-04--boolawaiter-и-taskcompletionsource)
  - [Module 05 — Task Combinators](#module-05--task-combinators)
  - [Module 06 — Lock-Free AverageMetric](#module-06--lock-free-averagemetric)
  - [Module 07 — NamedExclusiveScope](#module-07--namedexclusivescope)
  - [Module 08 — Concurrent Metrics Counters](#module-08--concurrent-metrics-counters)
  - [Module 09 — Pipelines](#module-09--pipelines)
- [Часть III — Шпаргалки и паттерны](#часть-iii--шпаргалки-и-паттерны)

---

# Часть I — Теоретическая база

---

## 1. Потоки и процессы

### Что такое поток?

**Поток (thread)** — минимальная единица выполнения, которую операционная система может планировать на CPU. Каждый поток имеет:

- Свой **стек вызовов** (~1 МБ по умолчанию в .NET)
- Свой **instruction pointer** (какую инструкцию выполняем)
- Общую **кучу (heap)** с другими потоками того же процесса
- Общее **адресное пространство** процесса

```
┌──────────────────────── Процесс ────────────────────────┐
│                                                          │
│  ┌─────────┐  ┌─────────┐  ┌─────────┐                 │
│  │ Поток 1 │  │ Поток 2 │  │ Поток 3 │                 │
│  │ Stack   │  │ Stack   │  │ Stack   │                 │
│  └────┬────┘  └────┬────┘  └────┬────┘                 │
│       │            │            │                        │
│       └────────────┼────────────┘                        │
│                    │                                     │
│           ┌────────▼────────┐                            │
│           │   Общая куча    │                            │
│           │     (Heap)      │                            │
│           └─────────────────┘                            │
└──────────────────────────────────────────────────────────┘
```

### new Thread vs ThreadPool

| Характеристика | `new Thread()` | `ThreadPool` |
|---|---|---|
| Создание | ~200 мкс, ~1 МБ стек | Переиспользование готовых |
| Контроль | Полный (Priority, Name, IsBackground) | Минимальный |
| Лимит | Ограничен только ОС | Автоматически управляется CLR |
| Подходит для | Долгоживущих задач | Коротких операций |
| `IsThreadPoolThread` | `false` | `true` |
| Используется через | `thread.Start()` + `thread.Join()` | `ThreadPool.QueueUserWorkItem` или `Task.Run` |

**Правило:** В 99% случаев используйте `Task.Run` (который использует `ThreadPool`). Создавайте `new Thread` только если нужен полный контроль или это очень долгоживущая задача.

### Контекстное переключение (Context Switch)

Когда ОС переключает CPU между потоками, происходит:
1. Сохранение регистров CPU текущего потока
2. Загрузка регистров CPU следующего потока
3. Переключение стека
4. Инвалидация кэшей CPU

Это стоит **~1-10 мкс** — кажется мало, но при тысячах потоков деградирует производительность.

---

## 2. ThreadPool

### Архитектура пула потоков .NET

```
                    QueueUserWorkItem / Task.Run
                              │
                              ▼
                    ┌─────────────────┐
                    │  Global Queue   │
                    └────────┬────────┘
                             │
              ┌──────────────┼──────────────┐
              ▼              ▼              ▼
        ┌───────────┐ ┌───────────┐ ┌───────────┐
        │ Worker 1  │ │ Worker 2  │ │ Worker N  │
        │ Local Q   │ │ Local Q   │ │ Local Q   │
        └───────────┘ └───────────┘ └───────────┘
```

- **Global Queue** — общая очередь для всех worker-потоков
- **Local Queue** — каждый worker имеет свою очередь (work stealing)
- CLR автоматически **добавляет/удаляет** worker-потоки в зависимости от нагрузки
- Минимум потоков = количество ядер CPU

### AutoResetEvent — сигнализация между потоками

```csharp
var are = new AutoResetEvent(false);  // создаём в "несигнальном" состоянии

// Поток 1 (ожидающий)
are.WaitOne();     // блокируется до получения сигнала

// Поток 2 (сигнализирующий)
are.Set();         // разблокирует WaitOne, автоматически сбрасывается в false
```

"Auto" означает: после пробуждения одного ожидающего потока, событие **автоматически** сбрасывается. `ManualResetEvent` требует явного `Reset()`.

---

## 3. Async/Await — как это работает внутри

### Что делает компилятор

Когда компилятор видит `async` метод, он:

1. Создаёт **state machine** — структуру с полями для всех локальных переменных
2. Каждый `await` становится **точкой прерывания** (suspension point)
3. Если операция завершена (`IsCompleted = true`) — продолжаем синхронно
4. Если нет — регистрируем continuation и возвращаем управление вызывающему

```csharp
// Вы пишете:
async Task<string> GetDataAsync()
{
    var response = await httpClient.GetAsync(url);
    var content = await response.Content.ReadAsStringAsync();
    return content;
}

// Компилятор генерирует (упрощённо):
struct GetDataAsyncStateMachine : IAsyncStateMachine
{
    public int state;  // -1, 0, 1
    public AsyncTaskMethodBuilder<string> builder;

    // Все локальные переменные становятся полями:
    HttpResponseMessage response;
    string content;

    public void MoveNext()
    {
        switch (state)
        {
            case -1:  // начало
                var awaiter1 = httpClient.GetAsync(url).GetAwaiter();
                if (!awaiter1.IsCompleted)
                {
                    state = 0;
                    builder.AwaitUnsafeOnCompleted(ref awaiter1, ref this);
                    return;  // ← возвращаем управление!
                }
                goto case 0;

            case 0:  // после первого await
                response = awaiter1.GetResult();
                var awaiter2 = response.Content.ReadAsStringAsync().GetAwaiter();
                if (!awaiter2.IsCompleted)
                {
                    state = 1;
                    builder.AwaitUnsafeOnCompleted(ref awaiter2, ref this);
                    return;
                }
                goto case 1;

            case 1:  // после второго await
                content = awaiter2.GetResult();
                builder.SetResult(content);
                break;
        }
    }
}
```

### Ключевой момент: async != многопоточность

`async/await` — это **НЕ** про создание потоков. Это про **неблокирующее ожидание**.

```
Синхронный код:       Асинхронный код:

Поток: ████████████   Поток: ███░░░░░███
       ↑ blocked           ↑free  ↑resume
                           ожидание I/O
```

При `await` поток **освобождается** и может делать другую работу. Когда I/O завершается, continuation выполняется (обычно на потоке из пула).

### SynchronizationContext

Определяет **куда** вернётся execution после `await`:

| Контекст | Поведение |
|---|---|
| UI (WPF/WinForms) | Возвращается в UI-поток |
| ASP.NET Core | Любой поток из пула |
| Console App | Любой поток из пула |
| `ConfigureAwait(false)` | Явно: любой поток из пула |

```csharp
await Task.Delay(100);                     // вернётся в текущий SyncContext
await Task.Delay(100).ConfigureAwait(false); // вернётся в любой поток пула
```

**Правило для библиотек:** Всегда используйте `.ConfigureAwait(false)` в библиотечном коде — вам не нужен конкретный контекст.

---

## 4. Task и Task\<T\>

### Состояния Task

```
                    Created
                       │
                       ▼
                 WaitingToRun
                       │
                       ▼
                    Running
                    /     \
                   ▼       ▼
          RanToCompletion  Faulted
                           │
                           ▼
                        Canceled
```

### Способы создания Task

```csharp
// 1. Task.Run — выполнить на ThreadPool
Task.Run(() => DoWork());

// 2. Task.FromResult — уже готовый результат (нет аллокации для true/false/0/1)
Task.FromResult(42);

// 3. Task.CompletedTask — завершённый Task без результата
Task.CompletedTask;

// 4. TaskCompletionSource — ручное управление
var tcs = new TaskCompletionSource<int>();
tcs.SetResult(42);  // или SetException, SetCanceled

// 5. async метод — компилятор создаёт Task
async Task<int> GetAsync() { ... }
```

### ValueTask — оптимизация для горячего пути

`Task<T>` — это объект в куче (аллокация ~72 байт). Для методов, которые часто возвращают результат синхронно, можно использовать `ValueTask<T>`:

```csharp
// Если результат в кэше — нулевые аллокации
public ValueTask<int> GetValueAsync()
{
    if (_cache.TryGetValue(key, out var value))
        return new ValueTask<int>(value);  // нет аллокации!

    return new ValueTask<int>(GetValueSlowAsync());
}
```

**Ограничение:** `ValueTask` нельзя `await`-ить дважды и нельзя использовать с `Task.WhenAll`.

---

## 5. CancellationToken

### Архитектура

```
CancellationTokenSource (управляет)
        │
        │ .Token
        ▼
CancellationToken (передаётся в операции)
        │
        ├── token.IsCancellationRequested
        ├── token.ThrowIfCancellationRequested()
        └── token.Register(callback)
```

### Паттерны использования

```csharp
// 1. Создание с таймаутом
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

// 2. Ручная отмена
cts.Cancel();

// 3. Отмена по таймеру
cts.CancelAfter(3000);

// 4. Связывание нескольких источников
var linked = CancellationTokenSource.CreateLinkedTokenSource(token1, token2);
// linked.Token отменяется если ЛЮБОЙ из token1/token2 отменён

// 5. Проверка в цикле
while (!token.IsCancellationRequested)
{
    DoWork();
}

// 6. Бросание исключения
token.ThrowIfCancellationRequested();  // → OperationCanceledException
```

### Linked CancellationTokenSource — объединение причин отмены

```
    Внешний token          Timeout CTS
    (от вызывающего)       (CancelAfter)
           ↘                    ↙
       LinkedTokenSource
              │
              ▼
      linkedCts.Token  →  используется во всех операциях
```

Когда **любой** из связанных токенов отменяется → `linkedCts.Token` тоже отменяется. Это ключевой паттерн для объединения таймаута и внешней отмены (Module 05).

**Важно:** Всегда `Dispose()` у `CancellationTokenSource` — он регистрирует таймеры и колбэки, которые могут утечь.

---

## 6. TaskCompletionSource

### Мост между callback и async/await

`TaskCompletionSource<T>` позволяет вручную управлять состоянием `Task<T>`. Это **основной инструмент** для оборачивания callback-based API в async/await.

```
Старый API (callbacks/events)
        │
        │  SetResult / SetException / SetCanceled
        ▼
TaskCompletionSource<T>
        │
        │  .Task
        ▼
Task<T> ─── можно await'ить
```

### API

```csharp
var tcs = new TaskCompletionSource<string>();

// Успех:
tcs.SetResult("data");       // бросит если уже завершён
tcs.TrySetResult("data");    // вернёт false если уже завершён

// Ошибка:
tcs.SetException(ex);
tcs.TrySetException(ex);

// Отмена:
tcs.SetCanceled();
tcs.TrySetCanceled();
```

### RunContinuationsAsynchronously

```csharp
// ВСЕГДА используйте этот флаг:
var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
```

Без него `SetResult()` выполнит все continuation **синхронно** в текущем потоке. Это может привести к:
- Deadlock (если continuation пытается захватить lock, который держит текущий поток)
- Stack overflow (глубокая цепочка continuations)
- Непредсказуемым задержкам

С `RunContinuationsAsynchronously` continuations планируются на ThreadPool.

---

## 7. Custom Awaitables

### Контракт Awaitable/Awaiter

Чтобы тип `T` можно было `await`-ить, нужно:

```
T должен иметь метод GetAwaiter() → возвращает Awaiter

Awaiter должен реализовать:
├── bool IsCompleted { get; }      — завершено ли?
├── T GetResult()                   — получить результат
└── void OnCompleted(Action)        — зарегистрировать callback
    (implements INotifyCompletion)
```

### Как компилятор использует Awaiter

```csharp
var awaiter = expression.GetAwaiter();

if (awaiter.IsCompleted)
{
    // Горячий путь: результат уже есть, продолжаем синхронно
    var result = awaiter.GetResult();
}
else
{
    // Холодный путь: регистрируем продолжение
    awaiter.OnCompleted(() =>
    {
        var result = awaiter.GetResult();
        // продолжаем выполнение state machine
    });
    return; // освобождаем поток
}
```

### Extension method GetAwaiter

Если тип не ваш (например, `bool`, `TimeSpan`), можно добавить `GetAwaiter` как extension method:

```csharp
public static class BoolExtensions
{
    public static BoolAwaiter GetAwaiter(this bool value)
        => new BoolAwaiter(value);
}
```

Компилятор ищет `GetAwaiter`:
1. Сначала как instance method
2. Потом как extension method (в scope-е)

---

## 8. Task Combinators

### Task.WhenAll — ждать все

```csharp
var results = await Task.WhenAll(task1, task2, task3);
// results = [result1, result2, result3]
// Бросит AggregateException если хотя бы одна задача упала
```

Все задачи выполняются **параллельно**. `WhenAll` завершается когда **последняя** задача завершена.

### Task.WhenAny — ждать первую

```csharp
Task<int> winner = await Task.WhenAny(task1, task2, task3);
int result = await winner;  // получить результат победителя
```

Завершается когда **первая** задача из списка завершена. Остальные продолжают выполняться!

**Паттерн "First Wins" с отменой остальных:**

```csharp
var tasks = urls.Select(url => client.GetAsync(url, linkedToken)).ToList();

while (tasks.Count > 0)
{
    var completed = await Task.WhenAny(tasks);
    tasks.Remove(completed);

    var response = await completed;
    if (response.IsSuccessStatusCode)
    {
        linkedCts.Cancel();  // отменяем остальные!
        return await response.Content.ReadAsStringAsync();
    }
}
```

### Визуализация WhenAll vs WhenAny

```
Task.WhenAll:
  Task1: ████████████████████▶ done
  Task2: ██████████▶ done
  Task3: ██████████████████████████▶ done
  WhenAll:                          ▶ завершается здесь (последняя)

Task.WhenAny:
  Task1: ████████████████████
  Task2: ██████████▶ done ← первая!
  Task3: █████████████████████████
  WhenAny:          ▶ завершается здесь (первая)
```

---

## 9. Модель памяти .NET

### Проблема: почему потоки не видят изменения друг друга?

Между записью в память и чтением из памяти есть несколько уровней кэширования:

```
CPU Core 1          CPU Core 2
┌──────────┐        ┌──────────┐
│ Registers│        │ Registers│
│  L1 Cache│        │  L1 Cache│
│  L2 Cache│        │  L2 Cache│
└─────┬────┘        └─────┬────┘
      │                   │
      └───────┬───────────┘
              │
       ┌──────▼──────┐
       │  L3 Cache   │
       │   (shared)  │
       └──────┬──────┘
              │
       ┌──────▼──────┐
       │    RAM      │
       └─────────────┘
```

Поток на Core 1 пишет `x = 42`. Значение может быть:
- Только в регистре Core 1
- В L1/L2 кэше Core 1
- Ещё не видно Core 2

**Компилятор и CPU** могут также **переупорядочивать** операции для оптимизации:

```csharp
// Вы пишете:
data = LoadData();    // (1)
dataReady = true;     // (2)

// CPU может выполнить:
dataReady = true;     // (2) ← сначала это!
data = LoadData();    // (1) ← потом это!
```

Другой поток может увидеть `dataReady = true`, но `data` ещё не загружена!

### Барьеры памяти (Memory Barriers)

Барьеры запрещают переупорядочивание и гарантируют видимость:

- **Acquire barrier** (при чтении): все последующие чтения/записи не могут быть перемещены ПЕРЕД этим чтением
- **Release barrier** (при записи): все предыдущие чтения/записи не могут быть перемещены ПОСЛЕ этой записи
- **Full barrier**: оба направления

```csharp
// Volatile.Read — acquire fence
var value = Volatile.Read(ref field);
// Гарантия: всё что ниже не выполнится раньше этого чтения

// Volatile.Write — release fence
Volatile.Write(ref field, value);
// Гарантия: всё что выше не выполнится позже этой записи
```

---

## 10. Lock-free программирование

### Interlocked — атомарные операции CPU

Обычный `x++` это 3 операции: read → increment → write. Между ними другой поток может изменить значение.

`Interlocked` использует аппаратные инструкции CPU (например, `lock cmpxchg` на x86), которые выполняют read-modify-write **атомарно**:

```csharp
Interlocked.Increment(ref count);        // атомарный count++
Interlocked.Decrement(ref count);        // атомарный count--
Interlocked.Add(ref sum, value);         // атомарный sum += value
Interlocked.Exchange(ref x, newValue);   // атомарный x = newValue, возвращает старое
Interlocked.CompareExchange(ref x, newValue, expected);  // CAS-операция
```

### Compare-And-Swap (CAS) — основа lock-free

```csharp
// Interlocked.CompareExchange(ref location, newValue, comparand)
// Если location == comparand → записать newValue, вернуть старое значение
// Если location != comparand → ничего не делать, вернуть текущее значение

// Паттерн CAS-loop:
int oldValue, newValue;
do
{
    oldValue = Volatile.Read(ref field);
    newValue = Transform(oldValue);
} while (Interlocked.CompareExchange(ref field, newValue, oldValue) != oldValue);
```

### Lock vs Lock-free: когда что?

| Ситуация | Рекомендация |
|---|---|
| Простая операция (increment, swap) | `Interlocked` |
| Чтение актуального значения | `Volatile.Read` |
| Несколько связанных переменных | `lock` (или CAS с immutable snapshot) |
| Сложная логика в критической секции | `lock` |
| Допустима небольшая неточность | `Volatile` |
| Высокая contention (тысячи потоков) | Lock-free |

---

## 11. Примитивы синхронизации

### Обзор всех примитивов

```
                    ┌─────────────────────────────────┐
                    │      Примитивы синхронизации     │
                    └─────────┬───────────────────────┘
                              │
              ┌───────────────┼───────────────┐
              ▼               ▼               ▼
        User-mode        Kernel-mode      Hybrid
     (быстрые)          (тяжёлые)       (лучшее из обоих)

  ┌────────────┐    ┌──────────────┐   ┌──────────────┐
  │SpinLock    │    │Mutex         │   │lock/Monitor  │
  │SpinWait    │    │Semaphore     │   │SemaphoreSlim │
  │Interlocked │    │EventWaitH.   │   │ManualResetE. │
  └────────────┘    └──────────────┘   │  Slim        │
                                       │ReaderWriterL.│
                                       │  Slim        │
                                       └──────────────┘
```

### Подробная таблица

| Примитив | Область | Макс. владельцев | Рекурсивный | Async-совместимый |
|---|---|---|---|---|
| `lock` (Monitor) | Процесс | 1 | Да | Нет |
| `Mutex` | Система* | 1 | Да | Нет |
| `Semaphore` | Система* | N | Нет | Нет |
| `SemaphoreSlim` | Процесс | N | Нет | Да (`WaitAsync`) |
| `SpinLock` | Процесс | 1 | Опционально | Нет |
| `ReaderWriterLockSlim` | Процесс | N read / 1 write | Опционально | Нет |

\* — при использовании именованных экземпляров

### Mutex — межпроцессная синхронизация

```csharp
// Локальный (внутри процесса)
var mutex = new Mutex(false, "myMutex");

// Системный (между процессами)
var mutex = new Mutex(false, @"Global\myMutex");
//                              ↑ этот префикс делает его system-wide

mutex.WaitOne();       // захватить (блокирующе)
mutex.WaitOne(0);      // попытка захвата без ожидания
mutex.WaitOne(5000);   // ждать до 5 секунд
mutex.ReleaseMutex();  // отпустить
mutex.Dispose();       // освободить handle ОС
```

### lock — самый используемый

```csharp
private readonly object _sync = new object();

lock (_sync)
{
    // Критическая секция — только один поток одновременно
    // Компилятор генерирует Monitor.Enter/Monitor.Exit в try/finally
}
```

`lock` — это синтаксический сахар:
```csharp
bool lockTaken = false;
try
{
    Monitor.Enter(_sync, ref lockTaken);
    // критическая секция
}
finally
{
    if (lockTaken) Monitor.Exit(_sync);
}
```

---

## 12. Concurrent Collections

### System.Collections.Concurrent

| Коллекция | Аналог | Механизм |
|---|---|---|
| `ConcurrentDictionary<K,V>` | `Dictionary<K,V>` | Striped locking + CAS |
| `ConcurrentQueue<T>` | `Queue<T>` | Lock-free (CAS) |
| `ConcurrentStack<T>` | `Stack<T>` | Lock-free (CAS) |
| `ConcurrentBag<T>` | `List<T>` | Thread-local storage |
| `BlockingCollection<T>` | — | Producer-Consumer с блокировкой |

### ConcurrentDictionary — ключевые методы

```csharp
var dict = new ConcurrentDictionary<string, int>();

// GetOrAdd — получить или создать (атомарно)
var value = dict.GetOrAdd("key", _ => ExpensiveCreate());
// ВНИМАНИЕ: factory может вызваться несколько раз (но только один результат сохранится)

// AddOrUpdate — добавить или обновить (с CAS-loop)
dict.AddOrUpdate("key",
    addValue: 1,                            // если ключа нет
    updateValueFactory: (k, old) => old + 1  // если ключ есть
);
// ВНИМАНИЕ: updateValueFactory может вызваться несколько раз!
```

### Два подхода к атомарному счётчику

**Подход 1: AddOrUpdate (CAS retry при contention)**
```csharp
dict.AddOrUpdate(key, 1, (_, old) => old + 1);
```
Внутри: read → compute → CAS → retry if failed

**Подход 2: GetOrAdd + Interlocked (CAS только при первом создании)**
```csharp
var counter = dict.GetOrAdd(key, _ => new AtomicCounter());
counter.Increment();  // Interlocked.Increment — всегда с первой попытки
```

Подход 2 лучше при частых инкрементах — contention на словаре только при создании ключей.

---

## 13. System.IO.Pipelines

### Проблема с Stream

Традиционный `Stream` API:
```csharp
var buffer = new byte[4096];  // ← аллокация при каждом вызове!
int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
// Если данные содержат неполное сообщение — храни остаток, жонглируй буферами...
```

Проблемы:
1. Аллокации `byte[]` → давление на GC
2. Ручное управление "остатками" данных
3. Нет back-pressure (reader не может сказать writer-у "подожди")

### Pipe — архитектура

```
┌──────────┐     ┌───────────────────┐     ┌──────────┐
│  Source   │────▶│       Pipe        │────▶│ Consumer │
│ (network) │     │  ┌─────────────┐  │     │(parsing) │
│           │     │  │ Ring Buffer │  │     │          │
│  Writer   │────▶│  │ (pooled)    │──│────▶│  Reader  │
└──────────┘     │  └─────────────┘  │     └──────────┘
                  └───────────────────┘

  PipeWriter            Pipe              PipeReader
  GetMemory()                            ReadAsync()
  Advance()                              AdvanceTo()
  FlushAsync()                           CompleteAsync()
  CompleteAsync()
```

### PipeWriter (продюсер)

```csharp
while (true)
{
    // 1. Получить буфер из внутреннего пула (НУЛЕВЫЕ аллокации!)
    Memory<byte> memory = writer.GetMemory(minimumBufferSize);

    // 2. Записать данные прямо в буфер
    int bytesRead = await stream.ReadAsync(memory);
    if (bytesRead == 0) break;

    // 3. Сообщить сколько байт реально записали
    writer.Advance(bytesRead);

    // 4. Сделать данные доступными для Reader
    //    Если Reader не успевает → FlushAsync будет ждать (back-pressure!)
    FlushResult result = await writer.FlushAsync();
    if (result.IsCompleted) break;
}
await writer.CompleteAsync();
```

### PipeReader (потребитель)

```csharp
while (true)
{
    // 1. Ждать новых данных от Writer
    ReadResult result = await reader.ReadAsync();
    ReadOnlySequence<byte> buffer = result.Buffer;

    // 2. Обработать данные
    ProcessData(buffer);

    // 3. Сообщить сколько данных обработано
    reader.AdvanceTo(buffer.End);
    //   consumed: buffer.End — всё обработано
    //   examined: buffer.End — мы посмотрели всё

    if (result.IsCompleted) break;
}
await reader.CompleteAsync();
```

### ReadOnlySequence\<byte\> и SequenceReader\<byte\>

Данные в Pipe хранятся как **связный список сегментов** (не один массив):

```
Segment 1        Segment 2        Segment 3
┌───────────┐    ┌───────────┐    ┌───────────┐
│ Hello\nWo │───▶│ rld\nFoo  │───▶│ Bar\n     │
└───────────┘    └───────────┘    └───────────┘
```

`SequenceReader<byte>` умеет работать с такой сегментированной памятью:
```csharp
var reader = new SequenceReader<byte>(buffer);
while (reader.TryAdvanceTo((byte)'\n'))
    lineCount++;
```

`TryAdvanceTo` эффективно ищет байт через все сегменты, без копирования данных в единый массив.

---

# Часть II — Решения заданий

---

## Module 01 — Fibonacci Benchmarking

**Файл:** `Module01-Introduction/Benchmark/Fibonacci.cs`

### Задание
1. Реализовать `RecursiveWithMemoization` и `Iterative` решения
2. Добавить `[MemoryDiagnoser]`
3. Сравнить результаты в Release-конфигурации
4. Изучить disassembler report

### Решение

```csharp
[DisassemblyDiagnoser(exportCombinedDisassemblyReport: true)]
[MemoryDiagnoser]
public class FibonacciCalc
{
    [Benchmark(Baseline = true)]
    [ArgumentsSource(nameof(Data))]
    public ulong Recursive(ulong n)
    {
        if (n == 1 || n == 2) return 1;
        return Recursive(n - 2) + Recursive(n - 1);
    }

    [Benchmark]
    [ArgumentsSource(nameof(Data))]
    public ulong RecursiveWithMemoization(ulong n)
    {
        var cache = new Dictionary<ulong, ulong>();
        return FibMemo(n, cache);
    }

    private static ulong FibMemo(ulong n, Dictionary<ulong, ulong> cache)
    {
        if (n == 1 || n == 2) return 1;
        if (cache.TryGetValue(n, out var cached)) return cached;
        var result = FibMemo(n - 2, cache) + FibMemo(n - 1, cache);
        cache[n] = result;
        return result;
    }

    [Benchmark]
    [ArgumentsSource(nameof(Data))]
    public ulong Iterative(ulong n)
    {
        if (n == 1 || n == 2) return 1;
        ulong prev = 1, curr = 1;
        for (ulong i = 3; i <= n; i++)
        {
            var next = prev + curr;
            prev = curr;
            curr = next;
        }
        return curr;
    }

    public IEnumerable<ulong> Data()
    {
        yield return 15;
        yield return 35;
    }
}
```

### Почему это работает

| Метод | Сложность | Память | Для n=35 |
|---|---|---|---|
| Recursive | O(2^n) | O(n) стек | ~9.2 млрд вызовов, ~50 мс |
| RecursiveWithMemoization | O(n) | O(n) Dictionary | ~35 вызовов, ~1 мкс |
| Iterative | O(n) | O(1) | Цикл из 33 итераций, ~20 нс |

**Мемоизация** превращает экспоненциальный алгоритм в линейный, кэшируя каждый `Fib(k)` ровно один раз.

**Iterative** — чемпион: никаких рекурсий, никаких аллокаций, всё в двух переменных на стеке.

**`[MemoryDiagnoser]`** показывает аллокации: Iterative = 0 B, Memoization = ~1-2 KB (Dictionary).

---

## Module 02 — Threading

**Файл:** `Module02-Threading/ThreadPoolExercises.Core/ThreadingHelpers.cs`

### Задание
Два метода: запуск действия на **новом потоке** и на **потоке из пула**, с ожиданием завершения, отменой и обработкой ошибок.

### Решение

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

### Ключевые решения

1. **`thread.Join()`** vs **`AutoResetEvent`** — два способа дождаться завершения:
   - `Join` — для `Thread` (блокирует до завершения потока)
   - `AutoResetEvent.WaitOne()` — для ThreadPool (потому что у нас нет объекта Thread для вызова Join)

2. **`finally { resetEvent.Set(); }`** — критически важно! Без `finally` при исключении `Set()` не вызовется, и `WaitOne()` заблокирует навсегда (deadlock).

3. **`token.ThrowIfCancellationRequested()`** перед `action()` — проверяем отмену до каждой итерации. При отмене бросает `OperationCanceledException`, который ловится в `catch`.

4. **`errorAction?.Invoke(ex)`** — null-conditional operator: если `errorAction` не передан, ничего не произойдёт, исключение просто "проглотится" (что и проверяет тест `ExceptionHandlingWhenErrorActionMissing`).

---

## Module 03 — Async Basics

**Файл:** `Module03-AsyncBasics/AsyncAwaitExercises.Core/AsyncHelpers.cs`

### Задание
HTTP GET с retry-логикой, exponential backoff, cancellation.

### Решение

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

### Разбор главной "хитрости" — Exception Filter

```csharp
catch (Exception) when (attempt < maxTries)
```

Это **exception filter**, а не обычный `catch`. Разница принципиальная:

```
Попытка 1: catch → retry   (attempt=1 < 3)
Попытка 2: catch → retry   (attempt=2 < 3)
Попытка 3: НЕ ловим!       (attempt=3 == 3) → исключение летит наверх
```

На последней попытке фильтр `when (3 < 3)` = `false`, и `catch` блок **не срабатывает**. Исключение пролетает как если бы `try/catch` не было. Это сохраняет оригинальный stack trace.

### Временная шкала тестов

```
AfterFirstTryCancellationTest (CancelAfter 500мс):
0ms     500ms    1000ms
│ GET ──▶ 500   │        │
│        CTS!   │        │
│    delay(1000, token) ← TaskCanceledException!

AfterSecondTryCancellationTest (CancelAfter 1500мс):
0ms     1000ms   1500ms   3000ms
│ GET ──▶ fail  │ GET ──▶ fail │        │
│    delay(1s)  │   delay(2s, token)    │
│               │        CTS!          │
│               │        ← TaskCanceledException!
```

---

## Module 04 — BoolAwaiter и TaskCompletionSource

### Задание 4a: BoolAwaiter

**Файл:** `Module04-AsyncPart2/AwaitableExercises.Core/BoolAwaiter.cs`

Сделать так, чтобы `await true` и `await false` компилировались и работали.

### Решение

```csharp
public static class BoolExtensions
{
    public static BoolAwaiter GetAwaiter(this bool value)
        => new BoolAwaiter(value);
}

public class BoolAwaiter : INotifyCompletion
{
    private readonly bool _value;

    public BoolAwaiter(bool value) => _value = value;
    public bool IsCompleted => true;
    public bool GetResult() => _value;
    public void OnCompleted(Action continuation) => continuation();
}
```

### Почему `await await false` работает?

```
await await false
      └── BoolAwaiter.GetResult() → false (тип bool)
└── BoolAwaiter.GetResult() → false (тип bool)

Шаг 1: await false → GetAwaiter(false) → BoolAwaiter → GetResult() → false
Шаг 2: await false → GetAwaiter(false) → BoolAwaiter → GetResult() → false
```

`GetResult()` возвращает `bool`, а `bool` имеет `GetAwaiter` extension → можно `await`-ить повторно.

### Задание 4b: RunProgramAsync

**Файл:** `Module04-AsyncPart2/TaskCompletionSourceExercises.Core/AsyncTools.cs`

Запуск внешнего процесса с возвратом `Task<string>`.

### Решение

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

### Поток выполнения

```
Вызывающий код:
──── RunProgramAsync() ──── return tcs.Task ──── await ─────────── получить результат
         │                                                              ↑
         │ process.Start()                                              │
         ▼                                                              │
  Внешний процесс:                                                     │
  ──── работает ──── завершается ──── Exited event ──── SetResult() ────┘
```

Метод **не** `async` — он возвращает `tcs.Task` синхронно. Вся магия в callback `Exited`.

---

## Module 05 — Task Combinators

**Файл:** `Module05-AsyncPart3/TaskCombinatorsExercises.Core/HttpClientExtensions.cs`

### Задание
Параллельная загрузка с нескольких URL, возврат первого успешного, отмена остальных, таймаут.

### Решение

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

### Диаграмма выполнения

```
urls: ["/delay/700", "/delay/300", "/delay/100"]
timeout: 10000ms

t=0ms:    Запуск всех трёх GetAsync параллельно
          ├── Task1: GET /delay/700 ──────────────────▶
          ├── Task2: GET /delay/300 ──────────▶
          └── Task3: GET /delay/100 ────▶

t=100ms:  WhenAny → Task3 завершился!
          Task3 → 200 OK → linkedCts.Cancel()
          Task1, Task2 → TaskCanceledException (игнорируются)
          return "100"
```

### Три уровня отмены

```
1. Внешний token    → вызывающий код решил отменить
2. timeoutCts       → прошло millisecondsTimeout мс
3. linkedCts.Cancel → мы сами отменяем после успеха

Все три приводят к отмене linkedToken → отмене всех GetAsync
```

---

## Module 06 — Lock-Free AverageMetric

**Файл:** `Module06-LowLevel/LowLevelExercises.Core/AverageMetric.cs`

### Задание
Убрать все `lock` и использовать `Interlocked`/`Volatile`.

### Решение

```csharp
public class AverageMetric
{
    int sum = 0;
    int count = 0;

    public void Report(int value)
    {
        Interlocked.Add(ref sum, value);
        Interlocked.Increment(ref count);
    }

    public double Average
    {
        get
        {
            var currentCount = Volatile.Read(ref count);
            var currentSum = Volatile.Read(ref sum);
            return Calculate(currentCount, currentSum);
        }
    }

    static double Calculate(in int count, in int sum)
    {
        if (count == 0) return double.NaN;
        return (double)sum / count;
    }
}
```

### Почему допустима неточность

С `lock` операции `sum += value` и `count += 1` были атомарны **вместе**. Без `lock` они атомарны **по отдельности**:

```
Поток A: Interlocked.Add(ref sum, 5)
                                         Поток B: Average → sum=5, count=0 → NaN? Нет!
Поток A: Interlocked.Increment(ref count)

Но на практике тест сходится:
- 8 потоков × 1М итераций
- Каждый Report(0) или Report(1) с вероятностью 50/50
- Ожидаемое среднее = 0.5
- Lock-free версия даёт то же значение в конце (все Report завершились до проверки)
```

Тест работает потому что `Average` проверяется **после** `Task.WhenAll` — все Report уже завершены.

---

## Module 07 — NamedExclusiveScope

**Файл:** `Module07-Synchronization/Synchronization.Core/NamedExclusiveScope.cs`

### Задание
IDisposable-обёртка вокруг Mutex для exclusive region через `using`.

### Решение

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

### Сценарий теста с двумя процессами

```
Процесс A: NamedExclusiveScope("someScopeName", true)
            → new Mutex(false, "Global\someScopeName")
            → WaitOne(0) → true ✓
            → Sleep(300ms)
            → Dispose() → ReleaseMutex()

Процесс B: NamedExclusiveScope("someScopeName", true)  (запускается параллельно)
            → new Mutex(false, "Global\someScopeName")
            → WaitOne(0) → false ✗ (mutex занят процессом A)
            → throw InvalidOperationException("Unable to get a global lock someScopeName.")
```

### Зачем `Global\`?

Без префикса `Global\` mutex виден только **в текущей сессии пользователя**. С `Global\` — виден **всем процессам в системе**, включая другие сессии и сервисы.

---

## Module 08 — Concurrent Metrics Counters

**Файлы:**
- `DataStructures/ConcurrentDictionaryOnlyMetricsCounter.cs`
- `DataStructures/ConcurrentDictionaryWithCounterMetricsCounter.cs`

### Задание
Два способа реализации потокобезопасного счётчика.

### Решение 1: Только ConcurrentDictionary

```csharp
public class ConcurrentDictionaryOnlyMetricsCounter : IMetricsCounter
{
    private readonly ConcurrentDictionary<string, int> _dictionary = new();

    public void Increment(string key)
    {
        _dictionary.AddOrUpdate(key, 1, (_, oldValue) => oldValue + 1);
    }

    public IEnumerator<KeyValuePair<string, int>> GetEnumerator()
        => _dictionary.GetEnumerator();
}
```

**Как работает AddOrUpdate внутри:**
```
1. Вычислить hash(key), найти bucket
2. Если ключа нет → добавить (key, 1)
3. Если ключ есть → oldValue = read current
4. newValue = factory(key, oldValue) = oldValue + 1
5. CAS: if current == oldValue → write newValue
6. Если CAS failed (кто-то изменил) → goto 3 (retry)
```

### Решение 2: ConcurrentDictionary + AtomicCounter

```csharp
public class ConcurrentDictionaryWithCounterMetricsCounter : IMetricsCounter
{
    private readonly ConcurrentDictionary<string, AtomicCounter> _dictionary = new();

    public void Increment(string key)
    {
        var counter = _dictionary.GetOrAdd(key, _ => new AtomicCounter());
        counter.Increment();  // Interlocked.Increment — всегда с первой попытки
    }

    public IEnumerator<KeyValuePair<string, int>> GetEnumerator()
    {
        foreach (var kvp in _dictionary)
            yield return new KeyValuePair<string, int>(kvp.Key, kvp.Value.Count);
    }
}
```

### Сравнение под нагрузкой

```
16 ключей × 2 writer-потока × 100,000 инкрементов = 3,200,000 операций

Подход 1 (AddOrUpdate):
  Каждый инкремент → lock bucket + CAS loop
  При contention на одном ключе → много CAS retries

Подход 2 (GetOrAdd + AtomicCounter):
  16 вызовов GetOrAdd (только при создании)
  3,200,000 вызовов Interlocked.Increment (без retry)
  Contention на словаре: практически нулевая после создания ключей
```

---

## Module 09 — Pipelines

**Файл:** `Module09-ChannelsPipelines/Pipelines/PipeLineCounter.cs`

### Задание
Подсчитать `\n` в сетевом потоке через System.IO.Pipelines.

### Решение

```csharp
public async Task<int> CountLines(Uri uri)
{
    using var client = new HttpClient();
    await using var stream = await client.GetStreamAsync(uri);

    var pipe = new Pipe();
    var writing = FillPipeAsync(stream, pipe.Writer);
    var reading = ReadPipeAsync(pipe.Reader);

    await Task.WhenAll(writing, reading);
    return await reading;
}

private static async Task FillPipeAsync(Stream stream, PipeWriter writer)
{
    const int minimumBufferSize = 512;
    while (true)
    {
        Memory<byte> memory = writer.GetMemory(minimumBufferSize);
        int bytesRead = await stream.ReadAsync(memory);
        if (bytesRead == 0) break;

        writer.Advance(bytesRead);
        FlushResult result = await writer.FlushAsync();
        if (result.IsCompleted) break;
    }
    await writer.CompleteAsync();
}

private static async Task<int> ReadPipeAsync(PipeReader reader)
{
    int lineCount = 0;
    while (true)
    {
        ReadResult result = await reader.ReadAsync();
        ReadOnlySequence<byte> buffer = result.Buffer;

        lineCount += CountNewLines(buffer);
        reader.AdvanceTo(buffer.End);

        if (result.IsCompleted) break;
    }
    await reader.CompleteAsync();
    return lineCount;
}

private static int CountNewLines(ReadOnlySequence<byte> buffer)
{
    var reader = new SequenceReader<byte>(buffer);
    int count = 0;
    while (reader.TryAdvanceTo((byte)'\n'))
        count++;
    return count;
}
```

### Поток данных

```
Network ──ReadAsync──▶ PipeWriter ──FlushAsync──▶ [Internal Buffer]
                                                        │
                                                   ReadAsync
                                                        │
                                                        ▼
                                                   PipeReader
                                                        │
                                                  SequenceReader
                                                  TryAdvanceTo('\n')
                                                        │
                                                        ▼
                                                   lineCount++
```

Writer и Reader работают **параллельно** через `Task.WhenAll`:
- Writer читает из сети и пишет в Pipe
- Reader читает из Pipe и считает `\n`
- Back-pressure: если Reader медленнее, `FlushAsync` приостановит Writer

---

# Часть III — Шпаргалки и паттерны

---

## Паттерн: Retry с Exponential Backoff

```csharp
async Task<T> RetryAsync<T>(Func<Task<T>> operation, int maxRetries, CancellationToken ct)
{
    int delay = 1000;
    for (int i = 0; i <= maxRetries; i++)
    {
        try { return await operation(); }
        catch when (i < maxRetries)
        {
            await Task.Delay(delay, ct);
            delay *= 2;
        }
    }
    throw new InvalidOperationException();
}
```

## Паттерн: First Wins (Race)

```csharp
async Task<T> RaceAsync<T>(IEnumerable<Task<T>> tasks, CancellationTokenSource cts)
{
    var taskList = tasks.ToList();
    while (taskList.Count > 0)
    {
        var winner = await Task.WhenAny(taskList);
        taskList.Remove(winner);
        try
        {
            var result = await winner;
            cts.Cancel();
            return result;
        }
        catch { if (taskList.Count == 0) throw; }
    }
    throw new InvalidOperationException();
}
```

## Паттерн: Callback → async/await (через TCS)

```csharp
Task<T> WrapCallbackAsync<T>(Action<Action<T>> callbackApi)
{
    var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    callbackApi(result => tcs.SetResult(result));
    return tcs.Task;
}
```

## Паттерн: Linked Cancellation (таймаут + внешняя отмена)

```csharp
using var timeoutCts = new CancellationTokenSource(timeout);
using var linked = CancellationTokenSource.CreateLinkedTokenSource(externalToken, timeoutCts.Token);
await operation(linked.Token);
```

## Паттерн: Lock-free counter

```csharp
int counter;
Interlocked.Increment(ref counter);
int snapshot = Volatile.Read(ref counter);
```

## Паттерн: IDisposable exclusive scope

```csharp
class ExclusiveScope : IDisposable
{
    readonly Mutex _m;
    public ExclusiveScope(string name)
    {
        _m = new Mutex(false, name);
        _m.WaitOne();
    }
    public void Dispose() { _m.ReleaseMutex(); _m.Dispose(); }
}
// Использование:
using (new ExclusiveScope("name")) { /* protected region */ }
```

## Паттерн: Pipe (Producer/Consumer)

```csharp
var pipe = new Pipe();
var producer = ProduceAsync(pipe.Writer);
var consumer = ConsumeAsync(pipe.Reader);
await Task.WhenAll(producer, consumer);
```

---

## Карта прогресса курса

```
Module 01 ✅  Introduction         → Benchmarking, алгоритмы, профилирование
       │
Module 02 ✅  Threading            → Thread, ThreadPool, синхронизация ожидания
       │
Module 03 ✅  Async Basics         → async/await, retry, CancellationToken
       │
Module 04 ✅  Async Part 2         → Custom Awaitable, TaskCompletionSource
       │
Module 05 ✅  Async Part 3         → Task.WhenAny, WhenAll, linked tokens
       │
Module 06 ✅  Low Level            → Interlocked, Volatile, модель памяти
       │
Module 07 ✅  Synchronization      → Mutex, Semaphore, system-wide locks
       │
Module 08 ✅  Concurrent Structs   → ConcurrentDictionary, atomic operations
       │
Module 09 ✅  Pipelines            → System.IO.Pipelines, SequenceReader
```

---

> **Все решения реализованы и лежат в соответствующих `.cs` файлах каждого модуля.**
