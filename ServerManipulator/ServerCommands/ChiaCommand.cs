namespace WebApi.Services.ServerCommands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Renci.SshNet;
    using WebApi.Models;

    public static class ChiaCommand
    {
        public static NodeStatus GetNodeStatus(this TargetMachine client)
        {
            if (!client.EnsureConnected()) return null;
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

        public static FarmerStatus GetFarmerStatus(this TargetMachine client)
        {
            if (!client.EnsureConnected()) return null;
            var farmCmd = client.RunCommand(@". ~/chia-blockchain/activate && chia farm summary");
            return ParseFarmStatus(farmCmd.Result);

            static FarmerStatus ParseFarmStatus(string output)
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
                    new StatusDefinition(nameof(FarmerStatus.Status), "Farming status"),
                    new StatusDefinition(nameof(FarmerStatus.TotalFarmed), "Total chia farmed"),
                    new StatusDefinition(nameof(FarmerStatus.TxFees), "User transaction fees"),
                    new StatusDefinition(nameof(FarmerStatus.Rewards), "Block rewards"),
                    new StatusDefinition(nameof(FarmerStatus.LastFarmedHeight), "Last height farmed"),
                    new StatusDefinition(nameof(FarmerStatus.PlotCount), "Plot count"),
                    new StatusDefinition(nameof(FarmerStatus.TotalSize), "Total size of plots"),
                    new StatusDefinition(nameof(FarmerStatus.Space), "Estimated network space"),
                    new StatusDefinition(nameof(FarmerStatus.ExpectedToWin), "Expected time to win")
                    )
                    .ToDictionary(_ => _.Key, _ => _.Value);
                return new FarmerStatus(
                    pairs[nameof(FarmerStatus.Status)],
                    GetDecimal(pairs[nameof(FarmerStatus.TotalFarmed)]),
                    GetDecimal(pairs[nameof(FarmerStatus.TxFees)]),
                    GetDecimal(pairs[nameof(FarmerStatus.Rewards)]),
                    GetInt(pairs[nameof(FarmerStatus.LastFarmedHeight)]),
                    GetInt(pairs[nameof(FarmerStatus.PlotCount)]),
                    pairs[nameof(FarmerStatus.TotalSize)],
                    pairs[nameof(FarmerStatus.Space)],
                    pairs[nameof(FarmerStatus.ExpectedToWin)]
                    );
            }
        }

        private static int? GetInt(string input )
        {
            if (input == "Unknown") return null;
            if (!int.TryParse(input, out var result)) return null;
            return result;
        }

        private static decimal? GetDecimal(string input )
        {
            if (input == "Unknown") return null;
            if (!decimal.TryParse(input, out var result)) return null;
            return result;
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

    public record FarmerNodeStatus(string Name, FarmerStatus Farmer, NodeStatus Node);
    public record NodeStatus(string Status, DateTime Time, int Height, string Space, string Difficulty, string Iterations, string TotalIterations);
    public record FarmerStatus(string Status, decimal? TotalFarmed, decimal? TxFees, decimal? Rewards, int? LastFarmedHeight, int? PlotCount, string TotalSize, string Space, string ExpectedToWin);
}