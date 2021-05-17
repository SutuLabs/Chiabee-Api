namespace WebApi.Services.ServerCommands
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Renci.SshNet;

    public static class TailChiaLogCommand
    {
        public static void StartTailChiaLog(this SshClient client, Action<ErrorEvent> errorRaised, Action<EligibleFarmerEvent> eventRaised)
        {
            client.EnsureConnected();

            var cmd = client.CreateCommand("tail -n 10000 -F ~/.chia/mainnet/log/debug.log");

            Task.Factory.StartNew(() => Ta(cmd, errorRaised, eventRaised), TaskCreationOptions.LongRunning);
        }

        private static void Ta(SshCommand cmd, Action<ErrorEvent> errorRaised, Action<EligibleFarmerEvent> eventRaised)
        {
            var result = cmd.BeginExecute();

            using var reader = new StreamReader(cmd.OutputStream, Encoding.UTF8, true, 1024, true);
            while (!result.IsCompleted || !reader.EndOfStream)
            {
                string line = reader.ReadLine();
                if (line == null) continue;

                // 2021-05-07T22:32:07.400 harvester chia.harvester.harvester: INFO     0 plots were eligible for farming 7ffcbb648f... Found 0 proofs. Time: 0.00295 s. Total 119 plots
                var re = new Regex(@"(?<time>[0-9\-]{10}T[0-9:]{8}\.[0-9]{3}) *(?<name>[\w\.]*) *(?<comp>[\w\.]*) *: *(?<level>\w*) *(?<log>.*)$");
                var match = re.Match(line);
                if (!match.Success) continue;

                var level = match.Groups["level"].Value;
                var time = DateTime.Parse(match.Groups["time"].Value);
                var log = match.Groups["log"].Value;

                var reToPeer = new Regex(@"-> (?<command>[\w\d_]+) to peer");
                var reFromPeer = new Regex(@"<- (?<command>[\w\d_]+) from peer");
                var reFrom = new Regex(@"<- (?<command>[\w\d_]+) from:");

                //0 plots were eligible for farming 9483499d77... Found 0 proofs. Time: 0.03114 s. Total 119 plots
                var reEligible = new Regex(@"(?<plots>\d+) plots were eligible for farming (?<id>[\w\d]+)... Found (?<proofs>\d+) proofs. Time: (?<duration>\d+\.\d+) s. Total (?<total>\d+) plots");

                if (level != "INFO")
                {
                    errorRaised?.Invoke(new ErrorEvent(time, level, log));
                }
                else if (reToPeer.Match(log).Success)
                {
                    //-> respond_block to peer 92.181.84.55 a0253668c8d2031d404ae90c1fda32b56258bc25954185026ae14cebc2faaed4
                    //ignore
                }
                else if (reFromPeer.Match(log).Success)
                {
                    //<- request_block from peer 0ba197f20af8a96ca3082f83e2da9e162a7bb6e07121b961a7e441fffa02a654 50.47.107.61
                    //ignore
                }
                else if (reFrom.Match(log).Success)
                {
                    //<- respond_block from: 193.6.41.248:8444
                    //ignore
                }
                else if (reEligible.Match(log).Success)
                {
                    var m = reEligible.Match(log);
                    eventRaised?.Invoke(new EligibleFarmerEvent(time,
                        int.Parse(m.Groups["plots"].Value),
                        int.Parse(m.Groups["proofs"].Value),
                        decimal.Parse(m.Groups["duration"].Value),
                        int.Parse(m.Groups["total"].Value)
                        ));
                }
                //else if (log.StartsWith(""))
                //{
                //    //ignore
                //}
                else if (log.StartsWith("Duplicate compact proof. Height:"))
                {
                    //Duplicate compact proof. Height: 26694. Header hash: 706c7d47a0cf2d11b9ccb8a4490ece6cd480d47c287136539e5ec76e5f32abd7.
                    //ignore
                }
                //It took 0.008915185928344727 to pre validate transaction
                //add_spendbundle took 0.013428211212158203 seconds
                //⏲️  Finished signage point 16/64: 7aa3d8ba801f9426be51faeb516f839ff50e8bd355c0692e4a82ce7b8ffaa290
                //Searching directories ['/farm/01/plots', '/farm/02/plots', '/mnt/usb/final', '/farm/03/plots', '/farm/04/plots', '/farm/05/plots', '/farm/06/plots', '/farm/07/plots', '/farm/08/plots', '/farm/09/plots', '/farm/10/plots', '/farm/11/plots', '/farm/12/plots']
                //Loaded a total of 119 plots of size 11.779274391036779 TiB, in 0.027655363082885742 seconds
                //0 plots were eligible for farming 9483499d77... Found 0 proofs. Time: 0.03114 s. Total 119 plots
                //Added unfinished_block 3d6a6dec0138182d0eb9b4e9effd733a38de12e6621b5f47ece6ac9834a06cf7, not farmed by us, SP: 16 time: 4.256900787353516Pool pk xch1fkrxcmd4kqfshvm8wejddj4xv5fwlqjq0u7eh8gt64fwg48vugqs37fm5l
                //🌱 Updated peak to height 247359, weight 9429058, hh d1b84f9405768f8e71fccf4d53dc0a1eaa0c50d23bc93385d2e524ffcf272388, forked at 247358, rh: 38c5e954f95bef8e6f1ddbf79d1e0e4f54bbc924eb8ccd8897054128d78fde48, total iters: 801102270362, overflow: False, deficit: 14, difficulty: 182, sub slot iters: 110624768, Generator size: No tx, Generator ref list size: No tx
                //💰 Updated wallet peak to height 247359, weight 9429058,
                //Already compactified block: e2357515f4a6a4cf7dd8ee035dae93bbaa5a790faea3602680dff12063156909. Ignoring.
                //⏲️  Finished signage point 17/64: f8bf8e38cb5efb06bf86dcde481935ffb7c52f160f379045f0c9b6e1423c6786
                //0 plots were eligible for farming 9483499d77... Found 0 proofs. Time: 0.00290 s. Total 119 plots
                //⏲️  Finished signage point 18/64: 2870fac8262c46e4f8718c7aca6c67036c9c2774625b0cdfe78df07b99c05099
                //1 plots were eligible for farming 9483499d77... Found 0 proofs. Time: 0.12953 s. Total 119 plots
                else
                {
                    Debug.WriteLine($"{log}");
                }
            }

            cmd.EndExecute(result);
        }
    }

    public record FarmerEvent(DateTime Time);
    public record EligibleFarmerEvent(DateTime Time, int EligibleNumber, int Proofs, decimal Duration, int Total) : FarmerEvent(Time);
    public record ErrorEvent(DateTime Time, string Level, string Error);
}