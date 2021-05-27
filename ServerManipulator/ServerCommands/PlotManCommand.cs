namespace WebApi.Services.ServerCommands
{
    using System;
    using System.Collections.Generic;
    using System.Dynamic;
    using System.Linq;
    using Renci.SshNet;
    using WebApi.Models;
    using WebApi.Services;
    using YamlDotNet.Serialization;

    public static class PlotManCommand
    {
        public static bool StopPlot(this TargetMachine client, string plotId)
        {
            if (!client.EnsureConnected()) return default;
            var cmd = client.RunCommand($@". ~/chia-blockchain/activate && yes | plotman kill {plotId}");
            if (cmd.ExitStatus == 0) return true;
            return false;
        }

        public static PlotManConfiguration ReadPlotManConfiguration(this TargetMachine client)
        {
            if (!client.EnsureConnected()) return default;
            var cmd = client.RunCommand($@"cat ~/.config/plotman/plotman.yaml");
            var yaml = cmd.Result;
            var deserializer = new DeserializerBuilder().Build();
            dynamic yamlObj = deserializer.Deserialize<ExpandoObject>(yaml);
            var idx = yamlObj.directories["archive"]["index"];
            var host = yamlObj.directories["archive"]["rsyncd_host"];
            var jobs = yamlObj.scheduling["tmpdir_max_jobs"];
            var stagger = yamlObj.scheduling["global_stagger_m"];

            return new PlotManConfiguration(host, ParseInt(idx), ParseInt(jobs), ParseInt(stagger));
        }

        public static int? ParseInt(string str)
        {
            if (int.TryParse(str, out var num))
                return num;

            return null;
        }
    }

    public record PlotManConfiguration(string RsyncdHost, int? RsyncdIndex, int? JobNumber, int? StaggerMinute);
}