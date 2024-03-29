﻿namespace WebApi.Controllers
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
    using Microsoft.AspNetCore.Http;
    using System.IO;

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
        [AllowAnonymous]
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
        [Authorize(nameof(UserRole.Operator))]
        public async Task<IActionResult> StopPlot(string name, string id)
        {
            var result = await this.serverService.StopPlot(name, id);
            if (result)
                return Ok();
            else
                return BadRequest();
        }

        [HttpGet("plots")]
        [Authorize(nameof(UserRole.Operator))]
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
        [Authorize(nameof(UserRole.Operator))]
        public async Task<IActionResult> GetAllHarvesterDisks(bool force = false)
        {
            if (force || !memoryCache.TryGetValue(nameof(GetAllHarvesterDisks), out var disks))
            {
                disks = await this.serverService.GetHarvesterDisksInfo();
                memoryCache.Set(nameof(GetAllHarvesterDisks), disks, TimeSpan.FromMinutes(30));
            }

            return Ok(disks);
        }

        [HttpGet("disk/{name}")]
        [Authorize(nameof(UserRole.Operator))]
        public async Task<IActionResult> GetHarvesterDisk(string name)
        {
            var disk = await this.serverService.GetHarvesterDisksInfo(name);

            return Ok(disk);
        }

        [HttpGet("serial-number")]
        [Authorize(nameof(UserRole.Operator))]
        public async Task<IActionResult> GetSerialNumber()
        {
            if (!memoryCache.TryGetValue(nameof(GetSerialNumber), out var s))
            {
                s = await this.serverService.GetSerialNumbers();
                memoryCache.Set(nameof(GetSerialNumber), s, TimeSpan.FromSeconds(30));
            }

            return Ok(s);
        }

        [HttpPut("serial-number")]
        [Authorize(nameof(UserRole.Operator))]
        public async Task<IActionResult> UploadSerialNumberFile(IFormFile file)
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            ms.Seek(0, SeekOrigin.Begin);

            await this.serverService.UploadSerialNumbers(ms);

            return Ok();
        }

        [HttpGet("farmer")]
        [AllowAnonymous]
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
        [Authorize(nameof(UserRole.Operator))]
        public async Task<IActionResult> SetPlotManPlan([FromBody] OptimizedPlotManPlan[] plans)
        {
            var result = serverService.SetOptimizePlotManPlan(plans);
            if (result)
                return Ok();
            else
                return BadRequest();
        }

        [HttpPost("create-part")]
        [Authorize(nameof(UserRole.Operator))]
        public async Task<IActionResult> CreatePartition(string host, string block, string label)
        {
            var result = serverService.CreatePartition(host, block, label);
            if (result)
                return Ok();
            else
                return BadRequest();
        }

        [HttpPost("rename-part")]
        [Authorize(nameof(UserRole.Operator))]
        public async Task<IActionResult> RenamePartition(string host, string block, string oldLabel, string newLabel)
        {
            var result = serverService.RenamePartition(host, block, oldLabel, newLabel);
            if (result)
                return Ok();
            else
                return BadRequest();
        }

        [HttpPost("mount-part")]
        [Authorize(nameof(UserRole.Operator))]
        public async Task<IActionResult> MountPartition(string host, string block, string label)
        {
            var result = serverService.MountPartition(host, block, label);
            if (result)
                return Ok();
            else
                return BadRequest();
        }

        [HttpPost("unmount-part")]
        [Authorize(nameof(UserRole.Operator))]
        public async Task<IActionResult> UnmountPartition(string host, string label)
        {
            var result = serverService.UnmountPartition(host, label);
            if (result)
                return Ok();
            else
                return BadRequest();
        }

        [HttpPost("remove-ntfs-part")]
        [Authorize(nameof(UserRole.Operator))]
        public async Task<IActionResult> RemoveNtfsPartition(string host, string block)
        {
            var result = serverService.RemoveNtfsPartition(host, block);
            if (result)
                return Ok();
            else
                return BadRequest();
        }

        [HttpPost("enable-smart")]
        [Authorize(nameof(UserRole.Operator))]
        public async Task<IActionResult> EnableSmart(string host, string block)
        {
            var result = serverService.EnableSmart(host, block);
            if (result)
                return Ok();
            else
                return BadRequest();
        }

        [HttpDelete("plot-dir")]
        [Authorize(nameof(UserRole.Operator))]
        public async Task<IActionResult> RemovePlotDir(string host, string path)
        {
            var result = serverService.RemovePlotDir(host, path);
            if (result)
                return Ok();
            else
                return BadRequest();
        }

        [HttpDelete("temporary")]
        [Authorize(nameof(UserRole.Operator))]
        public async Task<IActionResult> CleanTemporary([FromBody] string[] names)
        {
            var result = await this.serverService.CleanLegacyTemporaryFile(names);
            if (result)
                return Ok();
            else
                return BadRequest();
        }

        [HttpDelete("plots")]
        [Authorize(nameof(UserRole.Operator))]
        public async Task<IActionResult> RemovePlots(string host, [FromBody] string[] names)
        {
            var result = await this.serverService.RemovePlots(host, names);
            if (result)
                return Ok();
            else
                return BadRequest();
        }

        [HttpPost("mount-farms")]
        [Authorize(nameof(UserRole.Operator))]
        public async Task<IActionResult> MountFarms(string host)
        {
            var result = this.serverService.MountFarms(host);
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
        [Authorize(nameof(UserRole.Operator))]
        public async Task<IActionResult> MountAll([FromBody] string[] names)
        {
            var result = await this.serverService.MountAll(names);
            if (result)
                return Ok();
            else
                return BadRequest();
        }

        [HttpPost("daemons/plotters")]
        [Authorize(nameof(UserRole.Operator))]
        public async Task<IActionResult> PlotterDaemons([FromBody] string[] names)
        {
            var result = await this.serverService.PlotterDaemons(names);
            if (result)
                return Ok();
            else
                return BadRequest();
        }

        [HttpPost("daemons/harvesters")]
        [Authorize(nameof(UserRole.Operator))]
        public async Task<IActionResult> HarvesterDaemons([FromBody] string[] names)
        {
            var result = await this.serverService.HarvesterDaemons(names);
            if (result)
                return Ok();
            else
                return BadRequest();
        }

        public record PreTransferRequest(string Address, decimal Amount);
        [HttpPost("pre-transfer")]
        [Authorize(nameof(UserRole.Admin))]
        public async Task<IActionResult> PreTransfer([FromBody] PreTransferRequest request)
        {
            var (address, amount) = request;
            var tgts = await this.serverService.GetTargets();
            var tgt = tgts.FirstOrDefault(_ => _.Address == address);
            if (tgt == null) return BadRequest("No such address");
            if (amount <= 0) return BadRequest("amount should be greater than 0");

            var rnd = new Random((int)DateTime.UtcNow.Ticks);
            var code = rnd.Next(10000).ToString();
            var detail = $" `{amount}`给 {tgt.Name} ({address})";
            await new MarkdownMessage($"验证码为{code}，本次将转账{detail}，务必确认后操作。")
                .SendAsync(this.appSettings.WeixinTransferVerificationUrl);
            SetCode(new VerficationInfo(address, amount, code));
            return Ok();
        }

        public record TransferRequest(string Address, decimal Amount, string Code);
        [HttpPost("transfer")]
        [Authorize(nameof(UserRole.Admin))]
        public async Task<IActionResult> Transfer([FromBody] TransferRequest request)
        {
            var (address, amount, code) = request;
            var tgts = await this.serverService.GetTargets();
            var tgt = tgts.FirstOrDefault(_ => _.Address == address);
            if (tgt == null) return BadRequest("No such address");
            if (amount <= 0) return BadRequest("amount should be greater than 0");

            if (!VerifyCode(new VerficationInfo(address, amount, code)))
                return Unauthorized();

            var result = await this.serverService.Transfer(address, amount);
            var detail = $" `{amount}`给 {tgt.Name} ({address}) ";
            var msg = (result == null)
                ? $"本次转账失败：{detail}，但是不排除有特殊情况显示为失败，实际为成功，请等一段时间再确认。"
                : $"已经完成本次转账：{detail}，可能需要等待区块链确认还有一小段时间。";
            await new MarkdownMessage(msg)
                .SendAsync(this.appSettings.WeixinTransferVerificationUrl);

            if (result != null)
                return Ok(new { tx = result });
            else
                return BadRequest();
        }

        [HttpGet("pouch")]
        [Authorize(nameof(UserRole.Admin))]
        public async Task<IActionResult> GetWalletBalance()
        {
            var b = await this.serverService.GetBalance();
            return Ok(b);
        }

        [HttpGet("pouch/txs")]
        [Authorize(nameof(UserRole.Admin))]
        public async Task<IActionResult> GetAllWalletTransactions()
        {
            var txs = await this.serverService.GetTxs();
            return Ok(txs);
        }

        [HttpPost("pouch/txs")]
        [Authorize(nameof(UserRole.Admin))]
        public async Task<IActionResult> GetWalletTransactions([FromBody] string[] tx)
        {
            var txs = await this.serverService.GetTxs(tx);
            return Ok(txs);
        }

        [HttpGet("targets")]
        [Authorize(nameof(UserRole.Admin))]
        public async Task<IActionResult> GetTargetList()
        {
            var ret = await this.serverService.GetTargets();
            return Ok(ret);
        }

        public record CreateTargetRequest(string? Id, string Name, string Address);

        [HttpPost("targets")]
        [Authorize(nameof(UserRole.Admin))]
        public async Task<IActionResult> CreateTarget([FromBody] CreateTargetRequest request)
        {
            var ret = await this.serverService.CreateOrUpdateTarget(new Receiver(request.Id, request.Name, request.Address));
            var msg = $"创建账号：{request.Name}\n{request.Address}";
            await new MarkdownMessage(msg)
                .SendAsync(this.appSettings.WeixinTransferVerificationUrl);
            if (ret)
                return Ok();
            else
                return BadRequest();
        }

        public record DeleteTargetsRequest(string[] ids);
        [HttpDelete("targets")]
        [Authorize(nameof(UserRole.Admin))]
        public async Task<IActionResult> DeleteTargets([FromBody] DeleteTargetsRequest request)
        {
            var ret = await this.serverService.DeleteTargets(request.ids);
            if (ret)
                return Ok();
            else
                return BadRequest();
        }

        private record VerficationInfo(string Address, decimal Amount, string Code);
        private bool VerifyCode(VerficationInfo info)
        {
            if (!memoryCache.TryGetValue(nameof(VerficationInfo), out VerficationInfo saved))
                return false;

            return saved == info;
        }

        private void SetCode(VerficationInfo info)
        {
            memoryCache.Set(nameof(VerficationInfo), info, TimeSpan.FromMinutes(5));
        }
    }
}
