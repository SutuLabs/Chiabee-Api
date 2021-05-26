namespace WebApi.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Authorization;
    using WebApi.Services;
    using System.Threading.Tasks;
    using WebApi.Models;
    using System.Linq;
    using Microsoft.Extensions.Options;
    using Microsoft.Extensions.Logging;
    using Microsoft.Azure.Cosmos.Table;
    using WebApi.Entities;
    using WebApi.Services.ServerCommands;
    using Newtonsoft.Json;

    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class ServerController : ControllerBase
    {
        private readonly ILogger<ServerController> logger;
        private readonly ServerService serverService;
        private readonly AppSettings appSettings;
        private readonly CloudTable table;

        public ServerController(
            ILogger<ServerController> logger,
            ServerService serverService,
            IOptions<AppSettings> appSettings)
        {
            this.logger = logger;
            this.serverService = serverService;
            this.appSettings = appSettings.Value;

            var storageAccount = CloudStorageAccount.Parse(this.appSettings.ConnectionString);
            var tableClient = storageAccount.CreateCloudTableClient(new TableClientConfiguration());
            this.table = tableClient.GetTableReference(this.appSettings.LogTablePrefix + DataRefreshService.LatestStateTableName);
        }

        [HttpGet("servers")]
        public async Task<IActionResult> GetServersInfo()
        {
            var entity = await RetrieveEntityAsync<MachineStateEntity>(
                this.table, MachineStateEntity.DefaultPartitionKey, DataRefreshService.LatestStateKeyName);
            if (entity == null) return NoContent();
            var info = JsonConvert.DeserializeObject<ServerStatus[]>(entity.MachinesJson);
            return Ok(info);
        }

        [HttpGet("plotter")]
        public async Task<IActionResult> GetPlotterInfo()
        {
            var entity = await RetrieveEntityAsync<FarmStateEntity>(
                this.table, FarmStateEntity.DefaultPartitionKey, DataRefreshService.LatestStateKeyName);
            if (entity == null) return NoContent();
            var info = JsonConvert.DeserializeObject<PlotterStatus[]>(entity.PlotterJson);
            return Ok(info);
        }

        [HttpGet("farmer")]
        public async Task<IActionResult> GetFarmerInfo()
        {
            var entity = await RetrieveEntityAsync<FarmStateEntity>(
                this.table, FarmStateEntity.DefaultPartitionKey, DataRefreshService.LatestStateKeyName);
            if (entity == null) return NoContent();
            var info = JsonConvert.DeserializeObject<FarmerStatus[]>(entity.FarmerJson);
            return Ok(info);
        }

        [HttpGet("errors")]
        public async Task<IActionResult> GetFarmerErrorInfo()
        {
            var info = serverService.errorList.Select(_ => _.Value).ToArray();
            return Ok(info);
        }

        [HttpGet("events")]
        public async Task<IActionResult> GetFarmerEventInfo()
        {
            var info = serverService.eventList.ToArray();
            return Ok(info);
        }

        private async Task<T> RetrieveEntityAsync<T>(CloudTable table, string partitionKey, string rowKey)
            where T : class, ITableEntity
        {
            TableOperation retrieve = TableOperation.Retrieve<T>(partitionKey, rowKey);
            var result = await table.ExecuteAsync(retrieve);
            return result.Result as T;
        }
    }
}
