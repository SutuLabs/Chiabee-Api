namespace WebApi.Services.ServerCommands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Renci.SshNet;
    using WebApi.Models;
    using WebApi.Services;

    public static class HarvesterDiskCommand
    {
        public record BlockTuple(DevicePartInfo[] Parts, string[] Disks);
        public static BlockTuple GetHarvesterBlockInfo(this TargetMachine client)
        {
            if (!client.EnsureConnected()) return null;
            using var cmd = client.ExecuteCommand(@"lsblk -r -e7 -no name,mountpoint,label,size,type,uuid");
            /*
sda                                             3.7T disk
└─sda1                    /farm/8      wy006    3.7T part 10f015c0-4b7d-4a47-850a-c75bfc441ea9
sdc                                           223.6G disk
├─sdc1                                            1M part
├─sdc2                    /boot                   1G part 53801bff-2359-432f-9565-a7e563bd2f2a
└─sdc3                                        222.6G part iIQ1cM-qGEM-rqyp-Ul8e-fMg8-1dva-gz39XC
└─ubuntu--vg-ubuntu--lv /                   111.3G lvm  feaa5e8d-abb9-4704-9f73-75593f3ee898
sdd                                             3.7T disk
└─sdd1                    /farm/zsl003 zsl003   3.7T part d74c5ecb-7013-4967-9c2e-6dcf6f853f64
sde                                             3.7T disk
sdm                                             3.7T disk
└─sdm1                    /farm/zyc008 zyc008   3.7T part e6c6f3f3-55f9-4aa9-ac5b-ad12c78520f0
             */
            const string separator = "|";
            var output = cmd.Result.Replace(" ", separator);
            var diskNames = ParseDiskName(output).ToArray();

            static IEnumerable<string> ParseDiskName(string output) => output
                .CleanSplit()
                .Where(_ => _.Contains("disk"))
                .Select(_ => _.CleanSplit(separator))
                //sda                                             3.7T disk
                .Select(segs => (segs.Length == 3 && segs[2] == "disk") ? segs[0] : default)
                .Where(_ => _ != null);

            var devs = ParseDiskInfo(output)
                .ToArray();

            static IEnumerable<DevicePartInfo> ParseDiskInfo(string output) => output
                .CleanSplit()
                .Select(_ => _.CleanSplit(separator, StringSplitOptions.None))
                .Where(segs => segs.Length == 6 && segs[0].Contains("1") && segs[4] == "part")
                //sdm1 /farm/zyc008 zyc008 3.7T part e6c6f3f3-55f9-4aa9-ac5b-ad12c78520f0
                .Select(segs => new DevicePartInfo(segs[0], segs[3], segs[1], segs[2], segs[5]) { BlockDevice = segs[0].TrimEnd('1'), });

            return new BlockTuple(devs, diskNames);
        }

        public static HarvesterDiskInfo[] GetHarvesterDiskInfo(this TargetMachine client)
        {
            if (!client.EnsureConnected()) return null;
            var (devs, diskNames) = client.GetHarvesterBlockInfo();
            var dd = devs.ToDictionary(_ => _.BlockDevice, _ => _);
            return diskNames
                .AsParallel()
                .Select(_ => dd.TryGetValue(_, out var device)
                    ? new { device, name = _ }
                    : new { device = default(DevicePartInfo), name = _ })
                .Select(_ => ParseDiskSmart(_.name, _.device))
                .ToArray();

            HarvesterDiskInfo ParseDiskSmart(string disk, DevicePartInfo device)
            {
                using var cmd = client.ExecuteCommand(@$"echo sutu | sudo -S sudo smartctl -d sat -a /dev/{disk}");
                var smart = cmd.Result;
                var pairs = CommandHelper.ParsePairs(smart,
                    new StatusDefinition(nameof(HarvesterDiskInfo.Sn), "Serial Number"),
                    new StatusDefinition(nameof(HarvesterDiskInfo.Model), "Device Model")
                    )
                    .ToDictionary(_ => _.Key, _ => _.Value);

                //SMART Attributes Data Structure revision number: 10
                //Vendor Specific SMART Attributes with Thresholds:
                //ID# ATTRIBUTE_NAME          FLAG     VALUE WORST THRESH TYPE      UPDATED  WHEN_FAILED RAW_VALUE
                //  1 Raw_Read_Error_Rate     0x000f   082   079   044    Pre-fail  Always       -       193532065
                //  3 Spin_Up_Time            0x0003   091   091   000    Pre-fail  Always       -       0
                //  4 Start_Stop_Count        0x0032   099   099   020    Old_age   Always       -       1846
                //  5 Reallocated_Sector_Ct   0x0033   100   100   036    Pre-fail  Always       -       0
                var rows = smart.CleanSplit("\n", StringSplitOptions.None)
                    .SkipWhile(_ => !_.StartsWith("SMART Attributes Data Structure revision number:"))
                    .Skip(3)
                    .TakeWhile(_ => !string.IsNullOrWhiteSpace(_));

                var attributes = rows
                    .Select(_ => _.CleanSplit(" "))
                    .Select(_ => _[0..9].Concat(new[] { string.Join(" ", _[9..]) }).ToArray())
                    .Select(_ => new DiskSmartAttribute(ParseInt(_[0]), _[1], _[2], ParseInt(_[3]), ParseInt(_[4]), ParseInt(_[5]), _[6], _[7], _[8], _[9]))
                    .Where(_ => _.Id != null)
                    .ToArray();

                var smartPairs = attributes
                    .Select(_ => new DiskSmartPair(_.AttributeName, _.RawValue))
                    .ToArray();

                int? ParseInt(string str) => int.TryParse(str, out var ii) ? ii : null;
                int? ParseIntWithoutParenthesis(string str) => ParseInt(Regex.Replace(str ?? "", @"\(.*\)", ""));

                var dict = attributes.ToDictionary(_ => _.Id, _ => _);
                string TryGetAttribute(int attribute) => dict.TryGetValue(attribute, out var att) ? att.RawValue : null;
                int? ParseIntAttribute(int attribute) => ParseIntWithoutParenthesis(TryGetAttribute(attribute));


                var powerCycleCount = ParseIntAttribute(DiskSmartAttribute.Power_Cycle_Count);
                var powerOnHours = ParseIntAttribute(DiskSmartAttribute.Power_On_Hours);
                // 36 (Min/Max 36/40)
                var temperature = ParseIntAttribute(DiskSmartAttribute.Airflow_Temperature_Cel)
                    ?? ParseIntAttribute(DiskSmartAttribute.Temperature_Celsius);

                return new HarvesterDiskInfo(
                    pairs.TryGetValue(nameof(HarvesterDiskInfo.Sn), out var sn) ? sn : null,
                    pairs.TryGetValue(nameof(HarvesterDiskInfo.Model), out var model) ? model : null,
                    disk,
                    device == null ? null : new[] { device with { BlockDevice = null } },
                    new DiskSmartInfo(powerCycleCount, powerOnHours, temperature, smartPairs)
                    );
            }
        }

        public static int MountAll(this TargetMachine m)
        {
            using var cmd = m.ExecuteCommand($"echo sutu | sudo -S sudo mount -a");
            return cmd.ExitStatus;
        }

        public static bool EnableSmart(this TargetMachine m, string dname)
        {
            if (!dname.StartsWith("sd")) return false;
            using var cmd = m.ExecuteCommand($"echo sutu | sudo -S sudo smartctl -d sat -a /dev/{dname} -s on");
            if (cmd.ExitStatus > 0) return false;
            return true;
        }
    }

    public record HarvesterDiskInfo(string Sn, string Model, string BlockDevice, DevicePartInfo[] Parts, DiskSmartInfo Smart);
    public record DevicePartInfo(string Name, string Size, string MountPoint, string Label, string Uuid)
    {
        public string BlockDevice { get; init; }
    }
    public record DiskSmartInfo(int? PowerCycleCount, int? PowerOnHours, int? Temperature, DiskSmartPair[] Values);
    public record DiskSmartPair(string Key, string Value);
    public record DiskSmartAttribute(int? Id, string AttributeName, string Flag, int? Value, int? Worst, int? Thresh, string Type, string Updated, string WhenFailed, string RawValue)
    {
        public const int Raw_Read_Error_Rate = 1;// 193532065
        public const int Spin_Up_Time = 3;// 0
        public const int Start_Stop_Count = 4;// 1846
        public const int Reallocated_Sector_Ct = 5;// 0
        public const int Seek_Error_Rate = 7;// 4323844749
        public const int Power_On_Hours = 9;// 222
        public const int Spin_Retry_Count = 10;// 0
        public const int Power_Cycle_Count = 12;// 1857
        public const int End_to_End_Error = 184;// 0
        public const int Reported_Uncorrect = 187;// 0
        public const int Command_Timeout = 188;// 0
        public const int High_Fly_Writes = 189;// 1
        public const int Airflow_Temperature_Cel = 190;// 36 (Min/Max 36/40)
        public const int G_Sense_Error_Rate = 191;// 0
        public const int Power_Off_Retract_Count = 192;// 1850
        public const int Load_Cycle_Count = 193;// 1868
        public const int Temperature_Celsius = 194;// 36 (0 26 0 0 0)
        public const int Hardware_ECC_Recovered = 195;// 193532065
        public const int Current_Pending_Sector = 197;// 0
        public const int Offline_Uncorrectable = 198;// 0
        public const int UDMA_CRC_Error_Count = 199;// 0
    }
}
/*
smartctl 7.1 2019-12-30 r5022 [x86_64-linux-5.4.0-74-generic] (local build)
Copyright (C) 2002-19, Bruce Allen, Christian Franke, www.smartmontools.org

=== START OF INFORMATION SECTION ===
Device Model:     ST4000NM0053
Serial Number:    Z1Z4BGLJ
LU WWN Device Id: 5 000c50 06723c499
Firmware Version: G003
User Capacity:    4,000,787,030,016 bytes [4.00 TB]
Sector Size:      512 bytes logical/physical
Rotation Rate:    7200 rpm
Form Factor:      3.5 inches
Device is:        Not in smartctl database [for details use: -P showall]
ATA Version is:   ACS-2 (minor revision not indicated)
SATA Version is:  SATA 3.0, 6.0 Gb/s (current: 3.0 Gb/s)
Local Time is:    Thu Jun 24 10:29:07 2021 UTC
SMART support is: Available - device has SMART capability.
SMART support is: Enabled

=== START OF READ SMART DATA SECTION ===
SMART overall-health self-assessment test result: PASSED

General SMART Values:
Offline data collection status:  (0x82) Offline data collection activity
                                        was completed without error.
                                        Auto Offline Data Collection: Enabled.
Self-test execution status:      (   0) The previous self-test routine completed
                                        without error or no self-test has ever
                                        been run.
Total time to complete Offline
data collection:                (  592) seconds.
Offline data collection
capabilities:                    (0x7b) SMART execute Offline immediate.
                                        Auto Offline data collection on/off support.
                                        Suspend Offline collection upon new
                                        command.
                                        Offline surface scan supported.
                                        Self-test supported.
                                        Conveyance Self-test supported.
                                        Selective Self-test supported.
SMART capabilities:            (0x0003) Saves SMART data before entering
                                        power-saving mode.
                                        Supports SMART auto save timer.
Error logging capability:        (0x01) Error logging supported.
                                        General Purpose Logging supported.
Short self-test routine
recommended polling time:        (   1) minutes.
Extended self-test routine
recommended polling time:        ( 494) minutes.
Conveyance self-test routine
recommended polling time:        (   2) minutes.
SCT capabilities:              (0x50bd) SCT Status supported.
                                        SCT Error Recovery Control supported.
                                        SCT Feature Control supported.
                                        SCT Data Table supported.

SMART Attributes Data Structure revision number: 10
Vendor Specific SMART Attributes with Thresholds:
ID# ATTRIBUTE_NAME          FLAG     VALUE WORST THRESH TYPE      UPDATED  WHEN_FAILED RAW_VALUE
  1 Raw_Read_Error_Rate     0x000f   082   079   044    Pre-fail  Always       -       193532065
  3 Spin_Up_Time            0x0003   091   091   000    Pre-fail  Always       -       0
  4 Start_Stop_Count        0x0032   099   099   020    Old_age   Always       -       1846
  5 Reallocated_Sector_Ct   0x0033   100   100   036    Pre-fail  Always       -       0
  7 Seek_Error_Rate         0x000f   074   060   030    Pre-fail  Always       -       4323844749
  9 Power_On_Hours          0x0032   100   100   000    Old_age   Always       -       222
 10 Spin_Retry_Count        0x0013   100   100   097    Pre-fail  Always       -       0
 12 Power_Cycle_Count       0x0032   099   099   020    Old_age   Always       -       1857
184 End-to-End_Error        0x0032   100   100   099    Old_age   Always       -       0
187 Reported_Uncorrect      0x0032   100   100   000    Old_age   Always       -       0
188 Command_Timeout         0x0032   100   100   000    Old_age   Always       -       0
189 High_Fly_Writes         0x003a   099   099   000    Old_age   Always       -       1
190 Airflow_Temperature_Cel 0x0022   064   047   045    Old_age   Always       -       36 (Min/Max 36/40)
191 G-Sense_Error_Rate      0x0032   100   100   000    Old_age   Always       -       0
192 Power-Off_Retract_Count 0x0032   100   100   000    Old_age   Always       -       1850
193 Load_Cycle_Count        0x0032   100   100   000    Old_age   Always       -       1868
194 Temperature_Celsius     0x0022   036   053   000    Old_age   Always       -       36 (0 26 0 0 0)
195 Hardware_ECC_Recovered  0x001a   027   027   000    Old_age   Always       -       193532065
197 Current_Pending_Sector  0x0012   100   100   000    Old_age   Always       -       0
198 Offline_Uncorrectable   0x0010   100   100   000    Old_age   Offline      -       0
199 UDMA_CRC_Error_Count    0x003e   200   200   000    Old_age   Always       -       0

SMART Error Log Version: 1
No Errors Logged

SMART Self-test log structure revision number 1
No self-tests have been logged.  [To run self-tests, use: smartctl -t]

SMART Selective self-test log data structure revision number 1
 SPAN  MIN_LBA  MAX_LBA  CURRENT_TEST_STATUS
    1        0        0  Not_testing
    2        0        0  Not_testing
    3        0        0  Not_testing
    4        0        0  Not_testing
    5        0        0  Not_testing
Selective self-test flags (0x0):
  After scanning selected spans, do NOT read-scan remainder of disk.
If Selective self-test is pending on power-up, resume after 0 minute delay.

*/
/*
SMART Attributes Data Structure revision number: 16
Vendor Specific SMART Attributes with Thresholds:
ID# ATTRIBUTE_NAME          FLAG     VALUE WORST THRESH TYPE      UPDATED  WHEN_FAILED RAW_VALUE
  1 Raw_Read_Error_Rate     0x000b   100   100   050    Pre-fail  Always       -       0
  2 Throughput_Performance  0x0005   100   100   050    Pre-fail  Offline      -       0
  3 Spin_Up_Time            0x0027   100   100   001    Pre-fail  Always       -       5253
  4 Start_Stop_Count        0x0032   100   100   000    Old_age   Always       -       8
  5 Reallocated_Sector_Ct   0x0033   100   100   050    Pre-fail  Always       -       0
  7 Seek_Error_Rate         0x000b   100   100   050    Pre-fail  Always       -       0
  8 Seek_Time_Performance   0x0005   100   100   050    Pre-fail  Offline      -       0
  9 Power_On_Hours          0x0032   100   100   000    Old_age   Always       -       91
 10 Spin_Retry_Count        0x0033   100   100   030    Pre-fail  Always       -       0
 12 Power_Cycle_Count       0x0032   100   100   000    Old_age   Always       -       8
191 G-Sense_Error_Rate      0x0032   100   100   000    Old_age   Always       -       3
192 Power-Off_Retract_Count 0x0032   100   100   000    Old_age   Always       -       7
193 Load_Cycle_Count        0x0032   100   100   000    Old_age   Always       -       9
194 Temperature_Celsius     0x0022   100   100   000    Old_age   Always       -       32 (Min/Max 24/36)
196 Reallocated_Event_Count 0x0032   100   100   000    Old_age   Always       -       0
197 Current_Pending_Sector  0x0032   100   100   000    Old_age   Always       -       0
198 Offline_Uncorrectable   0x0030   100   100   000    Old_age   Offline      -       0
199 UDMA_CRC_Error_Count    0x0032   200   253   000    Old_age   Always       -       0
220 Disk_Shift              0x0002   100   100   000    Old_age   Always       -       0
222 Loaded_Hours            0x0032   100   100   000    Old_age   Always       -       91
223 Load_Retry_Count        0x0032   100   100   000    Old_age   Always       -       0
224 Load_Friction           0x0022   100   100   000    Old_age   Always       -       0
226 Load-in_Time            0x0026   100   100   000    Old_age   Always       -       692
240 Head_Flying_Hours       0x0001   100   100   001    Pre-fail  Offline      -       0
*/