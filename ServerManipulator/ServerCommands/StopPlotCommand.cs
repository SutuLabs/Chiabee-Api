namespace WebApi.Services.ServerCommands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Renci.SshNet;
    using WebApi.Models;
    using WebApi.Services;

    public static class StopPlotCommand
    {
        public static bool StopPlot(this TargetMachine client, string plotId)
        {
            if (!client.EnsureConnected()) return false;
            var cmd = client.RunCommand($@". ~/chia-blockchain/activate && yes | plotman kill {plotId}");
            if (cmd.ExitStatus == 0) return true;
            return false;
        }
    }
}