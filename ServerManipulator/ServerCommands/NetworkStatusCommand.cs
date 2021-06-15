namespace WebApi.Services.ServerCommands
{
    using WebApi.Models;

    public static class NetworkStatusCommand
    {
        public static int? GetNetworkIoSpeed(this TargetMachine client)
        {
            if (!client.EnsureConnected()) return null;
            var duration = 0.1;
            var ethName = "eno1";
            var cmdText =
                ReadCommand("sr1", "r", ethName) +
                ReadCommand("st1", "t", ethName) +
                $"sleep {duration};" +
                ReadCommand("sr2", "r", ethName) +
                ReadCommand("st2", "t", ethName) +
                $"echo $(($sr2-$sr1+$st2-$st1))";
            using var cmd = client.RunCommand(cmdText);
            return int.TryParse(cmd.Result, out var b) ? (int)(b / duration) : null;

            string ReadCommand(string par, string direction, string ethName) =>
                $"read {par} < /sys/class/net/{ethName}/statistics/{direction}x_bytes;";
        }
    }
}