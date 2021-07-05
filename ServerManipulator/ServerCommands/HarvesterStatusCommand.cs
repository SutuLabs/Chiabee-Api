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

            using var chiacmd = client.RunCommand($". ~/chia-blockchain/activate && chia plots show | grep ^/");
            var chiaFarms = chiacmd.Result
                .CleanSplit();
            using var dfcmd = client.RunCommand(@"df | grep -o ""/farm/.*""");
            var dfFarms = dfcmd.Result
                .CleanSplit();

            var fls = GetAbnormalFarmlands(client, chiaFarms, dfFarms);
            var (parts, _) = client.GetHarvesterBlockInfo();
            var dparts = parts
                .Where(_ => string.IsNullOrWhiteSpace(_.MountPoint) && !string.IsNullOrWhiteSpace(_.Label))
                .Select(_ => _.Label)
                .Except(fls.Select(_ => _.Name.Replace("/farm/", "")))
                .ToArray();

            var abs = new AbnormalFarmlandList(
                fls.Where(_ => _.Type == FarmlandStatusType.IoError).Select(_ => _.Name).ToArray(),
                fls.Where(_ => _.Type == FarmlandStatusType.Missing).Select(_ => _.Name).ToArray(),
                fls.Where(_ => _.Type == FarmlandStatusType.Uninhabited).Select(_ => _.Name).ToArray()
                );
            return new HarvesterStatus(client.Name, el?.Total, el?.Time, abs, dparts);
        }

        private static FarmlandStatus[] GetAbnormalFarmlands(TargetMachine client, string[] chiaFarms, string[] dfFarms)
        {
            const string tempfile = "onlyatestfileforiotest";

            var all = chiaFarms.Concat(dfFarms).Distinct().ToArray();
            var missings = chiaFarms
                .Except(dfFarms)
                .Select(_ => new FarmlandStatus(_, FarmlandStatusType.Missing));
            var uninhabiteds = dfFarms
                .Except(chiaFarms)
                .Select(_ => new FarmlandStatus(_, FarmlandStatusType.Uninhabited));
            var errors = GetAbnormals(client, all)
                .AsParallel()
                .Select(_ => new FarmlandStatus(_, FarmlandStatusType.IoError))
                .ToArray();

            return missings
                .Concat(uninhabiteds)
                .Concat(errors)
                .ToArray();

            static IEnumerable<string> GetAbnormals(TargetMachine client, string[] lines)
            {
                foreach (var dir in lines)
                {
                    using var trycmd = client.RunCommand($"touch {dir}/{tempfile} && rm {dir}/{tempfile}");
                    if (trycmd.ExitStatus != 0) yield return dir;
                }
            }
        }

        private static EligibleFarmerEvent GetEligibleInfo(TargetMachine client)
        {
            using var cmd = client.RunCommand("tac ~/.chia/mainnet/log/debug.log | grep -m1 'plots were eligible for farming'");
            return cmd.Result
                .CleanSplit()
                .Reverse()
                .Select(_ => ParseLine(_, client.Name))
                .FirstOrDefault(_ => _ != null);
        }

        private static EligibleFarmerEvent ParseLine(string line, string machineName)
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
                machineName,
                int.Parse(m.Groups["plots"].Value),
                int.Parse(m.Groups["proofs"].Value),
                decimal.Parse(m.Groups["duration"].Value),
                int.Parse(m.Groups["total"].Value)
                );
        }
    }

    public record HarvesterStatus(string Name, int? TotalPlot, DateTime? LastPlotTime, AbnormalFarmlandList AbnormalFarmlands, string[] DanglingPartitions);
    public record AbnormalFarmlandList(string[] IoErrors, string[] Missings, string[] Uninhabiteds)
    {
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public string[] All => this.IoErrors.Concat(Missings).Concat(Uninhabiteds).ToArray();
    }
    public record FarmlandStatus(string Name, FarmlandStatusType Type);

    public enum FarmlandStatusType
    {
        Missing,
        IoError,
        Uninhabited,
    }
}