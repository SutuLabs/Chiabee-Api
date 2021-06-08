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
    using WebApi.Helpers;

    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class ServerController : ControllerBase
    {
        private readonly ILogger<ServerController> logger;
        private readonly ServerService serverService;
        private readonly PersistentService persistentService;
        private readonly AppSettings appSettings;

        public ServerController(
            ILogger<ServerController> logger,
            ServerService serverService,
            PersistentService persistentService,
            IOptions<AppSettings> appSettings)
        {
            this.logger = logger;
            this.serverService = serverService;
            this.persistentService = persistentService;
            this.appSettings = appSettings.Value;

        }

        [HttpGet("servers")]
        public async Task<IActionResult> GetServersInfo()
        {
            var entity = await this.persistentService.RetrieveEntityAsync<MachineStateEntity>();
            if (entity == null) return NoContent();
            var json = entity.MachinesJsonGzip?.Decompress();
            if (string.IsNullOrEmpty(json)) return NoContent();
            var info = JsonConvert.DeserializeObject<ServerStatus[]>(json);
            return Ok(info);
        }

        [HttpGet("plotter")]
        public async Task<IActionResult> GetPlotterInfo()
        {
            var entity = await this.persistentService.RetrieveEntityAsync<FarmStateEntity>();
            if (entity == null) return NoContent();
            var json = entity.PlotterJsonGzip?.Decompress();
            if (string.IsNullOrEmpty(json)) return NoContent();
            var info = JsonConvert.DeserializeObject<PlotterStatus[]>(json);
            return Ok(info);
        }

        [HttpDelete("plot")]
        [Authorize(nameof(UserRole.Admin))]
        public async Task<IActionResult> StopPlot(string name, string id)
        {
            var result = await this.serverService.StopPlot(name, id);
            if (result)
                return Ok();
            else
                return BadRequest();
        }

        [HttpGet("plots")]
        [Authorize(nameof(UserRole.Admin))]
        public async Task<IActionResult> GetAllPlots()
        {
            var result = await this.serverService.GetPlotsInfo();
            return Ok(result);
        }

        [HttpGet("disks")]
        [Authorize(nameof(UserRole.Admin))]
        public async Task<IActionResult> GetAllHarvesterDisks()
        {
            var result = await this.serverService.GetHarvesterDisksInfo();
            return Ok(result);
        }

        [HttpGet("farmer")]
        public async Task<IActionResult> GetFarmerInfo()
        {
            var entity = await this.persistentService.RetrieveEntityAsync<FarmStateEntity>();
            if (entity == null) return NoContent();
            var json = entity.FarmerJsonGzip?.Decompress();
            if (string.IsNullOrEmpty(json)) return NoContent();
            var info = JsonConvert.DeserializeObject<FarmerNodeStatus[]>(json);
            return Ok(info);
        }

        [HttpGet("plotplan")]
        public async Task<IActionResult> GetPlotManPlan()
        {
            PlotterStatus[] plotters;
            ServerStatus[] harvesters;

            {
                var entity = await this.persistentService.RetrieveEntityAsync<FarmStateEntity>();
                if (entity == null) return NoContent();
                var json = entity.PlotterJsonGzip?.Decompress();
                if (string.IsNullOrEmpty(json)) return NoContent();
                plotters = JsonConvert.DeserializeObject<PlotterStatus[]>(json);
            }

            {
                var entity = await this.persistentService.RetrieveEntityAsync<MachineStateEntity>();
                if (entity == null) return NoContent();
                var json = entity.MachinesJsonGzip?.Decompress();
                if (string.IsNullOrEmpty(json)) return NoContent();
                harvesters = JsonConvert.DeserializeObject<ServerStatus[]>(json);
                harvesters = harvesters
                    .Where(_ => _.Name.StartsWith("harvester"))
                    .ToArray();
            }

            var plan = serverService.GetOptimizePlotManPlan(plotters, harvesters);
            return Ok(plan);
        }

        [HttpPost("plotplan")]
        public async Task<IActionResult> SetPlotManPlan([FromBody] OptimizedPlotManPlan[] plans)
        {
            var result = serverService.SetOptimizePlotManPlan(plans);
            if (result)
                return Ok();
            else
                return BadRequest();
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
    }
}
