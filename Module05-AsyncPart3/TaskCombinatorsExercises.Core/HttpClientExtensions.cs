using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TaskCombinatorsExercises.Core
{
    public static class HttpClientExtensions
    {
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
    }
}
