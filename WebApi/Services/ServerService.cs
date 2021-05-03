namespace WebApi.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Renci.SshNet;
    using WebApi.Entities;
    using WebApi.Helpers;
    using static OutputParser;

    public class ServerService : IDisposable
    {
        SshClient plotterClient;
        SshClient farmerClient;
        private bool disposedValue;

        public ServerService()
        {
            this.plotterClient = new SshClient("10.177.0.133", "sutu", new PrivateKeyFile(@"P:\.ssh\id_rsa.PEM"));
            this.farmerClient = new SshClient("10.177.0.148", "sutu", new PrivateKeyFile(@"P:\.ssh\id_rsa.PEM"));
        }

        public async Task<ServerStatus[]> GetServersInfo()
        {
            return new[] {
                this.farmerClient.GetServerStatus() with { Name = "Farmer" },
                this.plotterClient.GetServerStatus() with { Name = "Plotter" },
            };
        }

        public async Task<PlotterServerStatus> GetPlotterInfo()
        {
            var jobs = this.plotterClient.GetPlotStatus();
            var ss = this.plotterClient.GetServerStatus() with { Name = "Plotter" };

            return new PlotterServerStatus(ss, jobs);
        }

        public async Task<FarmServerStatus> GetFarmerInfo()
        {
            var ss = this.farmerClient.GetServerStatus() with { Name = "Farmer" };
            var farm = this.farmerClient.GetFarmStatus();
            var node = this.farmerClient.GetNodeStatus();

            return new FarmServerStatus(ss, node, farm);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (this.farmerClient.IsConnected) this.farmerClient.Disconnect();
                    this.farmerClient.Dispose();
                    if (this.plotterClient.IsConnected) this.plotterClient.Disconnect();
                    this.plotterClient.Dispose();
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

    public record ServerStatus
    {
        // cpu/disk/network
        public string Name { get; init; }
        public ProcessState Process { get; init; }
        public MemoryState Memory { get; init; }
        public decimal[] Cpus { get; init; }
        public DiskStatus[] Disks { get; init; }

    }

    public record PlotterServerStatus : ServerStatus
    {
        public PlotterServerStatus(ServerStatus serverStatus, PlotJob[] jobs)
        {
            this.Jobs = jobs;
            this.Cpus = serverStatus.Cpus;
            this.Memory = serverStatus.Memory;
            this.Process = serverStatus.Process;
            this.Name = serverStatus.Name;
            this.Disks = serverStatus.Disks;
        }

        public PlotJob[] Jobs { get; init; }
    }

    public record FarmServerStatus : ServerStatus
    {
        public FarmServerStatus(ServerStatus serverStatus, NodeStatus node, FarmStatus farm)
        {
            this.Farm = farm;
            this.Node = node;
            this.Cpus = serverStatus.Cpus;
            this.Memory = serverStatus.Memory;
            this.Process = serverStatus.Process;
            this.Name = serverStatus.Name;
            this.Disks = serverStatus.Disks;
        }

        public NodeStatus Node { get; init; }
        public FarmStatus Farm { get; init; }
    }

    public record DiskStatus(string Device, long Size, long Used, long Available, string Path);
    public record MemoryState(decimal Total, decimal Free, decimal Used);
    public record CpuState(int Index, decimal Idle);
    public record ProcessState(int Total, int Running, int Sleeping, int Stopped, int Zombie);
    public record PlotJob(int Index, string Id, string K, string TempDir, string DestDir, string WallTime,
        string Phase, string TempSize, int Pid, string MemorySize, string IoTime);
    public record NodeStatus(string Status, DateTime Time, int Height, string Space, string Difficulty, string Iterations, string TotalIterations);
    public record FarmStatus(string Status, decimal TotalFarmed, decimal TxFees, decimal Rewards, int LastFarmedHeight, int PlotCount, string TotalSize, string Space, string ExpectedToWin);
}