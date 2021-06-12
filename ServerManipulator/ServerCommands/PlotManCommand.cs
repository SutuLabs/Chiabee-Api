namespace WebApi.Services.ServerCommands
{
    using System;
    using System.Collections.Generic;
    using System.Dynamic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Renci.SshNet;
    using WebApi.Models;
    using WebApi.Services;
    using YamlDotNet.Serialization;

    public static class PlotManCommand
    {
        public static bool StopPlot(this TargetMachine client, string plotId)
        {
            if (!client.EnsureConnected()) return default;
            using var cmd = client.RunCommand($@". ~/chia-blockchain/activate && yes | plotman kill {plotId}");
            if (cmd.ExitStatus == 0) return true;
            return false;
        }

        public static PlotManConfiguration ReadPlotManConfiguration(this TargetMachine client)
        {
            if (!client.EnsureConnected()) return default;
            using var cmd = client.RunCommand($@"cat ~/.config/plotman/plotman.yaml");
            var yaml = cmd.Result;
            var deserializer = new DeserializerBuilder().Build();
            if (string.IsNullOrEmpty(yaml)) return null;
            try
            {
                dynamic yamlObj = deserializer.Deserialize<ExpandoObject>(yaml);
                var idx = yamlObj.directories["archive"]["index"];
                var host = yamlObj.directories["archive"]["rsyncd_host"];
                var jobs = yamlObj.scheduling["tmpdir_max_jobs"];
                var stagger = yamlObj.scheduling["global_stagger_m"];

                return new PlotManConfiguration(host, ParseInt(idx), ParseInt(jobs), ParseInt(stagger));
            }
            catch (KeyNotFoundException)
            {
                return null;
            }
        }

        public static bool SetPlotManConfiguration(this TargetMachine m, PlotManConfiguration config)
        {
            var successFlag = true;

            using var scp = new ScpClient(m.ConnectionInfo);
            scp.Connect();
            scp.Upload(new FileInfo("Assets/plotman.yaml"), "/home/sutu/.config/plotman/plotman.yaml");
            scp.Disconnect();

            // always execute these replace, return just the result
            var replaceFlag = true;
            replaceFlag &= Replace("rsyncd_host", "rsyncd_host", config.RsyncdHost);
            replaceFlag &= Replace("tmpdir_max_jobs", "TEMP_JOB", config.JobNumber);
            replaceFlag &= Replace("global_stagger_m", "STAGGER_MIN", config.StaggerMinute);
            replaceFlag &= Replace("index", "rsyncd_index", config.RsyncdIndex);

            successFlag &= replaceFlag;

            bool Replace(string leading, string placeholder, object value)
            {
                using var cresult = m.RunCommand($"sed -i 's/{leading}: {placeholder}/{leading}: {value}/g' ~/.config/plotman/plotman.yaml");
                return cresult.ExitStatus == 0;
            }

            return successFlag;
        }

        public static bool CleanLegacyTemporaryFiles(this TargetMachine client)
        {
            if (!client.EnsureConnected()) return default;

            var ps = client.GetPlotterStatus();
            var jobs = ps.Jobs.Select(_ => _.Id);

            using var cmd = client.RunCommand($@"ls /data/tmp/ |sort |grep -o '.*\.plot' |sort -u");
            var output = cmd.Result;

            //plot-k32-2021-06-08-16-20-80f5cf0ad7edb5daa869cce84ee1e5669a33ef5e6ffeba255f64c97642d3ae07.plot
            var reId = new Regex(@"plot-k\d{2}-\d{4}-(\d{2}-){4}(?<id>[0-9a-f]{64}).plot");

            // ids need to clean
            var lstId = output.CleanSplit()
                .Select(_ => reId.Match(_))
                .Where(_ => _.Success)
                .Select(_ => _.Groups["id"].Value)
                .Select(_ => _[0..8])
                .Except(jobs)
                .ToArray();

            var flag = true;

            foreach (var id in lstId)
            {
                using var cleanCmd = client.RunCommand($@"rm /data/tmp/*{id}*");
                flag &= cmd.ExitStatus == 0;
            }

            return flag;
        }

        private static int? ParseInt(string str)
        {
            if (int.TryParse(str, out var num))
                return num;

            return null;
        }
    }

    public record PlotManConfiguration(string RsyncdHost, int? RsyncdIndex, int? JobNumber, int? StaggerMinute);
}