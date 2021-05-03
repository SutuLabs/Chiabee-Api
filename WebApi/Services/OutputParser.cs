namespace WebApi.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Renci.SshNet;

    public static class OutputParser
    {
        public static PlotJob[] GetPlotStatus(this SshClient client)
        {
            client.EnsureConnected();
            var pmCmd = client.RunCommand(@". ~/chia-blockchain/activate && plotman status");

            return ParsePlotStatusOutput(pmCmd.Result).ToArray();

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

        public static ServerStatus GetServerStatus(this SshClient client)
        {
            client.EnsureConnected();
            var topCmd = client.RunCommand(@"top -b1n1 -E G |grep ""Cpu\|Mem \|Tasks""");
            var output = topCmd.Result;

            return new ServerStatus()
            {
                Process = ParseProcessState(output),
                Memory = ParseMemoryState(output),
                Cpus = ParseCpuState(output).OrderBy(_ => _.Index).Select(_ => _.Idle).ToArray(),
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

        public static IEnumerable<StatusPair> ParsePairs(string output, params StatusDefinition[] defs)
        {
            var lines = output.Split("\n", StringSplitOptions.RemoveEmptyEntries).Select(_ => _.Trim()).ToArray();
            foreach (var line in lines)
            {
                var parts = line.Split(":", StringSplitOptions.RemoveEmptyEntries).Select(_ => _.Trim()).ToArray();
                if (parts.Length == 2)
                {
                    var def = defs.FirstOrDefault(_ => _.Text == parts[0]);
                    if (def != null)
                        yield return new StatusPair(def.Key, parts[1]);
                }
            }
        }

        public record StatusPair(string Key, string Value);
        public record StatusDefinition(string Key, string Text);


        public static NodeStatus GetNodeStatus(this SshClient client)
        {
            client.EnsureConnected();
            var nodeCmd = client.RunCommand(@". ~/chia-blockchain/activate && chia show -s");
            return ParseNodeStatus(nodeCmd.Result);

            static NodeStatus ParseNodeStatus(string output)
            {
                //(venv) sutu@farmer7700:/farm/01$ chia show -s
                //Current Blockchain Status: Full Node Synced

                //Peak: Hash: 17c16f3b3e6052d9376697496eeb30741ea49be6f918de8ef74da40c1b402ab7
                //      Time: Sun May 02 2021 03:36:02 UTC Height:     217718

                //Estimated network space: 1528.527 PiB
                //Current difficulty: 99
                //Current VDF sub_slot_iters: 110624768
                //Total iterations since the start of the blockchain: 708896247888

                //  Height: |   Hash:
                //   217718 | 17c16f3b3e6052d9376697496eeb30741ea49be6f918de8ef74da40c1b402ab7
                var reState = new Regex(@"Time: (?<time>.*? UTC) +Height: +(?<height>\d+)");
                var match = reState.Match(output);
                var time = DateTime.Parse(match.Groups["time"].Value.Replace(" UTC", "Z"));
                var height = int.Parse(match.Groups["height"].Value);
                var pairs = ParsePairs(output,
                    new StatusDefinition(nameof(NodeStatus.Status), "Current Blockchain Status"),
                    new StatusDefinition(nameof(NodeStatus.Space), "Estimated network space"),
                    new StatusDefinition(nameof(NodeStatus.Difficulty), "Current difficulty"),
                    new StatusDefinition(nameof(NodeStatus.Iterations), "Current VDF sub_slot_iters"),
                    new StatusDefinition(nameof(NodeStatus.TotalIterations), "Total iterations since the start of the blockchain")
                    )
                    .ToDictionary(_ => _.Key, _ => _.Value);
                return new NodeStatus(
                    pairs[nameof(NodeStatus.Status)],
                    time,
                    height,
                    pairs[nameof(NodeStatus.Space)],
                    pairs[nameof(NodeStatus.Difficulty)],
                    pairs[nameof(NodeStatus.Iterations)],
                    pairs[nameof(NodeStatus.TotalIterations)]
                    );
            }
        }

        public static FarmStatus GetFarmStatus(this SshClient client)
        {
            client.EnsureConnected();
            var farmCmd = client.RunCommand(@". ~/chia-blockchain/activate && chia farm summary");
            return ParseFarmStatus(farmCmd.Result);

            static FarmStatus ParseFarmStatus(string output)
            {
                //(venv) sutu @farmer7700:/farm/01$ chia farm summary
                // Farming status: Farming
                // Total chia farmed: 0.0
                //User transaction fees: 0.0
                //Block rewards: 0.0
                //Last height farmed: 0
                //Plot count: 11
                //Total size of plots: 1.089 TiB
                //Estimated network space: 1528.527 PiB
                //Expected time to win: 9 months and 3 weeks
                //Note: log into your key using 'chia wallet show' to see rewards for each key
                var pairs = ParsePairs(output,
                    new StatusDefinition(nameof(FarmStatus.Status), "Farming status"),
                    new StatusDefinition(nameof(FarmStatus.TotalFarmed), "Total chia farmed"),
                    new StatusDefinition(nameof(FarmStatus.TxFees), "User transaction fees"),
                    new StatusDefinition(nameof(FarmStatus.Rewards), "Block rewards"),
                    new StatusDefinition(nameof(FarmStatus.LastFarmedHeight), "Last height farmed"),
                    new StatusDefinition(nameof(FarmStatus.PlotCount), "Plot count"),
                    new StatusDefinition(nameof(FarmStatus.TotalSize), "Total size of plots"),
                    new StatusDefinition(nameof(FarmStatus.Space), "Estimated network space"),
                    new StatusDefinition(nameof(FarmStatus.ExpectedToWin), "Expected time to win")
                    )
                    .ToDictionary(_ => _.Key, _ => _.Value);
                return new FarmStatus(
                    pairs[nameof(FarmStatus.Status)],
                    decimal.Parse(pairs[nameof(FarmStatus.TotalFarmed)]),
                    decimal.Parse(pairs[nameof(FarmStatus.TxFees)]),
                    decimal.Parse(pairs[nameof(FarmStatus.Rewards)]),
                    int.Parse(pairs[nameof(FarmStatus.LastFarmedHeight)]),
                    int.Parse(pairs[nameof(FarmStatus.PlotCount)]),
                    pairs[nameof(FarmStatus.TotalSize)],
                    pairs[nameof(FarmStatus.Space)],
                    pairs[nameof(FarmStatus.ExpectedToWin)]
                    );
            }
        }

        private static void EnsureConnected(this SshClient client)
        {
            if (!client.IsConnected)
            {
                client.Connect();
            }
        }
    }
}