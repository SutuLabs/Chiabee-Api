namespace WebApi.Services
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;

    public abstract class BaseRefreshService : IHostedService, IDisposable
    {
        private readonly ILogger logger;
        private Timer timer;
        private bool isRunning;

        public BaseRefreshService(ILogger<BaseRefreshService> logger, string serviceName, int delayStartSeconds, int intervalSeconds)
        {
            this.logger = logger;
            this.ServiceName = serviceName;
            this.DelayStartSeconds = delayStartSeconds;
            this.IntervalSeconds = intervalSeconds;
        }

        public string ServiceName { get; }
        public int DelayStartSeconds { get; }
        public int IntervalSeconds { get; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var seconds = this.IntervalSeconds;
            if (seconds == 0) return Task.CompletedTask;

            if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(IntervalSeconds), "must > 0");

            logger.LogInformation($"{ServiceName} refresh Service is starting, set to refresh per [{seconds}]s");

            timer = new Timer(LoopDoWork, null, TimeSpan.FromSeconds(DelayStartSeconds), TimeSpan.FromSeconds(seconds));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogDebug($"{ServiceName} refresh service is stopping.");

            timer?.Change(Timeout.Infinite, 0);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            timer?.Dispose();
        }

        protected abstract Task DoWorkAsync();

        private async void LoopDoWork(object state)
        {
            if (this.isRunning) return;
            try
            {
                this.isRunning = true;
                var sw = new Stopwatch();
                sw.Start();
                await DoWorkAsync();
                sw.Stop();
                logger.LogInformation($"{ServiceName} refreshed, {sw.ElapsedMilliseconds}ms elapsed.");
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "error when doing work");
            }
            finally
            {
                this.isRunning = false;
            }
        }
    }
}