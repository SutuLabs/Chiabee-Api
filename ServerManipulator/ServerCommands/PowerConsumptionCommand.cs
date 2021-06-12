namespace WebApi.Services.ServerCommands
{
    using WebApi.Models;

    public static class PowerConsumptionCommand
    {
        public static decimal? GetPowerConsumption(this TargetMachine client)
        {
            if (!client.EnsureConnected()) return null;
            if (!client.Name.StartsWith("r420") && !client.Name.StartsWith("r720"))
            {
                return null;
            }

            using var cmd = client.RunCommand(@$"echo sutu | sudo -S ipmitool sensor list | grep -i pwr");
            //Pwr Consumption  | 154.000 | Watts | ok | na | na | na | 896.000 | 980.000 | na

            var items = cmd.Result.CleanSplit("|");
            return items.Length < 1 ? null
                : decimal.TryParse(items[1], out var pwr) ? pwr : null;
        }
    }
}