namespace WebApi.Services.ServerCommands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using WebApi.Models;

    public static class PlotterStatusCommand
    {
        public static PlotterStatus GetPlotterStatus(this TargetMachine client)
        {
            if (!client.EnsureConnected()) return null;

            var pmCmd = client.RunCommand(@". ~/chia-blockchain/activate && plotman status");

            var fileCounts = client.GetDirectoryFileCountCommand(new[] { "/data/final" });
            var jobs = ParsePlotStatusOutput(pmCmd.Result).ToArray();
            var cfg = client.ReadPlotManConfiguration();
            return new PlotterStatus(client.Name, jobs, fileCounts, cfg);

            static IEnumerable<PlotJob> ParsePlotStatusOutput(string output)
            {
                // plot id    k          tmp                      dst   wall   phase    tmp     pid   stat    mem   user    sys    io
                //2cafd524   32   /data/tmp1   /data/transition/final   1:12     1:4   150G   12535    SLP   4.2G   1:51   0:09    9s
                var lines = output.Split("\n", StringSplitOptions.RemoveEmptyEntries).Select(_ => _.Trim()).ToArray();
                var idx = 0;
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("plot id")) continue;

                    var parts = line.Split(" ", StringSplitOptions.RemoveEmptyEntries).Select(_ => _.Trim()).ToArray();
                    if (parts.Length != 13) continue;

                    yield return new PlotJob(idx, parts[0], parts[1], parts[2], parts[3], parts[4],
                        parts[5], parts[6], int.Parse(parts[7]), parts[9], parts[12]);
                    idx++;
                }
            }
        }

        public static DirectoryFileCount[] GetDirectoryFileCountCommand(this TargetMachine client, params string[] pathes)
        {
            return pathes.Select(_ => client.GetDirectoryFileCountCommand(_)).ToArray();
        }

        public static DirectoryFileCount GetDirectoryFileCountCommand(this TargetMachine client, string path)
        {
            if (!client.EnsureConnected()) return null;
            var cmd = client.RunCommand(@$"ls {path}/*.plot | wc -l");
            if (!int.TryParse(cmd.Result, out var count)) return null;
            return new DirectoryFileCount(path, count);
        }
    }

    public record DirectoryFileCount(string Path, int Count);

    public record PlotterStatus(string Name, PlotJob[] Jobs, DirectoryFileCount[] FileCounts, PlotManConfiguration Configuration);
    public record PlotJob(int Index, string Id, string K, string TempDir, string DestDir, string WallTime,
        string Phase, string TempSize, int Pid, string MemorySize, string IoTime);
}