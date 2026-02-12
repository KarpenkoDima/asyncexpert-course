using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Http;
using System.Threading.Tasks;

namespace Pipelines
{
    public class PipeLineCounter
    {
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

        private static async Task FillPipeAsync(System.IO.Stream stream, PipeWriter writer)
        {
            const int minimumBufferSize = 512;

            while (true)
            {
                var memory = writer.GetMemory(minimumBufferSize);

                int bytesRead = await stream.ReadAsync(memory);
                if (bytesRead == 0)
                    break;

                writer.Advance(bytesRead);

                var result = await writer.FlushAsync();
                if (result.IsCompleted)
                    break;
            }

            await writer.CompleteAsync();
        }

        private static async Task<int> ReadPipeAsync(PipeReader reader)
        {
            int lineCount = 0;

            while (true)
            {
                var result = await reader.ReadAsync();
                var buffer = result.Buffer;

                lineCount += CountNewLines(buffer);

                reader.AdvanceTo(buffer.End);

                if (result.IsCompleted)
                    break;
            }

            await reader.CompleteAsync();
            return lineCount;
        }

        private static int CountNewLines(ReadOnlySequence<byte> buffer)
        {
            var reader = new SequenceReader<byte>(buffer);
            int count = 0;

            while (reader.TryAdvanceTo((byte)'\n'))
            {
                count++;
            }

            return count;
        }
    }
}
