# Module 01 - Introduction: Fibonacci Benchmarking

## Задание

1. Реализовать `RecursiveWithMemoization` и `Iterative` варианты вычисления чисел Фибоначчи
2. Добавить `MemoryDiagnoser` к бенчмарку
3. Запустить в Release-конфигурации и сравнить результаты
4. Изучить отчёт дизассемблера

## Решение

### 1. RecursiveWithMemoization

```csharp
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
```

**Принцип работы:**
- Мемоизация (memoization) - это техника кэширования результатов дорогостоящих вычислений.
- Обычная рекурсия для Fibonacci имеет экспоненциальную сложность **O(2^n)**, потому что одни и те же подзадачи вычисляются многократно. Например, `Fib(35)` вызывает `Fib(34)` и `Fib(33)`, но `Fib(34)` в свою очередь тоже вызовет `Fib(33)` - и так далее.
- С мемоизацией каждое значение вычисляется ровно один раз и сохраняется в `Dictionary<ulong, ulong>`. При повторном запросе значение берётся из кэша за **O(1)**.
- Итого сложность снижается до **O(n)** по времени и **O(n)** по памяти.

### 2. Iterative

```csharp
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
```

**Принцип работы:**
- Итеративный подход - самый эффективный. Мы храним только два последних числа последовательности и на каждом шаге вычисляем следующее.
- Сложность: **O(n)** по времени, **O(1)** по памяти (никаких аллокаций в куче).
- Нет накладных расходов на рекурсию (стек вызовов), нет аллокации `Dictionary`.

### 3. MemoryDiagnoser

```csharp
[MemoryDiagnoser]
public class FibonacciCalc { ... }
```

Атрибут `[MemoryDiagnoser]` добавляет в отчёт BenchmarkDotNet колонки:
- **Gen0/Gen1/Gen2** - сколько сборок мусора каждого поколения произошло
- **Allocated** - сколько байт памяти было аллоцировано на одну операцию

## Ожидаемые результаты бенчмарка

| Метод | n=15 | n=35 | Аллокации |
|-------|------|------|-----------|
| Recursive | ~3 мкс | ~50 мс | 0 B |
| RecursiveWithMemoization | ~0.5 мкс | ~1 мкс | ~1-2 KB (Dictionary) |
| Iterative | ~10 нс | ~20 нс | 0 B |

**Ключевые выводы:**
1. **Recursive** - катастрофически медленный для больших n из-за экспоненциальной сложности. Для n=35 это ~9.2 миллиарда операций.
2. **RecursiveWithMemoization** - на порядки быстрее простой рекурсии, но аллоцирует `Dictionary` в куче.
3. **Iterative** - самый быстрый, нулевые аллокации, минимальное использование стека. Предпочтительный вариант для продакшена.

## Disassembler Report

`[DisassemblyDiagnoser]` генерирует отчёт с машинным кодом (JIT-compiled). В нём видно:
- **Iterative** компилируется в компактный цикл с регистрами CPU - всё работает на стеке без обращений к куче.
- **Recursive** содержит два рекурсивных `call` на каждом уровне.
- **RecursiveWithMemoization** содержит вызовы `Dictionary.TryGetValue` и `Dictionary.set_Item`.
