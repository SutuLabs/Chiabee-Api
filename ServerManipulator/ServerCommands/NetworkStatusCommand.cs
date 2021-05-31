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
            var cmd = client.RunCommand(@$"read s1 < /sys/class/net/{ethName}/statistics/rx_bytes; sleep {duration}; read s2 < /sys/class/net/{ethName}/statistics/rx_bytes; echo $(($s2-$s1))");
            return int.TryParse(cmd.Result, out var b) ? (int)(b / duration) : null;
        }
    }
}