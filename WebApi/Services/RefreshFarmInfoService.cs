namespace WebApi.Services
{
    using System.Text.Json;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using WebApi.Entities;
    using WebApi.Helpers;
    using WebApi.Models;

    internal class RefreshFarmInfoService : BaseRefreshService
    {
        private readonly PersistentService persistentService;
        private readonly ServerService server;
        private readonly AppSettings appSettings;

        public RefreshFarmInfoService(
            ILogger<RefreshFarmInfoService> logger,
            PersistentService persistentService,
            ServerService server,
            IOptions<AppSettings> appSettings)
            : base(logger, nameof(RefreshFarmInfoService), 5, 20)
        {
            this.persistentService = persistentService;
            this.server = server;
            this.appSettings = appSettings.Value;
        }

        protected override async Task DoWorkAsync()
        {
            var pi = JsonSerializer.Serialize(await this.server.GetPlotterInfo());
            var fi = JsonSerializer.Serialize(await this.server.GetFarmerInfo());
            var entity = new FarmStateEntity { PlotterJsonGzip = pi.Compress(), FarmerJsonGzip = fi.Compress() };
            try
            {
                await this.persistentService.LogEntityAsync(entity);
            }
            catch (Microsoft.Azure.Cosmos.Table.StorageException sex)
            {
                this.logger.LogWarning(sex, $"failed to store, PJ Length: {entity.PlotterJsonGzip.Length}, FJ Length: {entity.FarmerJsonGzip.Length}");
            }
        }
    }
}