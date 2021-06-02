namespace WebApi.Services
{
    using System.Text.Json;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using WebApi.Entities;
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
            : base(logger, nameof(RefreshServerInfoService), 3, 5)
        {
            this.persistentService = persistentService;
            this.server = server;
            this.appSettings = appSettings.Value;
        }

        protected override async Task DoWorkAsync()
        {
            var si = JsonSerializer.Serialize(await this.server.GetServersInfo());
            await this.persistentService.LogEntityAsync(new MachineStateEntity { MachinesJson = si });
        }
    }
}