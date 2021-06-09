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
    using Microsoft.Extensions.Caching.Memory;
    using System;

    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class ServerController : ControllerBase
    {
        private readonly ILogger<ServerController> logger;
        private readonly ServerService serverService;
        private readonly PersistentService persistentService;
        private readonly IMemoryCache memoryCache;
        private readonly AppSettings appSettings;

        public ServerController(
            ILogger<ServerController> logger,
            ServerService serverService,
            PersistentService persistentService,
            IMemoryCache memoryCache,
            IOptions<AppSettings> appSettings)
        {
            this.logger = logger;
            this.serverService = serverService;
            this.persistentService = persistentService;
            this.memoryCache = memoryCache;
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
        public async Task<IActionResult> GetAllHarvesterDisks(bool force = false)
        {
            if (force || !memoryCache.TryGetValue(nameof(GetAllHarvesterDisks), out var disks))
            {
                disks = await this.serverService.GetHarvesterDisksInfo();
                memoryCache.Set(nameof(GetAllHarvesterDisks), disks, TimeSpan.FromMinutes(30));
            }

            return Ok(disks);
        }

        [HttpGet("serial-number")]
        [Authorize(nameof(UserRole.Admin))]
        public async Task<IActionResult> GetSerialNumber()
        {
            var s = @"A001	Z1Z1QWBG
A002	Z1Z4A7RS
A003	Z1Z2WHTW
A004	Z1Z47BWQ
A005	Z1Z2W424
A006	Z1Z4AT2D
A007	Z1Z35KPC
A008	Z1Z1R2E7
A009	Z1Z7Y1ZB
A010	Z1Z60NGF
A011	Z1Z604S7
A012	Z1Z2Y0TR
A013	Z1Z60EYM
A014	Z1Z5Z9D5
A015	Z1Z60JEA
A016	Z1Z4DA5P
A017	Z1Z799TD
A018	Z1Z4CZTG
A019	Z1Z6DTD3
A020	Z1Z6MS1X
A021	Z1Z6C8S8
A022	Z1Z8VEPV
A023	Z1Z4B739
A024	Z1Z11VDW
A025	Z1Z5Q98W
A026	Z1Z60KGP
A027	Z1Z5ZA7A
A028	Z1Z4BEM3
A029	Z1Z60LZ4
A030	Z1Z5Z9LV
A031	Z1Z5ZAGD
A032	Z1Z5ZAFH"
                .CleanSplit()
                .Select(_ => _.CleanSplit("\t"))
                .ToDictionary(_ => _[1], _ => _[0]);
            return Ok(s);
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

            var plan1 = serverService.GetOptimizePlotManPlan(plotters.Where(_ => _.Name.StartsWith("r720")).ToArray(), harvesters.Where(_ => _.Name.StartsWith("harvester_s")).ToArray());
            var plan2 = serverService.GetOptimizePlotManPlan(plotters.Where(_ => !_.Name.StartsWith("r720")).ToArray(), harvesters.Where(_ => !_.Name.StartsWith("harvester_s")).ToArray());
            var plan = plan1.Concat(plan2);
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

        [HttpPost("create-part")]
        public async Task<IActionResult> CreatePartition(string host, string block, string label)
        {
            var result = serverService.CreatePartition(host, block, label);
            if (result)
                return Ok();
            else
                return BadRequest();
        }

        [HttpDelete("temporary")]
        [Authorize(nameof(UserRole.Admin))]
        public async Task<IActionResult> CleanTemporary([FromBody] string[] names)
        {
            var result = await this.serverService.CleanLegacyTemporaryFile(names);
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
