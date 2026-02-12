# Module 08 - Concurrent Data Structures: Metrics Counters

## Задание

Реализовать два варианта потокобезопасного счётчика метрик:
1. **ConcurrentDictionaryOnlyMetricsCounter** - только через API `ConcurrentDictionary`
2. **ConcurrentDictionaryWithCounterMetricsCounter** - через `ConcurrentDictionary` + `AtomicCounter`

---

## Решение 1: ConcurrentDictionaryOnly

```csharp
public class ConcurrentDictionaryOnlyMetricsCounter : IMetricsCounter
{
    private readonly ConcurrentDictionary<string, int> _dictionary = new();

    public void Increment(string key)
    {
        _dictionary.AddOrUpdate(key, 1, (_, oldValue) => oldValue + 1);
    }

    public IEnumerator<KeyValuePair<string, int>> GetEnumerator()
    {
        return _dictionary.GetEnumerator();
    }
}
```

### Разбор AddOrUpdate

```csharp
_dictionary.AddOrUpdate(key, 1, (_, oldValue) => oldValue + 1);
```

`AddOrUpdate` - атомарная операция `ConcurrentDictionary`:
- Если ключ **не существует** → добавляет с значением `1` (второй аргумент)
- Если ключ **существует** → вызывает `updateValueFactory`: `(key, oldValue) => oldValue + 1`

**Важно:** `updateValueFactory` может быть вызван **несколько раз** при конкурентном доступе (optimistic concurrency). Внутри `AddOrUpdate` используется цикл с `CompareExchange`:

```
1. Читаем текущее значение
2. Вычисляем новое через factory
3. Пытаемся записать через CAS (Compare-And-Swap)
4. Если кто-то изменил значение между 1 и 3 → повторяем с шага 1
```

Поэтому factory **не должна** иметь побочных эффектов.

---

## Решение 2: ConcurrentDictionary + AtomicCounter

```csharp
public class ConcurrentDictionaryWithCounterMetricsCounter : IMetricsCounter
{
    private readonly ConcurrentDictionary<string, AtomicCounter> _dictionary = new();

    public void Increment(string key)
    {
        var counter = _dictionary.GetOrAdd(key, _ => new AtomicCounter());
        counter.Increment();
    }

    public IEnumerator<KeyValuePair<string, int>> GetEnumerator()
    {
        foreach (var kvp in _dictionary)
        {
            yield return new KeyValuePair<string, int>(kvp.Key, kvp.Value.Count);
        }
    }
}
```

### Разбор GetOrAdd + AtomicCounter

```csharp
var counter = _dictionary.GetOrAdd(key, _ => new AtomicCounter());
counter.Increment();  // Interlocked.Increment(ref value)
```

Подход отличается от первого:
1. **`GetOrAdd`** - получить существующий `AtomicCounter` или создать новый. Factory `_ => new AtomicCounter()` может быть вызвана несколько раз при конкурентном доступе, но `ConcurrentDictionary` гарантирует, что **только один** объект будет сохранён.
2. **`counter.Increment()`** - использует `Interlocked.Increment` для атомарного инкремента. Это **всегда** успешно с первой попытки (no retry loop).

### AtomicCounter

```csharp
public class AtomicCounter
{
    int value;
    public void Increment() => Interlocked.Increment(ref value);
    public int Count => Volatile.Read(ref value);
}
```

- `Interlocked.Increment` - атомарный `value++` (CPU-инструкция `lock inc`)
- `Volatile.Read` - чтение с memory barrier для видимости последних записей

---

## Сравнение двух подходов

| Критерий | AddOrUpdate | GetOrAdd + AtomicCounter |
|----------|-------------|------------------------|
| Retry при contention | Да (CAS loop) | Только при первом создании ключа |
| Аллокации | Минимальные | Один объект AtomicCounter на ключ |
| Производительность при частом инкременте | Может деградировать (CAS retries) | Стабильная (Interlocked) |
| Сложность | Проще | Немного сложнее |

**Второй подход лучше для высоконагруженных счётчиков**, потому что:
- `Interlocked.Increment` не требует retry loop
- Contention на `ConcurrentDictionary` происходит только при первом создании ключа
- После создания ключа все инкременты идут мимо словаря - прямо в `AtomicCounter`

### Тест

Тест запускает по 2 потока на 16 ключей, каждый поток делает 100,000 инкрементов. Итого 200,000 на ключ. Тест проверяет:
- Каждый ключ имеет ровно 200,000
- Нет лишних/пропущенных ключей

Это хороший stress-test на корректность потокобезопасных операций.
