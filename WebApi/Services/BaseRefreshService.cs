namespace WebApi.Services
{
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    public abstract class BaseRefreshService : IHostedService, IDisposable
    {
        private readonly ILogger logger;
        private readonly IServiceProvider serviceProvider;
        private Timer timer;
        private bool isRunning;

        public BaseRefreshService(ILogger<BaseRefreshService> logger, IServiceProvider serviceProvider)
        {
            this.logger = logger;
            this.serviceProvider = serviceProvider;
        }

        protected abstract string ServiceName { get; }
        protected abstract int DefaultIntervalSeconds { get; }
        protected abstract int DelayStartSeconds { get; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var seconds = GetIntervalSeconds();
            if (seconds == 0) return Task.CompletedTask;

            seconds = seconds < 0 ? this.DefaultIntervalSeconds : seconds;

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

        protected abstract int GetIntervalSeconds();

        private async void LoopDoWork(object state)
        {
            if (this.isRunning) return;
            try
            {
                this.isRunning = true;
                await DoWorkAsync();
                logger.LogInformation($"{ServiceName} refreshed");
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