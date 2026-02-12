# Module 09 - Channels & Pipelines: PipeLineCounter

## Задание

Подсчитать количество символов `\n` в сетевом потоке, используя `System.IO.Pipelines` с паттерном Writer/Reader и `SequenceReader<T>`.

---

## Решение

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
```

### Writer: FillPipeAsync

```csharp
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
```

### Reader: ReadPipeAsync

```csharp
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
```

### Counter: CountNewLines

```csharp
private static int CountNewLines(ReadOnlySequence<byte> buffer)
{
    var reader = new SequenceReader<byte>(buffer);
    int count = 0;

    while (reader.TryAdvanceTo((byte)'\n'))
        count++;

    return count;
}
```

---

## Как работает System.IO.Pipelines

### Проблема традиционного Stream API

При работе с `Stream` в .NET есть фундаментальные проблемы:
1. Нужно выделять `byte[]` буферы (аллокации в куче → GC pressure)
2. Данные могут приходить порциями, не совпадающими с логическими границами
3. Ручное управление позицией чтения

### Pipe = высокопроизводительный буфер

`Pipe` - это связующее звено между Writer и Reader:

```
Network Stream → PipeWriter → [Ring Buffer] → PipeReader → Processing
```

Ключевые преимущества:
- **Нулевые аллокации**: `GetMemory()` возвращает `Memory<byte>` из внутреннего пула буферов
- **Back-pressure**: Если Reader не успевает, `FlushAsync()` приостанавливает Writer
- **Конкурентность**: Writer и Reader работают **одновременно** (`Task.WhenAll`)

### PipeWriter (заполняет буфер)

```csharp
Memory<byte> memory = writer.GetMemory(512);    // 1. Запрашиваем буфер
int bytesRead = await stream.ReadAsync(memory);  // 2. Читаем из сети прямо в буфер
writer.Advance(bytesRead);                        // 3. Сообщаем сколько байт записали
await writer.FlushAsync();                        // 4. Делаем данные доступными для Reader
```

Последовательность: GetMemory → ReadAsync → Advance → FlushAsync

### PipeReader (обрабатывает данные)

```csharp
ReadResult result = await reader.ReadAsync();    // 1. Ждём новых данных
ReadOnlySequence<byte> buffer = result.Buffer;   // 2. Получаем буфер
lineCount += CountNewLines(buffer);              // 3. Обрабатываем
reader.AdvanceTo(buffer.End);                    // 4. Сообщаем что данные прочитаны
```

### ReadOnlySequence<byte> и SequenceReader<byte>

`ReadOnlySequence<byte>` - это цепочка сегментов памяти (linked list of `Memory<byte>`). Данные могут быть **не в одном куске** - Pipe использует несколько буферов.

`SequenceReader<byte>` - высокопроизводительный ридер для `ReadOnlySequence`:

```csharp
var reader = new SequenceReader<byte>(buffer);
while (reader.TryAdvanceTo((byte)'\n'))
    count++;
```

`TryAdvanceTo((byte)'\n')`:
- Ищет первое вхождение `\n` в последовательности
- Продвигает позицию ридера **за** найденный байт
- Возвращает `false` когда `\n` больше не найден
- Эффективно работает с сегментированной памятью (не нужно копировать в один массив)

### Конкурентная обработка

```csharp
var writing = FillPipeAsync(stream, pipe.Writer);
var reading = ReadPipeAsync(pipe.Reader);
await Task.WhenAll(writing, reading);
```

Writer и Reader работают **параллельно**:
- Writer заполняет буфер из сети
- Reader обрабатывает доступные данные
- Pipe координирует их через внутреннюю синхронизацию

```
Время →
Writer: [read 512B][flush][read 512B][flush][read 256B][complete]
Reader:        [read][count][read][count][read][count][complete]
```

---

## Зачем Pipelines в реальных проектах?

- **Kestrel** (веб-сервер ASP.NET Core) построен на Pipelines
- Обработка сетевых протоколов (HTTP/2, WebSocket)
- Парсинг больших файлов без загрузки в память
- Любая поточная обработка данных с back-pressure
