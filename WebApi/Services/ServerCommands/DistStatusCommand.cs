namespace WebApi.Services.ServerCommands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Renci.SshNet;
    using WebApi.Models;
    using WebApi.Services;

    public static class DistStatusCommand
    {
        public static DiskStatus[] GetDiskStatus(this TargetMachine client)
        {
            client.EnsureConnected();
            var cmd = client.RunCommand(@"df |grep ""/dev/sd\|/dev/md\|/$""");
            return ParseDiskStatus(cmd.Result).ToArray();

            static IEnumerable<DiskStatus> ParseDiskStatus(string output)
            {
                ///dev/sda2                             999320     107936     822572  12% /boot
                ///dev/sde1                          767491852  410258604  318177124  57% /data/tmp2
                ///dev/sdc1                         1534742632  852249848  604462400  59% /data/tmp1
                ///dev/sda1                             523248       8032     515216   2% /boot/efi
                ///dev/sdb1                         1921270996      77856 1823528172   1% /data/transition
                ///dev/sdd1                         5812716096 1176950008 4342751460  22% /data/drv2
                var lines = output.Split("\n", StringSplitOptions.RemoveEmptyEntries).Select(_ => _.Trim()).ToArray();
                var idx = 0;
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = line.Split(" ", StringSplitOptions.RemoveEmptyEntries).Select(_ => _.Trim()).ToArray();
                    if (parts.Length != 6) continue;

                    yield return new DiskStatus(
                        parts[0],
                        long.Parse(parts[1]),
                        long.Parse(parts[2]),
                        long.Parse(parts[3]),
                        parts[5]);

                    idx++;
                }
            }
        }
    }
}