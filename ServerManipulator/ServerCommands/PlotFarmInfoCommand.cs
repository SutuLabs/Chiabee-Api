namespace WebApi.Services.ServerCommands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using WebApi.Models;

    public static class PlotFarmInfoCommand
    {
        public static PlotInfo[] GetPlotFarmInfo(this TargetMachine client)
        {
            var rePlotId = new Regex("-(?<id>[0-9a-f]{64}).plot");

            if (!client.EnsureConnected()) return null;

            var host = client.ConnectionInfo.Host;
            var cmd = client.RunCommand(@$". ~/chia-blockchain/activate && chia plots show | grep ^/");

            return ParseListFile(cmd.Result).ToArray();

            IEnumerable<PlotInfo> ParseListFile(string filelist)
            {
                var dirs = filelist.ToArrayList();
                foreach (var dir in dirs)
                {
                    var dircmd = client.RunCommand($@"ls -l {dir}");
                    var list = dircmd.Result.ToArrayList();

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

        private static string[] ToArrayList(this string filelist) => filelist
            .Split("\n")
            .Select(_ => _.Trim())
            .Where(_ => !string.IsNullOrEmpty(_))
            .ToArray();
    }

    public record PlotInfo(string Filename, long Size, string PlotId, string Directory, string Host);
}