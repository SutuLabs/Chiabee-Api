namespace WebApi.Services
{
    using System.Diagnostics;
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
            var sw = new Stopwatch();
            sw.Start();
            var pi = JsonSerializer.Serialize(await this.server.GetPlotterInfo());
            var pms = sw.ElapsedMilliseconds;
            var fi = JsonSerializer.Serialize(await this.server.GetFarmerInfo());
            var fms = sw.ElapsedMilliseconds - pms;
            var hi = JsonSerializer.Serialize(await this.server.GetHarvesterInfo());
            var hms = sw.ElapsedMilliseconds - fms - pms;

            var entity = new FarmStateEntity { PlotterJsonGzip = pi.Compress(), FarmerJsonGzip = fi.Compress(), HarvesterJsonGzip = hi.Compress() };
            await this.persistentService.LogEntityAsync(entity);
            sw.Stop();
            this.logger.LogInformation($"Work time: plotter info = {pms}ms, farmer info = {fms}ms, harvester info = {hms}ms, total = {sw.ElapsedMilliseconds}ms");
        }
    }
}