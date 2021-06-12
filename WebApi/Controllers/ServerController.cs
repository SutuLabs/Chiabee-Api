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

        [HttpGet("harvester")]
        public async Task<IActionResult> GetHarvesterInfo()
        {
            var entity = await this.persistentService.RetrieveEntityAsync<FarmStateEntity>();
            if (entity == null) return NoContent();
            var json = entity.HarvesterJsonGzip?.Decompress();
            if (string.IsNullOrEmpty(json)) return NoContent();
            var info = JsonConvert.DeserializeObject<HarvesterStatus[]>(json);
            return Ok(info);
        }

        [HttpGet("prices")]
        public async Task<IActionResult> GetPrices()
        {
            var entity = await this.persistentService.RetrieveEntityAsync<PriceStateEntity>();
            if (entity == null) return NoContent();
            var json = entity.PricesJson;
            if (string.IsNullOrEmpty(json)) return NoContent();
            var info = JsonConvert.DeserializeObject<PriceEntity[]>(json);
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
        public async Task<IActionResult> GetAllPlots(bool force = false)
        {
            if (force || !memoryCache.TryGetValue(nameof(GetAllPlots), out var plots))
            {
                plots = await this.serverService.GetPlotsInfo();
                memoryCache.Set(nameof(GetAllPlots), plots, TimeSpan.FromMinutes(10));
            }

            return Ok(plots);
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
A032	Z1Z5ZAFH
A033	Z1Z4ATYA
A034	Z1Z8VRTX
A035	Z1Z60J76
A036	Z1Z2WPKP
A037	Z1Z5NGXV
A038	Z1Z2X0YA
A039	Z1Z2YFG2
A040	Z1Z4GABS
A041	Z1Z4AA1Y
A042	Z1Z60MB8
A043	Z1Z5EV7F
A044	Z1Z13M47
A045	Z1Z1QQ54
A046	Z1Z60LCJ
A047	Z1Z60544
A048	Z1Z60KKK
A049	Z1Z4BSD0
A050	Z1Z1RKQF
A051	Z1Z4AFT3
A052	Z1Z4B25H
A053	Z1Z60685
A054	Z1Z30PBY
A055	Z1Z60L5F
A056	Z1Z6081W
A057	Z1Z8VN52
A058	Z1Z8WCJ2
A059	Z1Z2ZF9P
A060	Z1Z2X13C
A061	Z1Z34LMT
A062	Z1Z2YFHY
A063	Z1Z4GA4F
A064	Z1Z5ZAYX
A065	Z1Z60JH2
A066	Z1Z60L9N
A067	ZA4H832K
A068	ZA4H834D
A069	ZA4H833N
A070	ZA4H8340
A071	58E3KWDHFMYB
A072	58KZKDOSFMYB
A073	58COKCBLFMYB
A074	58EZKDCUFMYB
A075	58DDKEOQFMYB
A076	58CWKGJ0FMYB
A077	58CFKVMFFMYB
A078	28DAK2TDFMYB
A079	58IEK9P2FMYB
A080	58FSKKD3FMYB
A081	58DEK9FNFMYB
A082	28D7KAE3FMYB
A083	58KZKDSMFMYB
A084	58KDKFKZFMYB
A085	58F6KP6WFMYB
A086	ZC11JEE0
A087	ZC18KLZ7
A088	ZC13G52L
A089	ZC138DM0
A090	ZC13GM0K
A091	ZC10X4G2
A092	ZC18KM0S
A093	ZC13MCDP
A094	ZC13EK1X
A095	ZC13GTZK
A096	Z1Z15EAJ
A097	Z1Z8XZLQ
A098	58EYKFPHFMYB
A099	58C5KO5JFMYB
A100	Z1Z8VDPZ
A101	Z1Z4GRJR
A102	58IDKF0DFMYB
A103	58D5KO8NFMYB"
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

        [HttpPost("mount")]
        [Authorize(nameof(UserRole.Admin))]
        public async Task<IActionResult> MountAll([FromBody] string[] names)
        {
            var result = await this.serverService.MountAll(names);
            if (result)
                return Ok();
            else
                return BadRequest();
        }

        [HttpPost("daemons/plotters")]
        [Authorize(nameof(UserRole.Admin))]
        public async Task<IActionResult> PlotterDaemons([FromBody] string[] names)
        {
            var result = await this.serverService.PlotterDaemons(names);
            if (result)
                return Ok();
            else
                return BadRequest();
        }

        [HttpPost("daemons/harvesters")]
        [Authorize(nameof(UserRole.Admin))]
        public async Task<IActionResult> HarvesterDaemons([FromBody] string[] names)
        {
            var result = await this.serverService.HarvesterDaemons(names);
            if (result)
                return Ok();
            else
                return BadRequest();
        }
    }
}
