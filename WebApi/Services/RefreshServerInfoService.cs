namespace WebApi.Services
{
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using WebApi.Entities;
    using WebApi.Helpers;
    using WebApi.Models;

    internal class RefreshServerInfoService : BaseRefreshService
    {
        private readonly PersistentService persistentService;
        private readonly ServerService server;
        private readonly AppSettings appSettings;

        public RefreshServerInfoService(
            ILogger<RefreshServerInfoService> logger,
            PersistentService persistentService,
            ServerService server,
            IOptions<AppSettings> appSettings)
            : base(logger, nameof(RefreshServerInfoService), 3, 5, 45)
        {
            this.persistentService = persistentService;
            this.server = server;
            this.appSettings = appSettings.Value;
        }

        protected override async Task DoWorkAsync(CancellationToken token)
        {
            var si = JsonSerializer.Serialize(await this.server.GetServersInfo());
            if (token.IsCancellationRequested)
            {
                this.logger.LogInformation($"Refresh work cancelled.");
            }
            else
            {
                await this.persistentService.LogEntityAsync(new MachineStateEntity { MachinesJsonGzip = si.Compress() });
            }
        }
    }
}