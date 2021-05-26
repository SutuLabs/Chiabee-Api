namespace WebApi.Services.ServerCommands
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Renci.SshNet;
    using WebApi.Models;

    public static class ServerStatusCommand
    {
        public static ServerStatus GetServerStatus(this TargetMachine client)
        {
            client.EnsureConnected();
            var topCmd = client.RunCommand(@"top -b1n1 -E G |grep ""Cpu\|Mem \|Tasks""");
            var output = topCmd.Result;

            var disks = client.GetDiskStatus();

            return new ServerStatus()
            {
                Process = ParseProcessState(output),
                Memory = ParseMemoryState(output),
                Cpus = ParseCpuState(output).OrderBy(_ => _.Index).Select(_ => _.Idle).ToArray(),
                Disks = disks,
                Name = client.Name,
            };

            static ProcessState ParseProcessState(string output)
            {
                //Tasks: 520 total,   3 running, 516 sleeping,   0 stopped,   1 zombie
                var reState = new Regex(@"Tasks: +(?<total>\d*) total, +(?<running>\d*) running, +(?<sleeping>\d*) sleeping, +(?<stopped>\d*) stopped, +(?<zombie>\d*) zombie");
                var match = reState.Match(output);
                return new ProcessState(
                    int.Parse(match.Groups["total"].Value),
                    int.Parse(match.Groups["running"].Value),
                    int.Parse(match.Groups["sleeping"].Value),
                    int.Parse(match.Groups["stopped"].Value),
                    int.Parse(match.Groups["zombie"].Value));
            }

            static MemoryState ParseMemoryState(string output)
            {
                //MiB Mem : 128859.6 total,    568.1 free,  16263.6 used, 112028.0 buff/cache
                var reState = new Regex(@"(?<unit>[A-Z]?iB) Mem : +(?<total>[\d\.]*) total, +(?<free>[\d\.]*) free, +(?<used>[\d\.]*) used, +(?<cache>[\d\.]*) buff/cache");
                var match = reState.Match(output);
                return new MemoryState(
                    decimal.Parse(match.Groups["total"].Value),
                    decimal.Parse(match.Groups["free"].Value),
                    decimal.Parse(match.Groups["used"].Value));
            }

            static IEnumerable<CpuState> ParseCpuState(string output)
            {
                //%Cpu45 :  5.6 us, 11.1 sy,  0.0 ni, 83.3 id,  0.0 wa,  0.0 hi,  0.0 si,  0.0 st
                var reState = new Regex(@"%Cpu(?<idx>\d+) *: *(?<us>[\d\.]*) us, *(?<sy>[\d\.]*) sy, *(?<ni>[\d\.]*) ni, *(?<id>[\d\.]*) id, *(?<wa>[\d\.]*) wa, *(?<hi>[\d\.]*) hi, *(?<si>[\d\.]*) si, *(?<st>[\d\.]*) st");
                var matches = reState.Matches(output);
                //foreach (var line in output.Split("\n",StringSplitOptions.RemoveEmptyEntries))
                foreach (Match match in matches)
                {
                    yield return new CpuState(
                        int.Parse(match.Groups["idx"].Value),
                        decimal.Parse(match.Groups["id"].Value));
                }
            }
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

    public record DiskStatus(string Device, long Size, long Used, long Available, string Path);
    public record MemoryState(decimal Total, decimal Free, decimal Used);
    public record CpuState(int Index, decimal Idle);
    public record ProcessState(int Total, int Running, int Sleeping, int Stopped, int Zombie);
}