namespace WebApi.Services.ServerCommands
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Renci.SshNet;
    using WebApi.Models;

    public static class WalletCommand
    {
        // range is some random number, not strict number
        private static readonly Regex fingerRegex = new(@"\d{4,18}");
        private static readonly Regex addressRegex = new(@"xch[0-9a-z]{50,65}");
        private static readonly Regex txRegex = new(@"0x[0-9a-f]{64}");

        public static async Task<string> Send(this TargetMachine client, string walletFinger, string address, decimal amount)
        {
            if (!client.EnsureConnected()) return null;

            if (!fingerRegex.IsMatch(walletFinger)) return null;
            if (amount <= 0) return null;
            if (!addressRegex.IsMatch(address)) return null;

            var (exit, result) = client.PerformCommand(@$". ~/chia-blockchain/activate && chia wallet send -f {walletFinger} -a {amount} -m 0 -t {address}");
            if (exit != 0) return null;

            var match = Regex.Match(result, @"-tx (?<tx>0x[0-9a-f]{64})");
            if (!match.Success) return null;

            return match.Groups["tx"].Value;

            /*
            (venv) sutu@chiafarm1:~$ chia wallet send -f 3091007504 -a 0.0010017 -m 0 -t xch1vutlxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
            Submitting transaction...
            Transaction submitted to nodes: [('8550ceb3f74a19d0a0b661882ad66b8dc64a35f719d704f906a8d3c71b769c02', 1, None)]
            Do chia wallet get_transaction -f 3091007504 -tx 0x68301dfc58eb05ed4fb847013c9abf02c4f7724a0b0f8d05ca54a098f7e8ca19 to get status
             */
        }

        public static async Task<WalletBalance> GetBalance(this TargetMachine client, string walletFinger)
        {
            if (!client.EnsureConnected()) return null;

            if (!fingerRegex.IsMatch(walletFinger)) return null;

            var (exit, result) = client.PerformCommand(@$". ~/chia-blockchain/activate && chia wallet show -f {walletFinger}");
            if (exit != 0) return null;
            /*
            (venv) sutu@chiafarm1:~$ chia wallet show -f 3091007504
            Wallet height: 723753
            Sync status: Synced
            Balances, fingerprint: 3091007504
            Wallet ID 1 type STANDARD_WALLET
               -Total Balance: 5.699003300111 xch (5699003300111 mojo)
               -Pending Total Balance: 5.699003300111 xch (5699003300111 mojo)
               -Spendable: 5.699003300111 xch (5699003300111 mojo)
             */
            var pairs = CommandHelper.ParsePairs(result,
                new StatusDefinition(nameof(WalletBalance.IsSynced), "Sync status"),
                new StatusDefinition(nameof(WalletBalance.Fingerprint), "Balances, fingerprint"),
                new StatusDefinition(nameof(WalletBalance.Balance), "-Total Balance"),
                new StatusDefinition(nameof(WalletBalance.Pending), "-Pending Total Balance"),
                new StatusDefinition(nameof(WalletBalance.Spendable), "-Spendable"),
                new StatusDefinition(nameof(WalletBalance.Height), "Wallet height")
                )
                .ToDictionary(_ => _.Key, _ => _.Value);
            return new WalletBalance(
                pairs[nameof(WalletBalance.IsSynced)] == "Synced",
                pairs[nameof(WalletBalance.Fingerprint)],
                GetLong(pairs[nameof(WalletBalance.Height)]) ?? -1,
                null,
                ParseBalance(pairs[nameof(WalletBalance.Balance)]),
                ParseBalance(pairs[nameof(WalletBalance.Pending)]),
                ParseBalance(pairs[nameof(WalletBalance.Spendable)])
                );

            static decimal ParseBalance(string s)
            {
                var xch = Regex.Replace(s, @"\(\d* mojo\)", "").Trim();
                return ParseXch(xch);
            }
        }

        public static async Task<WalletTx[]> GetTxs(this TargetMachine client, string walletFinger)
        {
            if (!client.EnsureConnected()) return null;

            if (!fingerRegex.IsMatch(walletFinger)) return null;

            var (exit, result) = client.PerformCommand(@$". ~/chia-blockchain/activate && yes c | chia wallet get_transactions -f {walletFinger}");
            if (exit != 0)
                return null;
            else
                return ParseTxs(result).ToArray();
        }

        public static async Task<WalletTx> GetTx(this TargetMachine client, string walletFinger, string txId)
        {
            if (!client.EnsureConnected()) return null;

            if (!fingerRegex.IsMatch(walletFinger)) return null;
            if (!txRegex.IsMatch(txId)) return null;

            var (exit, result) = client.PerformCommand(@$". ~/chia-blockchain/activate && chia wallet get_transaction -f {walletFinger} -tx {txId}");
            if (exit != 0)
                return null;
            else
                return ParseTxs(result).FirstOrDefault();
        }

        private record LeadingHandler(string Leading, Func<string, WalletTx, WalletTx> Parse);
        private static IEnumerable<WalletTx> ParseTxs(string input)
        {
            WalletTx tx = null;

            var leadings = new[] {
                new LeadingHandler("Status", (s, tx) => tx with { Status = s }),
                new LeadingHandler("Amount", (s, tx) => tx with { Amount = ParseXch(s) }),
                new LeadingHandler("To address", (s, tx) => tx with { Target = s }),
                new LeadingHandler("Created at", (s, tx) => tx with { Created = DateTime.Parse(s) }),
            };
            /*
            Transaction 68301dfc58eb05ed4fb847013c9abf02c4f7724a0b0f8d05ca54a098f7e8ca19
            Status: Confirmed
            Amount: 0.0010017 xch
            To address: xch1vutlnjnugh9umcpw300vdw2nqegh8xh7jtmmuzxd3hgh8rje3tcqcafpw0
            Created at: 2021-08-07 00:34:02
             */
            foreach (var line in input.CleanSplit())
            {
                if (line.StartsWith("Transaction"))
                {
                    if (tx != null) yield return tx;
                    var id = "0x" + line[("Transaction".Length + 1)..];
                    tx = new WalletTx(id, null, 0, null, DateTime.MinValue);
                }
                else if (leadings.Any(_ => line.StartsWith(_.Leading)))
                {
                    var val = line[(line.IndexOf(":") + 1)..].Trim();
                    var l = leadings.First(_ => line.StartsWith(_.Leading));
                    tx = l.Parse(val, tx);
                }
                else
                {
                    if (tx != null)
                    {
                        yield return tx;
                        tx = null;
                    }
                }
            }

            if (tx != null) yield return tx;
        }
        private static decimal ParseXch(string s)
            => decimal.TryParse(s.Replace("xch", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var am)
                ? am
                : -1;

        private static long? GetLong(string input)
        {
            if (!long.TryParse(input, out var result)) return null;
            return result;
        }
    }
    public record WalletBalance(bool IsSynced, string Fingerprint, long Height, string Type, decimal Balance, decimal Pending, decimal Spendable);
    public record WalletTx(string Id, string Status, decimal Amount, string Target, DateTime Created);
}