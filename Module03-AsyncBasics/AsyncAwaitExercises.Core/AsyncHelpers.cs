using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncAwaitExercises.Core
{
    public class AsyncHelpers
    {
        public static async Task<string> GetStringWithRetries(HttpClient client, string url, int maxTries = 3, CancellationToken token = default)
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

            // This path is unreachable because the last attempt will throw,
            // but the compiler requires it. The for loop's last iteration
            // doesn't catch the exception (attempt == maxTries), so it propagates.
            throw new InvalidOperationException("Unreachable");
        }
    }
}
