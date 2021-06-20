namespace WebApi.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
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

            var param = new ExecutionPlanParameter(plotters, hs, this.appSettings.GetAllMachines());

            // get this json to used in unit test
            ////var json = JsonConvert.SerializeObject(param);

            var plans = GetExecutionPlan(param).ToArray();
            var result = plans
                .AsParallel()
                .Select(_ => ExecutePlan(_))
                .All(_ => _);

            bool ExecutePlan(ExecutionRsyncPlan plan)
            {
                var msg = $"Plan to transfer: {plan.FromHost} -> {plan.ToHost}@{plan.DiskName}";
                try
                {
                    var p = this.plotterClients.FirstOrDefault(_ => _.Name == plan.FromHost);
                    p.EnsureConnected();
                    var chkCmd = p.RunCommand("ps -eo cmd | grep '^rsync'");
                    var rsyncExist = chkCmd.Result.StartsWith("rsync");
                    if (rsyncExist)
                    {
                        msg += ", however, rsync process already exists, abort." + $"[{plan.PlotFilePath}]";
                    }
                    else
                    {
                        var cmd = $"rsync --compress-level=0 --remove-source-files -P {plan.PlotFilePath}" +
                            $" rsync://sutu@{plan.ToHost}:12000/plots/{plan.DiskName}" +
                            $" | tee ~/plotter/rsync.log &";
                        var rsyncCmd = p.CreateCommand(cmd);
                        rsyncCmd.BeginExecute();
                        msg += $", rsync started." + $"[{plan.PlotFilePath}]";
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
                    this.logger.LogWarning(msg + ", but failed to execute." + $"[{plan.PlotFilePath}]", ex);
                    return false;
                }
            }
        }

        internal static IEnumerable<ExecutionRsyncPlan> GetExecutionPlan(ExecutionPlanParameter planParam)
        {
            var (plotters, harvesters, allMachines) = planParam;

            const int PlotSize = 108_888_888;// 1-K based
            var dicLoc = allMachines
                .Where(_ => _.Type == ServerType.Harvester || _.Type == ServerType.Plotter)
                .ToDictionary(_ => _.Name, _ => _.Location);
            var locs = dicLoc
                .Select(_ => _.Value)
                .Distinct();
            var allHs = allMachines.Where(_ => _.Type == ServerType.Harvester).ToArray();

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
                    .Select(h => hs.TryGetValue(h.Name, out var ss) ? (h, disks: GetAvailableDisks(ss.Disks)) : (null, null))
                    .Where(_ => _.disks != null && _.disks.Sum(d => d.available) > 3)
                    .Reverse()
                    .Select(_ => new HarvesterPlan(
                        new HarvesterTarget(_.h.Name, _.h.Hosts, _.disks.Select(d => d.name).ToArray()),
                        Array.Empty<string>(), 0))
                    .SelectMany(_ => _.Harvester.Hosts.Select(h => (h, _)))
                    .ToDictionary(_ => _.h, _ => _._);
                if (targets.Count == 0) yield break;

                //var diskOccupation = GetAvailableDisks(hs.SelectMany(_ => _.Value.Disks))
                //    .ToDictionary(_ => _.name, _ => new DiskOccupy(_.name, _.available, 0));

                (string name, int available)[] GetAvailableDisks(IEnumerable<DiskStatus> disks) => disks
                    .Select(d => (path: d.Path, available: (int)Math.Max(0, d.Available / PlotSize - 1)))
                    .Where(_ => _.available > 0 && _.path.StartsWith(farmPrefix))
                    .Select(d => (d.path[farmPrefix.Length..], d.available))
                    .ToArray();

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
                        var speed = _.MadmaxJob?.Job?.CopyingSpeed ?? 0;
                        var disk = _.MadmaxJob?.Job?.CopyingDisk;

                        return new PlotterPlan(_.Name, model, finalNum, popd, filePath, target, speed, disk);
                    })
                    .OrderByDescending(_ => _.FinalNum)
                    .ThenByDescending(_ => _.Popd)
                    .ToArray();

                // add weight only when plotter is transferring
                foreach (var p in ps.Where(_ => _.CopyingTarget != null))
                {
                    var t = targets[p.CopyingTarget];
                    targets[p.CopyingTarget] = t with
                    {
                        Weight = t.Weight + p.CopyingSpeed,
                    };
                    //if (diskOccupation.ContainsKey(p.CopyingDisk))
                    //{
                    //    var d = diskOccupation[p.CopyingDisk];
                    //    diskOccupation[p.CopyingDisk] = d with
                    //    {
                    //        Copying = d.Copying + 1,
                    //    };
                    //}
                }

                foreach (var p in ps.Where(_ => _.CopyingTarget == null && _.FinalNum > 0))
                {
                    var fg = targets
                        .GroupBy(_ => _.Value.Harvester.Name)
                        .OrderBy(g => g.Sum(_ => _.Value.Weight))
                        .First().Key;
                    var (key, t) = targets
                        .Where(_ => _.Value.Harvester.Name == fg)
                        .OrderBy(_ => _.Value.Weight).First();
                    const int bandwidth = 120 * 1024 * 1024;
                    targets[key] = t with
                    {
                        Jobs = t.Jobs.Concat(new[] { p.Name }).ToArray(),
                        Weight = t.Weight + bandwidth,
                    };
                }

                foreach (var (host, t) in targets)
                {
                    foreach (var j in t.Jobs)
                    {
                        var p = ps.First(_ => _.Name == j);
                        var rnd = new Random().Next(t.Harvester.Disks.Length);
                        var disk = t.Harvester.Disks[rnd];
                        yield return new ExecutionRsyncPlan(j, p.FilePath, host, disk);

                        //string ChooseDisk()
                        //{
                        //    var disk = diskOccupation
                        //        .Where(_ => _.Value.Copying < _.Value.Available)
                        //        .OrderBy(_ => _.Value.Copying);
                        //}
                    }
                }
            }
        }


        // POPD: Plot Output Per Day
        private record PlotterPlan(string Name, string Model, int FinalNum, int Popd, string FilePath, string CopyingTarget, int CopyingSpeed, string CopyingDisk);
        [DebuggerDisplay("{Name, nq}, Hosts: {Hosts.Length}, Disks: {Disks.Length}")]
        private record HarvesterTarget(string Name, string[] Hosts, string[] Disks);
        [DebuggerDisplay("{Harvester}, Jobs: {Jobs.Length}, Weight: {Weight}")]
        private record HarvesterPlan(HarvesterTarget Harvester, string[] Jobs, int Weight);
        private record DiskOccupy(string Name, int Available, int Copying);
        internal record ExecutionRsyncPlan(string FromHost, string PlotFilePath, string ToHost, string DiskName);
        internal record ExecutionPlanParameter(PlotterStatus[] plotters, ServerStatus[] harvesters, SshEntity[] allMachines);
    }
}