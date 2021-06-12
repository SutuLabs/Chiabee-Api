namespace WebApi.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Options;
    using Renci.SshNet;
    using WebApi.Helpers;
    using WebApi.Models;
    using WebApi.Services.ServerCommands;
    using static WebApi.Services.ServerCommands.CommandHelper;

    public class ServerService : IDisposable
    {
        private TargetMachine[] plotterClients;
        private TargetMachine[] harvesterClients;
        private TargetMachine farmerLogClient;
        private TargetMachine[] farmerClients;
        private bool disposedValue;

        internal FixedSizedQueue<EligibleFarmerEvent> eventList = new();
        internal Dictionary<string, ErrorEvent> errorList = new();
        private readonly AppSettings appSettings;

        public ServerService(IOptions<AppSettings> appSettings)
        {
            this.appSettings = appSettings.Value;

            var farmer = this.appSettings.GetFarmers();
            var plotter = this.appSettings.GetPlotters();
            var harvester = this.appSettings.GetHarvesters();

            this.plotterClients = plotter.ToMachineClients().ToArray();
            this.harvesterClients = harvester.ToMachineClients().ToArray();
            this.farmerLogClient = farmer.First().ToMachineClient();
            this.farmerClients = farmer.ToMachineClients().ToArray();

            this.farmerLogClient.StartTailChiaLog((err) =>
            {
                if (this.errorList.ContainsKey(err.Error))
                {
                    this.errorList.Remove(err.Error);
                }

                this.errorList.Add(err.Error, new ErrorEvent(err.Time, err.Level, err.Error));
            }, (evt) =>
            {
                this.eventList.Enqueue(evt);
            });
        }

        public async Task<bool> StopPlot(string machineName, string plotId)
        {
            var machine = this.plotterClients.FirstOrDefault(_ => _.Name == machineName);
            if (machine == null) return false;

            return machine.StopPlot(plotId);
        }

        public async Task<bool> CleanLegacyTemporaryFile(string[] machineNames)
        {
            var machines = this.plotterClients;
            var successFlag = true;
            foreach (var name in machineNames)
            {
                var m = machines.FirstOrDefault(_ => _.Name == name);
                if (m == null) continue;

                successFlag &= m.CleanLegacyTemporaryFiles();
            }

            return successFlag;
        }

        public async Task<ServerStatus[]> GetServersInfo() =>
            new[] { this.farmerClients, this.plotterClients, this.harvesterClients }
                .AsParallel()
                .SelectMany(_ => _.Select(_ => TryGet(() => _.GetServerStatus())))
                .Where(_ => _ != null)
                .ToArray();

        public async Task<PlotInfo[]> GetPlotsInfo() =>
            new[] { this.farmerClients, this.harvesterClients }
                .AsParallel()
                .SelectMany(_ => _
                    .SelectMany(_ => TryGet(() => _.GetPlotFarmInfo()) ?? new PlotInfo[] { })
                    .Where(_ => _ != null))
                .Where(_ => _ != null)
                .ToArray();

        public async Task<MachineWithDisks[]> GetHarvesterDisksInfo() =>
            new[] { this.farmerClients, this.harvesterClients }
                .AsParallel()
                .SelectMany(_ => _)
                .Select(_ => new MachineWithDisks(_.Name, TryGet(() => _.GetHarvesterDiskInfo())))
                .ToArray();

        public async Task<PlotterStatus[]> GetPlotterInfo() =>
            this.plotterClients
                .AsParallel()
                .Select(_ => TryGet(() => _.GetPlotterStatus()))
                .Where(_ => _ != null)
                .ToArray();

        public async Task<FarmerNodeStatus[]> GetFarmerInfo() =>
            this.farmerClients
                .AsParallel()
                .Select(_ => new FarmerNodeStatus(
                    _.Name,
                    TryGet(() => _.GetFarmerStatus()),
                    TryGet(() => _.GetNodeStatus())))
                .Where(_ => _ != null && _.Farmer != null && _.Node != null)
                .ToArray();

        public async Task<bool> PlotterDaemons(string[] names) =>
            this.plotterClients
                .Where(_ => (names == null || names.Length == 0) || names.Contains(_.Name))
                .AsParallel()
                .Select(_ => _.StartPlotterDaemon())
                .Aggregate(true, (l, c) => l & c);

        public async Task<bool> HarvesterDaemons(string[] names) =>
            this.harvesterClients
                .Where(_ => (names == null || names.Length == 0) || names.Contains(_.Name))
                .AsParallel()
                .Select(_ => _.StartHarvesterDaemon())
                .Aggregate(true, (l, c) => l & c);

        public IEnumerable<OptimizedPlotManPlan> GetOptimizePlotManPlan(PlotterStatus[] plotters, ServerStatus[] harvesters)
        {
            var hs = harvesters.ToDictionary(_ => _.Name, _ => _);
            var targets = this.appSettings.GetHarvesters()
                .Where(h => hs.TryGetValue(h.Name, out var ss) && (ss.Disks.Sum(d => Math.Max(0, d.Available / 108_888_888 - 1)) > 3)) // 1-K based
                .Select(_ => _.Host)
                .Reverse()
                .Select(_ => new HarvestorPlan(_, Array.Empty<string>(), 0))
                .ToDictionary(_ => _.Host, _ => _);
            if (targets.Count == 0) yield break;

            var ps = plotters
                .Select(_ =>
                {
                    var model = _.Name.Substring(0, 4).ToLower();
                    var finalNum = _.FileCounts.FirstOrDefault()?.Count ?? 0;
                    var (jobNum, stagger, popd) = model switch
                    {
                        "r720" => (14, 30, 24),
                        "r420" => (7, 45, 12),
                        _ => (0, 120, 0),
                    };

                    return new PlotterPlan(_.Name, model, finalNum, jobNum, stagger, popd);
                })
                .OrderByDescending(_ => _.FinalNum)
                .ThenByDescending(_ => _.Popd)
                .ToArray();

            foreach (var p in ps)
            {
                var t = targets.OrderBy(_ => _.Value.Weight).First().Value;
                targets[t.Host] = t with { Jobs = t.Jobs.Concat(new[] { p.Name }).ToArray(), Weight = t.Weight + p.Popd + p.FinalNum };
            }

            foreach (var t in targets.Select(_ => _.Value))
            {
                for (var i = 0; i < t.Jobs.Length; i++)
                {
                    var j = t.Jobs[i];
                    var p = ps.First(_ => _.Name == j);
                    yield return new OptimizedPlotManPlan(j, new PlotManConfiguration(t.Host, i, p.JobNum, p.Stagger));
                }
            }
        }

        // POPD: Plot Output Per Day
        private record PlotterPlan(string Name, string Model, int FinalNum, int JobNum, int Stagger, int Popd);
        private record HarvestorPlan(string Host, string[] Jobs, int Weight);

        public bool SetOptimizePlotManPlan(OptimizedPlotManPlan[] plans)
        {
            var machines = this.plotterClients;
            var successFlag = true;
            foreach (var plan in plans)
            {
                var m = machines.FirstOrDefault(_ => _.Name == plan.Name);
                if (m == null) continue;

                successFlag &= m.SetPlotManConfiguration(plan.Plan);
                successFlag &= m.StartPlotterDaemon();
            }

            return successFlag;
        }

        public bool CreatePartition(string host, string block, string label)
        {
            var machines = this.harvesterClients;
            var m = machines.FirstOrDefault(_ => _.Name == host);
            if (m == null) return false;

            return m.CreatePartition(block, label);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    var cs = new[] { this.farmerClients, this.plotterClients, this.harvesterClients, new[] { this.farmerLogClient } }
                        .SelectMany(_ => _);
                    foreach (var c in cs)
                    {
                        if (c.IsConnected) c.Disconnect();
                        c.Dispose();
                    }
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    public record OptimizedPlotManPlan(string Name, PlotManConfiguration Plan);
    public record MachineWithDisks(string Name, HarvesterDiskInfo[] Disks);
}