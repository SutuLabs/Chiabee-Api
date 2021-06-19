#nullable enable

namespace WebApi.Services.ServerCommands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using WebApi.Models;

    public static class PlotterStatusCommand
    {
        public static PlotterStatus GetPlotterStatus(this TargetMachine client)
        {
            var files = client.GetDirectoryFileCountCommand(new[] { "/data/final" });
            var processes = client.GetProcesses(new[] { "rsync", "chia_plot" });
            var fc = files
                .Select(_ => new DirectoryFileCount(_.Path, _.Files.Length))
                .ToArray();
            switch (client.Properties.Program)
            {
                case PlotProgram.MadmaxPlotter:
                    var job = client.GetMadmaxPlotJob();
                    return new PlotterStatus(client.Name, null, fc, files, null, job, processes);
                case PlotProgram.Plotman:
                    var jobs = client.GetPlotJobs();
                    var cfg = client.ReadPlotManConfiguration();
                    return new PlotterStatus(client.Name, jobs, fc, files, cfg, null, processes);
                default:
                    return new PlotterStatus(client.Name, null, fc, files, null, null, processes);
            }
        }

        public static MadmaxPlotJobStatus? GetMadmaxPlotJob(this TargetMachine client)
        {
            if (!client.EnsureConnected()) return default;

            using var timeCmd = client.RunCommand(@"grep -o ""^Total .* sec"" ~/plotter/plot.log | sed -e ""s/^Total .* \([0-9]\+\.[0-9]\+\) sec$/\1/""");
            decimal[] times = timeCmd.Result.CleanSplit()
                .Select(_ => decimal.TryParse(_, out var number) ? (decimal?)number : null)
                .Where(_ => _ != null)
                .Select(_ => _ ?? 0)
                .ToArray();
            times = times.Length == 0 ? new decimal[] { -1 } : times;
            var stats = new MadmaxPlotStatistics(
                (int)times.Reverse().Take(5).Average(),
                (int)times.Max(),
                (int)times.Min(),
                -1);

            using var logCmd = client.RunCommand(@"tail -n 100 ~/plotter/plot.log");
            var phase = ParseMadmaxOutput(logCmd.Result);

            using var lastTimeCmd = client.RunCommand(@"stat -c '%y' ~/plotter/plot.log");
            var lastTime = DateTime.TryParse(lastTimeCmd.Result, out var lt) ? (DateTime?)lt : null;

            var job = new MadmaxPlotJob(phase, -1, lastTime, "Unknown");

            return new MadmaxPlotJobStatus(job, stats);

            static string ParseMadmaxOutput(string output)
            {
                // Example see end of the file
                var re = new Regex(@"\[P(?<phase>\d)(-\d)?\]( Table (?<table>\d))?");
                var lines = output.CleanSplit().Reverse().ToArray();
                foreach (var line in lines)
                {
                    var match = re.Match(line);
                    if (!match.Success) continue;

                    var phase = int.TryParse(match.Groups["phase"].Value, out var tp) ? tp : -1;
                    var table = int.TryParse(match.Groups["table"].Value, out var tt) ? tt : -1;
                    var (ep, esp) = (phase, table) switch
                    {
                        // P1: 1-7
                        (1, _) => (1, table),
                        // P2: 7-2
                        (2, _) => (2, 7 - table + 1),
                        // P3: 2-7
                        (3, _) => (3, table - 1),
                        // P4: Nothing
                        (4, _) => (4, 0),
                        (_, _) => (-1, -1),
                    };

                    return $"{ep}:{esp}";
                }

                return "-1";
            }
        }

        public static PlotmanJob[] GetPlotJobs(this TargetMachine client)
        {
            if (!client.EnsureConnected()) return Array.Empty<PlotmanJob>();

            using var pmCmd = client.RunCommand(@". ~/chia-blockchain/activate && plotman status");

            return ParsePlotStatusOutput(pmCmd.Result).ToArray();

            static IEnumerable<PlotmanJob> ParsePlotStatusOutput(string output)
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

                    yield return new PlotmanJob(idx, parts[0], parts[1], parts[2], parts[3], parts[4],
                        parts[5], parts[6], int.Parse(parts[7]), parts[9], parts[12]);
                    idx++;
                }
            }
        }

        public static string[] GetProcesses(this TargetMachine client, params string[] processes)
        {
            if (!client.EnsureConnected()) return Array.Empty<string>();

            using var cmd = client.RunCommand(@$"ps -e | egrep ""({string.Join("|", processes)})""");
            return cmd.Result
                .CleanSplit()
                .Select(line => processes.FirstOrDefault(_ => line.Contains(_)))
                .NotNull()
                .ToArray();
        }

        public static DirectoryFiles[] GetDirectoryFileCountCommand(this TargetMachine client, params string[] pathes)
        {
            return pathes
                .Select(_ => client.GetDirectoryFileCountCommand(_))
                .NotNull()
                .ToArray();
        }

        public static DirectoryFiles? GetDirectoryFileCountCommand(this TargetMachine client, string path)
        {
            if (!client.EnsureConnected()) return null;
            using var cmd = client.RunCommand(@$"ls {path}/*.plot | sort");
            var files = cmd.Result
                .CleanSplit()
                .Select(_ => _.Replace($"{path}/", ""))
                .ToArray();
            return new DirectoryFiles(path, files);
        }
    }

    public record DirectoryFiles(string Path, string[] Files);
    public record DirectoryFileCount(string Path, int Count);
    public record PlotterStatus(
        string Name,
        PlotmanJob[]? Jobs,
        DirectoryFileCount[] FileCounts,
        DirectoryFiles[] Files,
        PlotManConfiguration? Configuration,
        MadmaxPlotJobStatus? MadmaxJob,
        string[] Processes // rsync/chia_plot
        );
    public record MadmaxPlotJobStatus(MadmaxPlotJob Job, MadmaxPlotStatistics Statistics);
    public record MadmaxPlotJob(string Phase, int WallTime, DateTime? LastUpdateTime, string CopyingFile);
    public record MadmaxPlotStatistics(int AverageTime, int MaxTime, int MinTime, int DailyProduction);
    public record PlotmanJob(int Index, string Id, string K, string TempDir, string DestDir, string WallTime,
        string Phase, string TempSize, int Pid, string MemorySize, string IoTime);
}
/*
[P1] Table 1 took 17.8165 sec
[P1] Table 2 took 122.973 sec, found 4294926705 matches
[P2] max_table_size = 4294967296
[P2] Table 7 scan took 15.9252 sec
[P2] Table 7 rewrite took 76.7358 sec, dropped 0 entries (0 %)
[P2] Table 6 scan took 47.3812 sec
[P2] Table 6 rewrite took 57.1099 sec, dropped 581292549 entries (13.5346 %)
[P3-1] Table 2 took 63.2117 sec, wrote 3429365509 right entries
[P3-2] Table 2 took 35.8836 sec, wrote 3429365509 left entries, 3429365509 final
[P3-1] Table 3 took 67.2098 sec, wrote 3439887429 right entries
[P3-2] Table 3 took 38.5581 sec, wrote 3439887429 left entries, 3439887429 final
[P4] Starting to write C1 and C3 tables
[P4] Finished writing C1 and C3 tables
[P4] Writing C2 table
[P4] Finished writing C2 table
*/
/*
Multi-threaded pipelined Chia k32 plotter - 93ed0cb
Final Directory: /data/final/
Number of Plots: infinite
Crafting plot 1 out of -1
Process ID: 834850
Number of Threads: 40
Number of Buckets: 2^8 (256)
Pool Public Key:   8ff2ab1cadb205fd8805717dacda261ac77680be956c57657556300c932b25d3535a1c5006be56fc1a925c508ed44ee3
Farmer Public Key: 886a15a7ec49cad262b91fa18fda6f2a95a8fb0f67ffcf44e3d50469db223d3232eda26b38864f1793d718176690e5bc
Working Directory:   /data/tmp/
Working Directory 2: /data/tmp/
Plot Name: plot-k32-2021-06-18-14-29-e6a1f143e02629b65c453a1b5629281b08c7da04d4e4b640cb5f0f778e4bc9d0
[P1] Table 1 took 17.8165 sec
[P1] Table 2 took 122.973 sec, found 4294926705 matches
[P1] Table 3 took 184.258 sec, found 4294942819 matches
[P1] Table 4 took 222.611 sec, found 4294927809 matches
[P1] Table 5 took 211.907 sec, found 4294875679 matches
[P1] Table 6 took 181.016 sec, found 4294867936 matches
[P1] Table 7 took 134.074 sec, found 4294768192 matches
Phase 1 took 1074.73 sec
[P2] max_table_size = 4294967296
[P2] Table 7 scan took 15.9252 sec
[P2] Table 7 rewrite took 76.7358 sec, dropped 0 entries (0 %)
[P2] Table 6 scan took 47.3812 sec
[P2] Table 6 rewrite took 57.1099 sec, dropped 581292549 entries (13.5346 %)
[P2] Table 5 scan took 38.1733 sec
[P2] Table 5 rewrite took 49.6341 sec, dropped 761943075 entries (17.7407 %)
[P2] Table 4 scan took 35.8122 sec
[P2] Table 4 rewrite took 49.2373 sec, dropped 828858290 entries (19.2985 %)
[P2] Table 3 scan took 36.2175 sec
[P2] Table 3 rewrite took 48.2347 sec, dropped 855055390 entries (19.9084 %)
[P2] Table 2 scan took 34.745 sec
[P2] Table 2 rewrite took 52.556 sec, dropped 865561196 entries (20.1531 %)
Phase 2 took 558.322 sec
Wrote plot header with 268 bytes
[P3-1] Table 2 took 63.2117 sec, wrote 3429365509 right entries
[P3-2] Table 2 took 35.8836 sec, wrote 3429365509 left entries, 3429365509 final
[P3-1] Table 3 took 67.2098 sec, wrote 3439887429 right entries
[P3-2] Table 3 took 38.5581 sec, wrote 3439887429 left entries, 3439887429 final
[P3-1] Table 4 took 67.3402 sec, wrote 3466069519 right entries
[P3-2] Table 4 took 37.7018 sec, wrote 3466069519 left entries, 3466069519 final
[P3-1] Table 5 took 69.1943 sec, wrote 3532932604 right entries
[P3-2] Table 5 took 38.97 sec, wrote 3532932604 left entries, 3532932604 final
[P3-1] Table 6 took 68.9836 sec, wrote 3713575387 right entries
[P3-2] Table 6 took 42.2902 sec, wrote 3713575387 left entries, 3713575387 final
[P3-1] Table 7 took 86.6836 sec, wrote 4294768192 right entries
[P3-2] Table 7 took 57.7068 sec, wrote 4294768192 left entries, 4294768192 final
Phase 3 took 677.847 sec, wrote 21876598640 entries to final plot
[P4] Starting to write C1 and C3 tables
[P4] Finished writing C1 and C3 tables
[P4] Writing C2 table
[P4] Finished writing C2 table
Phase 4 took 69.8056 sec, final plot size is 108832382686 bytes
Total plot creation time was 2380.78 sec (39.6797 min)
Started copy to /data/final/plot-k32-2021-06-18-14-29-e6a1f143e02629b65c453a1b5629281b08c7da04d4e4b640cb5f0f778e4bc9d0.plot
Crafting plot 2 out of -1
Process ID: 834850
Number of Threads: 40
Number of Buckets: 2^8 (256)
Pool Public Key:   8ff2ab1cadb205fd8805717dacda261ac77680be956c57657556300c932b25d3535a1c5006be56fc1a925c508ed44ee3
Farmer Public Key: 886a15a7ec49cad262b91fa18fda6f2a95a8fb0f67ffcf44e3d50469db223d3232eda26b38864f1793d718176690e5bc
Working Directory:   /data/tmp/
Working Directory 2: /data/tmp/
Plot Name: plot-k32-2021-06-18-15-09-bee141598c3ad541303a90e508819f4f38779fa262aa9d64db8872112d4649fa
[P1] Table 1 took 23.883 sec
[P1] Table 2 took 119.157 sec, found 4295007460 matches
[P1] Table 3 took 170.453 sec, found 4294987319 matches
[P1] Table 4 took 212.373 sec, found 4294978255 matches
[P1] Table 5 took 206.526 sec, found 4295045309 matches
[P1] Table 6 took 185.24 sec, found 4294983597 matches
Copy to /data/final/plot-k32-2021-06-18-14-29-e6a1f143e02629b65c453a1b5629281b08c7da04d4e4b640cb5f0f778e4bc9d0.plot finished, took 964.486 sec, 107.612 MB/s avg.
[P1] Table 7 took 139.4 sec, found 4294951295 matches
Phase 1 took 1057.13 sec
[P2] max_table_size = 4295045309
[P2] Table 7 scan took 20.2329 sec
[P2] Table 7 rewrite took 69.4962 sec, dropped 0 entries (0 %)
[P2] Table 6 scan took 45.9737 sec
[P2] Table 6 rewrite took 64.182 sec, dropped 581284417 entries (13.534 %)
[P2] Table 5 scan took 36.4928 sec
[P2] Table 5 rewrite took 50.0354 sec, dropped 762032884 entries (17.7421 %)
[P2] Table 4 scan took 36.3844 sec
[P2] Table 4 rewrite took 53.8857 sec, dropped 828821652 entries (19.2975 %)
[P2] Table 3 scan took 37.2485 sec
[P2] Table 3 rewrite took 59.5918 sec, dropped 855070838 entries (19.9086 %)
[P2] Table 2 scan took 34.9463 sec
[P2] Table 2 rewrite took 58.584 sec, dropped 865595586 entries (20.1535 %)
Phase 2 took 583.603 sec
Wrote plot header with 268 bytes
[P3-1] Table 2 took 64.6106 sec, wrote 3429411874 right entries
[P3-2] Table 2 took 42.3469 sec, wrote 3429411874 left entries, 3429411874 final
[P3-1] Table 3 took 68.3279 sec, wrote 3439916481 right entries
[P3-2] Table 3 took 42.3766 sec, wrote 3439916481 left entries, 3439916481 final
[P3-1] Table 4 took 66.4019 sec, wrote 3466156603 right entries
[P3-2] Table 4 took 39.569 sec, wrote 3466156603 left entries, 3466156603 final
[P3-1] Table 5 took 68.9574 sec, wrote 3533012425 right entries
[P3-2] Table 5 took 43.0132 sec, wrote 3533012425 left entries, 3533012425 final
[P3-1] Table 6 took 71.4658 sec, wrote 3713699180 right entries
[P3-2] Table 6 took 41.6292 sec, wrote 3713699180 left entries, 3713699180 final
[P3-1] Table 7 took 86.0528 sec, wrote 4294951295 right entries
[P3-2] Table 7 took 56.2793 sec, wrote 4294951295 left entries, 4294951295 final
Phase 3 took 695.165 sec, wrote 21877147858 entries to final plot
[P4] Starting to write C1 and C3 tables
[P4] Finished writing C1 and C3 tables
[P4] Writing C2 table
[P4] Finished writing C2 table
Phase 4 took 69.1341 sec, final plot size is 108835451858 bytes
Total plot creation time was 2405.1 sec (40.085 min)
Started copy to /data/final/plot-k32-2021-06-18-15-09-bee141598c3ad541303a90e508819f4f38779fa262aa9d64db8872112d4649fa.plot
*/