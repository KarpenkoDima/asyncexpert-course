# Module 06 - Low Level: Lock-Free AverageMetric

## Задание

Переписать `AverageMetric` с `lock`-блокировки на lock-free реализацию с использованием `Interlocked` и `Volatile`.

---

## Было (с lock)

```csharp
readonly object sync = new object();
int sum = 0;
int count = 0;

public void Report(int value)
{
    lock (sync)
    {
        sum += value;
        count += 1;
    }
}

public double Average
{
    get
    {
        lock (sync) { return Calculate(count, sum); }
    }
}
```

## Стало (lock-free)

```csharp
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
```

---

## Разбор

### Interlocked - атомарные операции

**Проблема:** Операция `sum += value` в машинном коде состоит из 3 шагов:
1. Прочитать `sum` из памяти в регистр
2. Прибавить `value`
3. Записать результат обратно в `sum`

Если два потока выполняют это одновременно, один может перезаписать результат другого (lost update).

**Решение:** `Interlocked.Add(ref sum, value)` выполняет все 3 шага **атомарно** на уровне CPU (инструкция `lock xadd` на x86). Никакой другой поток не может "вклиниться" между чтением и записью.

```csharp
Interlocked.Add(ref sum, value);      // атомарный sum += value
Interlocked.Increment(ref count);     // атомарный count++
```

### Volatile - барьеры памяти

**Проблема:** Компилятор и CPU могут **переупорядочивать** чтения/записи для оптимизации. Один поток может видеть "устаревшее" значение переменной, закэшированное в регистре CPU.

**Решение:** `Volatile.Read` гарантирует:
- Значение читается **из памяти**, а не из кэша регистров
- Все предыдущие записи других потоков "видимы" на момент чтения (acquire fence)

```csharp
var currentCount = Volatile.Read(ref count);
var currentSum = Volatile.Read(ref sum);
```

### Почему это "приемлемо неточно"?

В lock-free версии `Report()` выполняет **две** атомарные операции, а не одну. Между `Interlocked.Add` и `Interlocked.Increment` другой поток может прочитать `Average` и получить несогласованное состояние:

```
Поток A: Interlocked.Add(ref sum, 100)
                                            Поток B: Average → sum=100, count=0 → NaN!
Поток A: Interlocked.Increment(ref count)
```

Задание явно говорит: "assume that we can return value estimated on a bit stale data". Для метрик/статистики небольшая неточность допустима, а выигрыш в производительности - значителен.

---

## Lock vs Lock-free: сравнение

| Характеристика | `lock` | `Interlocked`/`Volatile` |
|---|---|---|
| Гарантии | Полная согласованность | Отдельные операции атомарны |
| Производительность | Контекстное переключение при contention | Аппаратные инструкции CPU |
| Deadlock | Возможен | Невозможен |
| Сложность кода | Простой | Требует понимания модели памяти |
| Масштабируемость | Плохая при высокой contention | Хорошая |

### Когда lock-free оправдан?

- Высокая нагрузка (тысячи операций в секунду)
- Допустима небольшая неточность чтения
- Операции простые (инкремент, чтение)
- Нет сложных инвариантов между несколькими переменными

В тесте: 8 потоков × 1М операций каждый. На `lock`-версии потоки будут постоянно ждать друг друга. На lock-free - работают параллельно.
