namespace WebApi.Services.ServerCommands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Renci.SshNet;

    public static class ChiaCommand
    {
        public static NodeStatus GetNodeStatus(this SshClient client)
        {
            client.EnsureConnected();
            var nodeCmd = client.RunCommand(@". ~/chia-blockchain/activate && chia show -s");
            return ParseNodeStatus(nodeCmd.Result);

            static NodeStatus ParseNodeStatus(string output)
            {
                //Current Blockchain Status: Full Node syncing to block 254351
                //Currently synced to block: 253528
                //Current Blockchain Status: Not Synced. Peak height: 253528

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
                    pairs.ContainsKey(nameof(NodeStatus.Status)) ? pairs[nameof(NodeStatus.Status)] : "Special",
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

        private static IEnumerable<StatusPair> ParsePairs(string output, params StatusDefinition[] defs)
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
    }

    public record FarmServerStatus : ServerStatus
    {
        public FarmServerStatus()
        {
        }

        public FarmServerStatus(ServerStatus serverStatus, NodeStatus node, FarmStatus farm)
        {
            this.Farm = farm;
            this.Node = node;
            this.Cpus = serverStatus.Cpus;
            this.Memory = serverStatus.Memory;
            this.Process = serverStatus.Process;
            this.Name = serverStatus.Name;
            this.Disks = serverStatus.Disks;
        }

        public NodeStatus Node { get; init; }
        public FarmStatus Farm { get; init; }
    }

    public record NodeStatus(string Status, DateTime Time, int Height, string Space, string Difficulty, string Iterations, string TotalIterations);
    public record FarmStatus(string Status, decimal TotalFarmed, decimal TxFees, decimal Rewards, int LastFarmedHeight, int PlotCount, string TotalSize, string Space, string ExpectedToWin);
}