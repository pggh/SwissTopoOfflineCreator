using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace SwissTopoOfflineCreator
{
    interface IDownloadRequest
    {
        string Uri { get; }
    }

    class BatchDownloader<R> : IDisposable where R : IDownloadRequest
    {

        public BatchDownloader(double maxRequestPerSec, int maxParallelRequests)
        {
            if (maxRequestPerSec < 0 || maxRequestPerSec > 1E3 || !double.IsFinite(maxRequestPerSec)) {
                maxRequestPerSec = 1E3;
            } else if (maxParallelRequests < 0.1) {
                maxRequestPerSec = 0.1;
            }
            if (maxRequestPerSec > 20) {
                limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions {
                    QueueLimit = maxParallelRequests,
                    TokenLimit = Math.Max(maxParallelRequests, Math.Clamp((int)Math.Round(10*maxRequestPerSec), 10, 1000)),
                    AutoReplenishment = true,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(0.1),
                    TokensPerPeriod = (int)Math.Round(maxRequestPerSec/10)
                });
            } else {
                limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions {
                    QueueLimit = maxParallelRequests,
                    TokenLimit = Math.Max(maxParallelRequests, Math.Clamp((int)Math.Round(10*maxRequestPerSec), 10, 1000)),
                    AutoReplenishment = true,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1.0 / maxRequestPerSec),
                    TokensPerPeriod = 1
                });
            }

            cancellationTokenSource = new CancellationTokenSource();
            cancellationToken = cancellationTokenSource.Token;
            for (int i = 0; i < maxParallelRequests; i++) {
                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SwissTopoOfflineCreator");
                idleHttpClients.Push(httpClient);
            }
            this.maxParallelRequests = maxParallelRequests;
        }

        public void Dispose()
        {
            Cancel();
        }

        public void Cancel()
        {
            cancellationTokenSource.Cancel();
        }

        public delegate void RequestCompleted(R request, byte[]? data, HttpStatusCode httpStatusCode, Exception? exception);

        public async Task Download(IEnumerable<R> requests, RequestCompleted requestCompleted)
        {
            using (var enumerator = requests.GetEnumerator()) {
                var running = new Dictionary<Task, R>();
                var runningHttpClients = new Dictionary<Task, HttpClient>();
                for (;;) {
                    while (running.Count < maxParallelRequests && enumerator.MoveNext()) {
                        await limiter.AcquireAsync(1, cancellationToken);
                        var request = enumerator.Current;
                        var httpClient = idleHttpClients.Pop();
                        var task = httpClient.GetAsync(request.Uri, HttpCompletionOption.ResponseContentRead, cancellationToken);
                        running[task] = request;
                        runningHttpClients[task] = httpClient;
                    }

                    if (running.Count == 0) { break; }
                    var completedTask = (Task<HttpResponseMessage>)await Task.WhenAny(running.Keys);
                    if (cancellationToken.IsCancellationRequested) {
                        foreach (var c in runningHttpClients.Values) {
                            idleHttpClients.Push(c);
                        }
                        break;
                    }
                    var completedRequest = running[completedTask];
                    running.Remove(completedTask);
                    idleHttpClients.Push(runningHttpClients[completedTask]);
                    runningHttpClients.Remove(completedTask);

                    if (completedTask.IsFaulted) {
                        requestCompleted(completedRequest, null, HttpStatusCode.Ambiguous, completedTask.Exception);
                    } else if (completedTask.Status == TaskStatus.RanToCompletion) {
                        var rr = completedTask.Result;
                        requestCompleted(completedRequest,
                            rr.StatusCode == HttpStatusCode.OK ? rr.Content.ReadAsByteArrayAsync(cancellationToken).Result : null,
                            rr.StatusCode,
                            null);
                    }
                }
            }
        }

        private readonly TokenBucketRateLimiter limiter;
        private readonly Stack<HttpClient> idleHttpClients = new Stack<HttpClient>();
        private readonly CancellationTokenSource cancellationTokenSource;
        private readonly CancellationToken cancellationToken;
        private readonly int maxParallelRequests;
    }
}
