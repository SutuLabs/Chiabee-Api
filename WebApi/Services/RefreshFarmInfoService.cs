namespace WebApi.Services
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using WebApi.Entities;
    using WebApi.Models;

    internal class RefreshFarmInfoService : BaseRefreshService
    {
        private readonly PersistentService persistentService;
        private readonly ServerService server;
        private readonly AppSettings appSettings;

        public RefreshFarmInfoService(
            ILogger<RefreshFarmInfoService> logger,
            IServiceProvider serviceProvider,
            PersistentService persistentService,
            ServerService server,
            IOptions<AppSettings> appSettings)
            : base(logger, serviceProvider)
        {
            this.persistentService = persistentService;
            this.server = server;
            this.appSettings = appSettings.Value;
        }

        protected override string ServiceName => nameof(RefreshFarmInfoService);
        protected override int DefaultIntervalSeconds => 120;
        protected override int DelayStartSeconds => 5;

        protected override int GetIntervalSeconds() => 20;

        protected override async Task DoWorkAsync()
        {
            var pi = JsonSerializer.Serialize(await this.server.GetPlotterInfo());
            var fi = JsonSerializer.Serialize(await this.server.GetFarmerInfo());
            await this.persistentService.LogEntityAsync(new FarmStateEntity { PlotterJson = pi, FarmerJson = fi });
        }
    }
}