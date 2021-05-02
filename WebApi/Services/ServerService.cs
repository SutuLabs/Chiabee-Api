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

    public class ServerService
    {
        public async Task<PlotterServerStatus> GetPlotterInfo()
        {
            var connectionInfo = new ConnectionInfo(
                "10.177.0.133", "sutu", new PrivateKeyAuthenticationMethod("sutu", new PrivateKeyFile(@"P:\.ssh\id_rsa.PEM")));
            using var client = new SshClient(connectionInfo);

            client.Connect();
            var topCmd = client.RunCommand(@"top -b1n1 -E G |grep ""Cpu\|Mem \|Tasks""");
            var topOutput = topCmd.Result;

            var pmCmd = client.RunCommand(@". ~/chia-blockchain/activate && plotman status");
            var plotmanOutput = pmCmd.Result;

            client.Disconnect();

            return new PlotterServerStatus(plotmanOutput, topOutput);
        }

        public async Task<FarmServerStatus> GetFarmInfo()
        {
            var connectionInfo = new ConnectionInfo(
                "10.177.0.148", "sutu", new PrivateKeyAuthenticationMethod("sutu", new PrivateKeyFile(@"P:\.ssh\id_rsa.PEM")));
            using var client = new SshClient(connectionInfo);

            client.Connect();
            var topCmd = client.RunCommand(@"top -b1n1 -E G |grep ""Cpu\|Mem \|Tasks""");
            var topOutput = topCmd.Result;

            var farmCmd = client.RunCommand(@". ~/chia-blockchain/activate && chia farm summary");
            var farmOutput = farmCmd.Result;

            var nodeCmd = client.RunCommand(@". ~/chia-blockchain/activate && chia show -s");
            var nodeOutput = nodeCmd.Result;

            client.Disconnect();

            return new FarmServerStatus(farmOutput, nodeOutput, topOutput);
        }
    }

    public record ServerStatus
    {
        // cpu/disk/network
        public ProcessState Process { get; init; }
        public MemoryState Memory { get; init; }
        public CpuState[] Cpus { get; init; }

    }

    public record PlotterServerStatus : ServerStatus
    {
        public PlotterServerStatus(string plotmanOutput, string topOutput)
        {
            this.Jobs = ParsePlotStatusOutput(plotmanOutput).ToArray();
            this.Cpus = ParseCpuState(topOutput).ToArray();
            this.Memory = ParseMemoryState(topOutput);
            this.Process = ParseProcessState(topOutput);
        }

        public PlotJob[] Jobs { get; init; }
    }

    public record FarmServerStatus : ServerStatus
    {
        public FarmServerStatus(string farmOutput, string nodeOutput, string topOutput)
        {
            this.Farm = ParseFarmStatus(farmOutput);
            this.Node = ParseNodeStatus(nodeOutput);
            this.Cpus = ParseCpuState(topOutput).ToArray();
            this.Memory = ParseMemoryState(topOutput);
            this.Process = ParseProcessState(topOutput);
        }

        public NodeStatus Node { get; init; }
        public FarmStatus Farm { get; init; }
    }

    public record MemoryState(decimal Total, decimal Free, decimal Used);
    public record CpuState(int Index, decimal Idle);
    public record ProcessState(int Total, int Running, int Sleeping, int Stopped, int Zombie);
    public record PlotJob(int Index, string Id, string K, string TempDir, string DestDir, string WallTime,
        string Phase, string TempSize, int Pid, string MemorySize, string IoTime);
    public record NodeStatus(string Status, DateTime Time, int Height, string Space, string Difficulty, string Iterations, string TotalIterations);
    public record FarmStatus(string Status, decimal TotalFarmed, decimal TxFees, decimal Rewards, int LastFarmedHeight, int PlotCount, string TotalSize, string Space, string ExpectedToWin);
}