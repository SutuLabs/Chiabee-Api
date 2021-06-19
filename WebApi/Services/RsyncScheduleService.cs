namespace WebApi.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Newtonsoft.Json;
    using Renci.SshNet.Common;
    using WebApi.Entities;
    using WebApi.Helpers;
    using WebApi.Models;
    using WebApi.Services.ServerCommands;
    using static WebApi.Services.ServerCommands.CommandHelper;

    internal class RsyncScheduleService : BaseRefreshService
    {
        private readonly PersistentService persistentService;
        private readonly ServerService server;
        private readonly AppSettings appSettings;
        private TargetMachine[] plotterClients;
        private TargetMachine[] harvesterClients;

        public RsyncScheduleService(
            ILogger<RsyncScheduleService> logger,
            PersistentService persistentService,
            ServerService server,
            IOptions<AppSettings> appSettings)
            : base(logger, nameof(RsyncScheduleService), 3, 5)
        {
            this.persistentService = persistentService;
            this.server = server;
            this.appSettings = appSettings.Value;

            var plotter = this.appSettings.GetPlotters();
            var harvester = this.appSettings.GetHarvesters();

            this.plotterClients = plotter.ToMachineClients().ToArray();
            this.harvesterClients = harvester.ToMachineClients().ToArray();
        }

        protected override async Task DoWorkAsync()
        {
            var farm = await this.persistentService.RetrieveEntityAsync<FarmStateEntity>();
            var plotters = JsonConvert.DeserializeObject<PlotterStatus[]>(farm.PlotterJsonGzip.Decompress());
            var harvesters = JsonConvert.DeserializeObject<HarvesterStatus[]>(farm.HarvesterJsonGzip.Decompress());

            var server = await this.persistentService.RetrieveEntityAsync<MachineStateEntity>();
            var machines = JsonConvert.DeserializeObject<ServerStatus[]>(server.MachinesJsonGzip.Decompress());

            var farmSeconds = (DateTime.UtcNow - farm.Timestamp).TotalSeconds;
            var serverSeconds = (DateTime.UtcNow - server.Timestamp).TotalSeconds;
            const int staleThreshold = 60;
            if (farmSeconds > staleThreshold || serverSeconds > staleThreshold)
            {
                logger.LogInformation($"information is stale (farm: {farmSeconds}s, server: {serverSeconds}s), ignore to rsync");
                return;
            }

            var hs = machines
                .Select(_ => (h: harvesters.FirstOrDefault(h => h.Name == _.Name), m: _))
                .Where(_ => _.h != null)
                .Select(_ => _.m with
                {
                    Disks = _.m.Disks
                        .Where(d => _.h.AbnormalFarmlands.IoErrors.All(p => p != d.Path))
                        .ToArray()
                })
                .ToArray();

            var plans = GetExecutionPlan(plotters, hs).ToArray();
            var result = plans
                .AsParallel()
                .Select(_ => ExecutePlan(_))
                .All(_ => _);

            bool ExecutePlan(ExecutionRsyncPlan plan)
            {
                var msg = $"Plan to transfer [{plan.PlotFilePath}]: {plan.FromHost} -> {plan.ToHost}@{plan.DiskName}";
                try
                {
                    var p = this.plotterClients.FirstOrDefault(_ => _.Name == plan.FromHost);
                    p.EnsureConnected();
                    var chkCmd = p.RunCommand("ps -eo cmd | grep '^rsync'");
                    var rsyncExist = chkCmd.Result.StartsWith("rsync");
                    if (rsyncExist)
                    {
                        msg += ", however, rsync process already exists, abort.";
                    }
                    else
                    {
                        var cmd = $"rsync --compress-level=0 --remove-source-files -P {plan.PlotFilePath}" +
                            $" rsync://sutu@{plan.ToHost}:12000/plots/{plan.DiskName}" +
                            $" | tee ~/plotter/rsync.log &";
                        var rsyncCmd = p.CreateCommand(cmd);
                        rsyncCmd.BeginExecute();
                        msg += $", rsync started.";
                    }

                    this.logger.LogInformation(msg);
                    return true;
                }
                catch (SshOperationTimeoutException)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    this.logger.LogWarning(msg + ", but failed to execute.", ex);
                    return false;
                }
            }
        }

        private IEnumerable<ExecutionRsyncPlan> GetExecutionPlan(PlotterStatus[] plotters, ServerStatus[] harvesters)
        {
            const int PlotSize = 108_888_888;// 1-K based
            var dicLoc = new[] { this.plotterClients, this.harvesterClients }
                .SelectMany(_ => _)
                .ToDictionary(_ => _.Name, _ => _.Location);
            var locs = dicLoc
                .Select(_ => _.Value)
                .Distinct();
            var allHs = this.appSettings.GetHarvesters();

            return locs
                .Select(loc => new
                {
                    ps = plotters.Where(_ => dicLoc.TryGetValue(_.Name, out var dl) && dl == loc).ToArray(),
                    hs = harvesters.Where(_ => dicLoc.TryGetValue(_.Name, out var dl) && dl == loc).ToArray()
                })
                .SelectMany(_ => GetPlan(_.ps, _.hs, allHs));

            static IEnumerable<ExecutionRsyncPlan> GetPlan(PlotterStatus[] plotters, ServerStatus[] harvesters, SshEntity[] allHarvesters)
            {
                const string farmPrefix = "/farm/";
                var hs = harvesters
                    .ToDictionary(_ => _.Name, _ => _);
                var targets = allHarvesters
                    .Select(h => hs.TryGetValue(h.Name, out var ss) ? (h, disks: ss.Disks.Select(d => (path: d.Path, available: Math.Max(0, d.Available / PlotSize - 1))).ToArray()) : (null, null))
                    .Where(_ => _.disks != null && _.disks.Sum(d => d.available) > 3)
                    .Reverse()
                    .Select(_ => new HarvesterPlan(
                        new HarvesterTarget(
                            _.h.Name,
                            new[] { _.h.Host }.Concat(_.h.AlternativeHosts ?? Array.Empty<string>()).ToArray(),
                            _.disks.Where(_ => _.available > 0 && _.path.StartsWith(farmPrefix)).Select(d => d.path[farmPrefix.Length..]).ToArray()),
                        Array.Empty<string>(), 0))
                    .ToDictionary(_ => _.Harvester.Name, _ => _);
                if (targets.Count == 0) yield break;

                var ps = plotters
                    .Select(_ =>
                    {
                        var model = _.Name.Substring(0, 4).ToLower();
                        var folder = _.Files.FirstOrDefault();
                        var finalNum = folder?.Files.Length ?? 0;
                        var filePath = folder?.Path == null ? null : folder?.Path + "/" + folder?.Files.FirstOrDefault();
                        //var popd = 24 * 3600 / _.MadmaxJob.Statistics.AverageTime;
                        var popd = 0;
                        var target = _.MadmaxJob?.Job?.CopyingTarget;
                        var targetName = target == null ? null : allHarvesters.FirstOrDefault(_ => new[] { _.Host }.Concat(_.AlternativeHosts ?? new string[] { }).Contains(target)).Name;

                        return new PlotterPlan(_.Name, model, finalNum, popd, filePath, targetName);
                    })
                    .OrderByDescending(_ => _.FinalNum)
                    .ThenByDescending(_ => _.Popd)
                    .ToArray();

                // add weight only when plotter is transferring
                foreach (var p in ps.Where(_ => _.CopyingTarget != null))
                {
                    var t = targets.OrderBy(_ => _.Value.Weight).First().Value;
                    targets[t.Harvester.Name] = t with
                    {
                        Weight = t.Weight + 1,
                    };
                }

                foreach (var p in ps.Where(_ => _.CopyingTarget == null && _.FinalNum > 0))
                {
                    var t = targets.OrderBy(_ => _.Value.Weight).First().Value;
                    targets[t.Harvester.Name] = t with
                    {
                        Jobs = t.Jobs.Concat(new[] { p.Name }).ToArray(),
                        Weight = t.Weight + 1,
                    };
                }

                foreach (var t in targets.Select(_ => _.Value))
                {
                    for (var i = 0; i < t.Jobs.Length; i++)
                    {
                        var j = t.Jobs[i];
                        var p = ps.First(_ => _.Name == j);
                        var host = t.Harvester.Hosts[i % t.Harvester.Hosts.Length];
                        var disk = t.Harvester.Disks[i % t.Harvester.Disks.Length];
                        yield return new ExecutionRsyncPlan(j, p.FilePath, host, disk);
                    }
                }
            }
        }


        // POPD: Plot Output Per Day
        private record PlotterPlan(string Name, string Model, int FinalNum, int Popd, string FilePath, string CopyingTarget);
        private record HarvesterTarget(string Name, string[] Hosts, string[] Disks);
        private record HarvesterPlan(HarvesterTarget Harvester, string[] Jobs, int Weight);
        private record OptimizedRsyncPlan(string Name, string RsyncdHost, int? RsyncdIndex);
        private record ExecutionRsyncPlan(string FromHost, string PlotFilePath, string ToHost, string DiskName);
    }
}