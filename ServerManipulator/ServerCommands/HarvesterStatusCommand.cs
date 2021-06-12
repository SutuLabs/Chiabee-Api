namespace WebApi.Services.ServerCommands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Renci.SshNet;
    using WebApi.Models;

    public static class HarvesterStatusCommand
    {
        public static HarvesterStatus GetHarvesterStatus(this TargetMachine client)
        {
            if (!client.EnsureConnected()) return null;

            var el = GetEligibleInfo(client);
            var abs = GetAbnormalFarmlands(client);

            return new HarvesterStatus(client.Name, el?.Total, el?.Time, abs);
        }

        private static string[] GetAbnormalFarmlands(TargetMachine client)
        {
            const string bdir = "/farm/";
            using var cmd = client.RunCommand($"ls {bdir} | sort");
            var lines = cmd.Result
                .CleanSplit();

            return GetAbnormals(client, lines).AsParallel().ToArray();

            static IEnumerable<string> GetAbnormals(TargetMachine client, string[] lines)
            {
                foreach (var line in lines)
                {
                    var dir = bdir + line;
                    using var trycmd = client.RunCommand($"touch {dir}");
                    if (trycmd.ExitStatus != 0) yield return dir;
                }
            }
        }

        private static EligibleFarmerEvent GetEligibleInfo(TargetMachine client)
        {
            using var cmd = client.RunCommand("tail -n 50 ~/.chia/mainnet/log/debug.log | grep eligible");
            return cmd.Result
                .CleanSplit()
                .Reverse()
                .Select(_ => ParseLine(_))
                .FirstOrDefault(_ => _ != null);
        }

        private static EligibleFarmerEvent ParseLine(string line)
        {
            // 2021-05-07T22:32:07.400 harvester chia.harvester.harvester: INFO     0 plots were eligible for farming 7ffcbb648f... Found 0 proofs. Time: 0.00295 s. Total 119 plots
            var re = new Regex(@"(?<time>[0-9\-]{10}T[0-9:]{8}\.[0-9]{3}) *(?<name>[\w\.]*) *(?<comp>[\w\.]*) *: *(?<level>\w*) *(?<log>.*)$");
            var match = re.Match(line);
            if (!match.Success) return null;

            var level = match.Groups["level"].Value;
            var time = DateTime.Parse(match.Groups["time"].Value);
            var log = match.Groups["log"].Value;

            //0 plots were eligible for farming 9483499d77... Found 0 proofs. Time: 0.03114 s. Total 119 plots
            var reEligible = new Regex(@"(?<plots>\d+) plots were eligible for farming (?<id>[\w\d]+)... Found (?<proofs>\d+) proofs. Time: (?<duration>\d+\.\d+) s. Total (?<total>\d+) plots");
            var m = reEligible.Match(log);
            return new EligibleFarmerEvent(time,
                int.Parse(m.Groups["plots"].Value),
                int.Parse(m.Groups["proofs"].Value),
                decimal.Parse(m.Groups["duration"].Value),
                int.Parse(m.Groups["total"].Value)
                );
        }
    }

    public record HarvesterStatus(string Name, int? TotalPlot, DateTime? LastPlotTime, string[] AbnormalFarmlands);
}