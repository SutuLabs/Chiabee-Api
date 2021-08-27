namespace WebApi.Services.ServerCommands
{
    using WebApi.Models;

    public static class NetworkStatusCommand
    {
        public static int? GetNetworkIoSpeed(this TargetMachine client)
        {
            if (!client.EnsureConnected()) return null;
            const decimal duration = 0.1m;
            var ethBaseName = "eno";

            var sum = 0;
            for (int i = 0; i < client.Properties.Hosts.Length; i++)
            {
                using var cmd = client.ExecuteCommand(GetCmdText($"{ethBaseName}{i + 1}"));
                var cur = int.TryParse(cmd.Result, out var b) ? (int?)(b / duration) : null;
                sum += cur ?? 0;
            }

            return sum;

            static string GetCmdText(string ethName) =>
                ReadCommand("sr1", "r", ethName) +
                ReadCommand("st1", "t", ethName) +
                $"sleep {duration};" +
                ReadCommand("sr2", "r", ethName) +
                ReadCommand("st2", "t", ethName) +
                $"echo $(($sr2-$sr1+$st2-$st1))";

            static string ReadCommand(string par, string direction, string ethName) =>
                $"read {par} < /sys/class/net/{ethName}/statistics/{direction}x_bytes;";
        }
    }
}