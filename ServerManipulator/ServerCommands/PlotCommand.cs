namespace WebApi.Services.ServerCommands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using WebApi.Models;

    public static class PlotCommand
    {
        public static PlotInfo[] GetPlotFarmInfo(this TargetMachine client)
        {
            var rePlotId = new Regex("-(?<id>[0-9a-f]{64}).plot");

            if (!client.EnsureConnected()) return null;

            var host = client.ConnectionInfo.Host;
            using var cmd = client.ExecuteCommand(@$". ~/chia-blockchain/activate && chia plots show | grep ^/");

            return ParseListFile(cmd.Result).ToArray();

            IEnumerable<PlotInfo> ParseListFile(string filelist)
            {
                var dirs = filelist.CleanSplit();
                foreach (var dir in dirs)
                {
                    using var dircmd = client.ExecuteCommand($@"ls -al {dir}");
                    var list = dircmd.Result.CleanSplit();

                    for (int i = 0; i < list.Length; i++)
                    {
                        var item = list[i];
                        // only process file, ignore directory/`total`/empty-line
                        if (!item.StartsWith("-")) continue;

                        // -rw-r--r--  1 sutu sutu 108808198171 May 31 18:28 plot-k32-2021-05-31-04-48-1a44ef45da6c5a34a7aea936b28b9b9b951994fbc5d6fa87fcd3e4202caeaf4a.plot
                        var segs = item.Split(" ", StringSplitOptions.RemoveEmptyEntries);
                        var size = long.Parse(segs[4]);
                        var filename = segs[8];
                        var idMatch = rePlotId.Match(filename);
                        var id = idMatch.Success ? idMatch.Groups["id"].Value : null;

                        yield return new PlotInfo(filename, size, id, dir, host);
                    }
                }
            }
        }

        public static bool RemovePlots(this TargetMachine client, string[] plots)
        {
            // /farm/A238/plot-k32-2021-07-03-20-58-a4ebd38acd57bebd14c87b9737dc432ddac2dd37d643392c3f3138fc0563dc55.plot
            var re = new Regex(@"/farm/A\d{1,4}/plot-k\d{2,3}-\d{4}(-\d{2}){4}-[0-9a-f]{64}.plot");
            if (plots.Any(plot => !re.IsMatch(plot))) return false;
            var cmds = string.Join("\n", plots.Select(_ => $"rm {_}"));
            var (code, result) = client.PerformScript(cmds);
            return code == 0;
        }
    }

    public record PlotInfo(string Filename, long Size, string PlotId, string Directory, string Host);
}