﻿namespace WebApi.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Humanizer;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;
    using WebApi.Entities;
    using WebApi.Helpers;
    using WebApi.Models;
    using WebApi.Services.ServerCommands;

    internal class StatisticsService : BaseRefreshService
    {
        private readonly PersistentService persistentService;
        private readonly ServerService server;
        private readonly AppSettings appSettings;

        private Dictionary<string, string> texts;
        private DateTime lastFarmerNull = DateTime.MinValue;

        public StatisticsService(
            ILogger<StatisticsService> logger,
            PersistentService persistentService,
            ServerService server,
            IOptions<AppSettings> appSettings)
            : base(logger, nameof(StatisticsService), 50, 20, 300)
        {
            this.persistentService = persistentService;
            this.server = server;
            this.appSettings = appSettings.Value;
            this.texts = new Dictionary<string, string>()
            {
                [Key.NodeStatus] = "节点状态",
                [Key.FarmStatus] = "农场状态",
                [Key.TotalFarmed] = "总共挖币",
                [Key.TotalPlot] = "总共耕田",
                [Key.NetSpace] = "全网空间",
                [Key.TotalPlotter] = "P图机数量",
                [Key.TotalHarvester] = "收割机数量",
                [Key.CoinPrice] = "币价",
                [Key.EstimateWin] = "成功期望",
                [Key.PlotHeap] = "堆积图数",
            };
        }

        public record ReportState(HarvesterStatus[] HarvesterStatuses, DateTime ReportTime, DateTime NodeLastTime, Dictionary<string, string> Items);

        public class Key
        {
            public const string NodeStatus = nameof(NodeStatus);
            public const string FarmStatus = nameof(FarmStatus);
            public const string TotalFarmed = nameof(TotalFarmed);
            public const string TotalPlot = nameof(TotalPlot);
            public const string NetSpace = nameof(NetSpace);
            public const string TotalPlotter = nameof(TotalPlotter);
            public const string TotalHarvester = nameof(TotalHarvester);
            public const string CoinPrice = nameof(CoinPrice);
            public const string EstimateWin = nameof(EstimateWin);
            public const string PlotHeap = nameof(PlotHeap);
        }

        protected override async Task DoWorkAsync(CancellationToken token)
        {
            var alertUrl = this.appSettings.WeixinAlertUrl;
            var hourlyReportUrl = this.appSettings.WeixinHourlyReportUrl;
            var dailyReportUrl = this.appSettings.WeixinDailyReportUrl;
            StatisticsStateEntity persistState = null;

            try
            {
                // get all data for preparing this state data
                var farm = await this.persistentService.RetrieveEntityAsync<FarmStateEntity>();
                var farmers = JsonConvert.DeserializeObject<FarmerNodeStatus[]>(farm.FarmerJsonGzip.Decompress());
                var farmer = farmers.FirstOrDefault();
                if (farmer == null)
                {
                    if ((DateTime.UtcNow - this.lastFarmerNull) > TimeSpan.FromHours(12))
                    {
                        await new MarkdownMessage("ERROR: farmer not exist").SendAsync(alertUrl);
                        this.lastFarmerNull = DateTime.UtcNow;
                    }

                    return;
                }
                else
                {
                    if (this.lastFarmerNull > DateTime.MinValue)
                    {
                        await new MarkdownMessage("farmer come back").SendAsync(alertUrl);
                        this.lastFarmerNull = DateTime.MinValue;
                    }
                }

                var plotters = JsonConvert.DeserializeObject<PlotterStatus[]>(farm.PlotterJsonGzip.Decompress());
                var harvesters = JsonConvert.DeserializeObject<HarvesterStatus[]>(farm.HarvesterJsonGzip.Decompress());

                var server = await this.persistentService.RetrieveEntityAsync<MachineStateEntity>();
                var machines = JsonConvert.DeserializeObject<ServerStatus[]>(server.MachinesJsonGzip.Decompress());

                var market = await this.persistentService.RetrieveEntityAsync<PriceStateEntity>();
                var prices = JsonConvert.DeserializeObject<PriceEntity[]>(market.PricesJson);

                // prepare this state data
                var dictionary = new Dictionary<string, string>();
                dictionary.Add(Key.NodeStatus, farmer.Node.Status);
                dictionary.Add(Key.FarmStatus, farmer.Farmer.Status);
                dictionary.Add(Key.TotalFarmed, farmer.Farmer.TotalFarmed?.ToString());
                var totalPlot = harvesters.Sum(_ => _.TotalPlot) ?? 0;
                totalPlot += farmer.Farmer?.PlotCount ?? 0;
                dictionary.Add(Key.TotalPlot, totalPlot.ToString());
                dictionary.Add(Key.NetSpace, farmer.Node.Space);
                dictionary.Add(Key.TotalPlotter, plotters.Length.ToString());
                dictionary.Add(Key.TotalHarvester, harvesters.Length.ToString());
                dictionary.Add(Key.CoinPrice, prices.First().Price.ToString("0.##"));
                dictionary.Add(Key.EstimateWin, totalPlot <= 0 ? "None" : GetEstimateTime(totalPlot, farmer.Node.Space));
                dictionary.Add(Key.PlotHeap, plotters.Sum(_ => _?.Files?.FirstOrDefault()?.Files?.Length ?? 0).ToString());

                var thisState = new ReportState(harvesters, DateTime.UtcNow, farmer.Node.Time, dictionary);
                persistState = await this.persistentService.RetrieveEntityAsync<StatisticsStateEntity>();
                if (persistState == null) persistState = new StatisticsStateEntity();

                var json = persistState.LastCheckJsonGzip.Decompress();
                if (!json.TryParseJson<ReportState>(out var lastState, out var parseError))
                {
                    logger.LogWarning($"failed to parse last state, error: [{parseError}], json: {json}");
                    persistState.LastCheckJsonGzip = JsonConvert.SerializeObject(thisState).Compress();
                    persistState.LastHour = DateTime.UtcNow.AddHours(-1);
                    persistState.LastDay = DateTime.UtcNow.AddDays(-1);
                    persistState.LastHourJsonGzip = persistState.LastCheckJsonGzip;
                    persistState.LastDayJsonGzip = persistState.LastCheckJsonGzip;
                    await this.persistentService.LogEntityAsync(persistState);
                    lastState = thisState;
                    // avoid process when cannot read last state
                    return;
                }

                // send alert immediately
                var alert = GenerateAlert(lastState, thisState);
                if (!string.IsNullOrEmpty(alert.Trim()))
                {
                    await new MarkdownMessage(alert).SendAsync(alertUrl);
                }
                persistState.LastCheckJsonGzip = JsonConvert.SerializeObject(thisState).Compress();

                // send hour report
                var lastSendHourReportTime = persistState.LastHour ?? DateTime.MinValue;
                if ((DateTime.UtcNow - lastSendHourReportTime).TotalMinutes > 55
                    && this.appSettings.HourlyReportMin == DateTime.UtcNow.Minute)
                {
                    await SendReport(thisState, "小时", persistState.LastHourJsonGzip.Decompress(), hourlyReportUrl, true);

                    // update time
                    persistState.LastHour = DateTime.UtcNow;
                    persistState.LastHourJsonGzip = persistState.LastCheckJsonGzip;
                }

                // send day report
                var lastSendDayReportTime = persistState.LastDay ?? DateTime.MinValue;
                if ((DateTime.UtcNow - lastSendDayReportTime).TotalHours > 22
                    && this.appSettings.DailyReportHour == DateTime.UtcNow.Hour)
                {
                    await SendReport(thisState, "每日", persistState.LastDayJsonGzip.Decompress(), dailyReportUrl);

                    // update time
                    persistState.LastDay = DateTime.UtcNow;
                    persistState.LastDayJsonGzip = persistState.LastCheckJsonGzip;
                }
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "error during generating report");
            }
            finally
            {
                if (token.IsCancellationRequested)
                {
                    this.logger.LogInformation($"Refresh work cancelled.");
                }
                else if (persistState != null)
                {
                    await this.persistentService.LogEntityAsync(persistState);
                }
            }

            async Task SendReport(ReportState thisState, string reportTitle, string json, string reportUrl, bool isSummativeReportGenerated = false)
            {
                if (!json.TryParseJson<ReportState>(out var lastReportState, out var error))
                {
                    await new MarkdownMessage("前次存储信息有误，本次报告暂不提供，待管理员处理。").SendAsync(reportUrl);
                    logger.LogWarning($"failed to parse last {reportTitle} report, json: {json}");
                    return;
                }

                var msg = GenerateReport(lastReportState, thisState, reportTitle);
                if (isSummativeReportGenerated) msg += await GenerateSummativeReport();
                await new MarkdownMessage(msg).SendAsync(reportUrl);
            }
        }

        private record SummativeMachine(string Name, int Total, string[] Disks);
        private async Task<string> GenerateSummativeReport()
        {
            var machines = await this.server.GetHarvesterDisksInfo();

            var msg = "";

            const string title = "\n---\n**硬盘情况**\n---\n";
            var re = new Regex(@"(?<name>[a-z]\d)-");

            var total = machines.Sum(m => m.Disks?.Length ?? 0);
            var overall = string.Join(", ", machines.Select(m => $"{GetShortName(m.Name)}({m.Disks?.Length ?? 0})"));

            string GetShortName(string name) => re.Match(name).Groups["name"].Value;
            SummativeMachine[] GetByTemperature(int minTemp, int maxTemp = int.MaxValue) => machines
                .Select(m => new SummativeMachine(GetShortName(m.Name), m.Disks?.Length ?? 0, m.Disks?
                    .Where(d => d.Smart.Temperature >= minTemp && d.Smart.Temperature < maxTemp)
                    .Select(d => d.Parts?.FirstOrDefault()?.Label)
                    .Where(_ => _ != null)
                    .ToArray() ?? new string[] { }))
                .Where(m => m.Disks.Length > 0)
                .ToArray();
            string GetDiskLabel(SummativeMachine[] machines) =>
                string.Join(", ", machines.Select(m => $"{m.Name}({string.Join(", ", m.Disks)})"));
            string GetDiskCount(SummativeMachine[] machines) =>
                string.Join(", ", machines.Select(m => $"{m.Name}({m.Disks.Length}/{m.Total})"));

            var t55 = GetByTemperature(55);
            var s55 = GetDiskLabel(t55);
            var t50 = GetByTemperature(50, 55);
            var t50total = t50.Sum(m => m.Disks?.Length ?? 0);
            var s50 = t50total <= 10 ? GetDiskLabel(t50) : GetDiskCount(t50);

            msg += title
                + "\n硬盘总数：" + total
                + "\n分布情况：" + overall
                + "\n超过55度：" + (string.IsNullOrEmpty(s55) ? "无" : s55)
                + "\n超过50度：" + (string.IsNullOrEmpty(s50) ? "无" : s50);

            return msg;
        }

        private string GenerateReport(ReportState prevState, ReportState nowState, string title)
        {
            var msg = "";
            var stats = new[] {
                Key.TotalFarmed,
                Key.TotalPlot,
                Key.NetSpace,
                Key.TotalPlotter,
                Key.TotalHarvester,
                Key.CoinPrice,
                Key.EstimateWin,
                Key.PlotHeap,
            };

            // 统计数据
            var prev = prevState.Items;
            var now = nowState.Items;
            msg += $"\n\n**{title}统计数据**\n---\n" +
                string.Join("\n", now
                .Where(_ => stats.Contains(_.Key))
                .Select(_ => new { _.Key, str = _.Value, isNumber = decimal.TryParse(_.Value, out var number), number })
                .Select(_ => new
                {
                    _.Key,
                    _.str,
                    _.number,
                    _.isNumber,
                    prev = prev.TryGet(_.Key),
                    delta = _.isNumber && decimal.TryParse(prev.TryGet(_.Key), out var pnumber)
                        ? _.number - pnumber : 0
                })
                .Select(_ => $"- {texts.TryGetName(_.Key)}: "
                    + (_.isNumber ? ShowNumber(_.number, _.delta) : ShowText(_.str, _.prev)))
                );

            return msg;

            string ShowText(string now, string prev)
            {
                if (prev == now) return Style(WeixinFontType.comment, now);
                return (string.IsNullOrEmpty(prev) ? "" : $" {Style(WeixinFontType.comment, prev)} ->")
                    + $" {Style(WeixinFontType.info, now)}";
            }

            string ShowNumber(decimal number, decimal delta)
            {
                if (delta == 0) return Style(WeixinFontType.comment, number.ToString("0.##"));
                var str = delta == (int)delta ? delta.ToString() : delta.ToString("0.##");
                return Style(WeixinFontType.info, number.ToString("0.##"))
                    + Style(WeixinFontType.comment, $"({(delta > 0 ? "+" : "")}{str})");
            }
        }

        private string GenerateAlert(ReportState lastState, ReportState thisState)
        {
            // disk changes
            var sb = new StringBuilder();
            var nowd = thisState.Items;
            var prevd = lastState.Items;

            {
                var now = thisState.HarvesterStatuses.Select(_ => _.Name).ToArray();
                var prev = lastState.HarvesterStatuses.Select(_ => _.Name).ToArray();
                var appear = now.Except(prev).ToArray();
                var disappear = prev.Except(now).ToArray();
                if (appear.Any()) sb.AppendLine(Style(WeixinFontType.info, $"`[Harvester]`New arrival: ({string.Join(",", appear)})"));
                if (disappear.Any()) sb.AppendLine(Style(WeixinFontType.warning, $"`[Harvester]`Disappeared: ({string.Join(",", disappear)})"));
            }

            foreach (var now in thisState.HarvesterStatuses)
            {
                var tsb = new StringBuilder();
                var prev = lastState.HarvesterStatuses.FirstOrDefault(_ => _.Name == now.Name);
                if (prev == null) continue;

                var appearFl = now.AbnormalFarmlands.All.Except(prev.AbnormalFarmlands.All).ToArray();
                if (appearFl.Any())
                    tsb.AppendLine(Style(WeixinFontType.warning, $"[Harvester]{now.Name}: New abnormal farmlands ({string.Join(",", appearFl)})"));

                var appearDp = now.DanglingPartitions.Except(prev.DanglingPartitions).ToArray();
                if (appearDp.Any())
                    tsb.AppendLine(Style(WeixinFontType.warning, $"[Harvester]{now.Name}: New dangling partition ({string.Join(",", appearDp)})"));

                var seconds = (thisState.ReportTime - (now.LastPlotTime ?? DateTime.MinValue)).TotalSeconds;
                var prevSeconds = (lastState.ReportTime - (prev.LastPlotTime ?? DateTime.MinValue)).TotalSeconds;
                const int threshold = 300;
                if (seconds > threshold && prevSeconds <= threshold)
                    tsb.AppendLine(Style(WeixinFontType.warning, $"[Harvester]{now.Name}: Plot verification generation time longer than {seconds}s"));
                if (seconds <= threshold && prevSeconds > threshold)
                    tsb.AppendLine(Style(WeixinFontType.info, $"[Harvester]{now.Name}: Plot verification generation time is {seconds}s, back to normal"));

                var plotMinus = prev.TotalPlot - now.TotalPlot;
                if (plotMinus > 10)
                    tsb.AppendLine(Style(WeixinFontType.warning, $"[Harvester]{now.Name}: Plot greatly reduced from {prev.TotalPlot} to {now.TotalPlot}"));

                var tsbout = tsb.ToString().Trim();
                if (!string.IsNullOrEmpty(tsbout)) sb.AppendLine(tsbout);
            }

            {
                var seconds = (DateTime.UtcNow - thisState.NodeLastTime).TotalSeconds;
                if (seconds > 300)
                    sb.AppendLine($"[Farmer]: Block generation time longer than {seconds}s");

                foreach (var item in new[] { Key.NodeStatus, Key.FarmStatus, Key.TotalFarmed })
                {
                    if (nowd.TryGet(item) != prevd.TryGet(item))
                        sb.AppendLine($"[Farmer]: {this.texts.TryGetName(item)} changed to {nowd.TryGet(item)}");
                }
            }

            return sb.ToString();
        }

        private string GetEstimateTime(int plot, string totalNetSpace)
        {
            if (totalNetSpace == null) return null;

            // in seconds (last paragraph in https://docs.google.com/document/d/1tmRIb7lgi4QfKkNaxuKOBHRmwbVlGL4f7EsBDr_5xZE/edit#heading=h.z0v0b3hmk4fl)
            const double averageBlockTime = 18.75;
            const double plotSize = 101.4 / 1024;//tib

            var totalNetSpaceTib = !totalNetSpace.EndsWith("EiB") ? throw new NotImplementedException()
                : double.Parse(totalNetSpace[0..(totalNetSpace.Length - 4)]) * Math.Pow(2, 20);

            var ourSize = plot * plotSize;
            var proportion = ourSize / totalNetSpaceTib;

            // in seconds (reference:https://github.com/Chia-Network/chia-blockchain/blob/95d6030876fb19f6836c6c6eeb41273cf7c30d93/chia/cmds/farm_funcs.py#L246-L247)
            var expectTimeWin = averageBlockTime / proportion;
            var estimatedTime = TimeSpan.FromSeconds((int)expectTimeWin).Humanize(2);
            return estimatedTime;
        }

        private string Style(WeixinFontType type, string content)
            => Wrap("font", $@"color=""{type}""", content);

        private string Wrap(string tag, string attributes, string content)
            => $@"<{tag} {attributes}>{content}</{tag}>";

        private enum WeixinFontType
        {
            comment,
            info,
            warning,
        }

        public record ReportDiskState();

        //private async Task SendMessageAsync()
        //{
        //    var stalist = await this.GetResultAsync(uow => uow.Statistics.GetAllAsync());

        //    // 待处理项目
        //    var todo = stalist
        //        .Where(_ => importanttexts.ContainsKey(_.Name.ToString()))
        //        .Where(_ => _.Value != "0")
        //        .Select(_ => $"- {importanttexts[_.Name]}: `{_.Value}`")
        //        .ToArray();
        //    var zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        //    var dtNow = TimeZoneInfo.ConvertTime(DateTime.UtcNow, zone);
        //    var msg = $"**{dtNow:yyyy-MM-dd}**\n---\n";
        //    msg += todo.Length == 0
        //        ? "无待处理项目"
        //        : ($"**待处理项目**\n---\n" + string.Join("\n", todo));

        //    // 统计数据
        //    // dby = day before yesterday
        //    var dbyitem = stalist.FirstOrDefault(_ => _.Name == StatisticItem.YesterdayRecords.ToString());
        //    var dbyjson = dbyitem == null ? "{}" : dbyitem.Value;
        //    var dby = JsonConvert.DeserializeObject<Dictionary<string, decimal>>(dbyjson);
        //    msg += $"\n\n**统计数据**\n---\n" +
        //        string.Join("\n", stalist
        //        .Where(_ => othertexts.ContainsKey(_.Name.ToString()))
        //        .Select(_ => new { _.Name, Value = decimal.Parse(_.Value), })
        //        .Select(_ => new { _.Name, _.Value, Delta = dby.ContainsKey(_.Name) ? _.Value - dby[_.Name] : 0 })
        //        .Select(_ => $"- {othertexts[_.Name]}: `{_.Value}` ({(_.Delta >= 0 ? "+" : "")}{_.Delta})")
        //        );

        //    // 昨日数据
        //    msg += $"\n\n**昨日数据**\n---\n" +
        //        string.Join("\n", stalist
        //        .Where(_ => yesterdaytexts.ContainsKey(_.Name.ToString()))
        //        .Select(_ => $"- {yesterdaytexts[_.Name]}: `￥{((decimal)(int.TryParse(_.Value, out var number) ? number : 0)) / 100:0.00}`")
        //        );

        //    var yesterdayjson = JsonConvert.SerializeObject(stalist
        //        .Where(_ => othertexts.ContainsKey(_.Name.ToString()))
        //        .ToDictionary(_ => _.Name, _ => _.Value));
        //    await this.ExecuteAsync(uow => uow.Statistics.UpdateOrAddAsync(
        //        StatisticItem.YesterdayRecords.ToString(), yesterdayjson));

        //    await SendMessageAsync(ChatTarget.Information, null, msg);
        //}
    }

    public static class Helper
    {
        public static bool TryParseJson<T>(this string @this, out T result, out ErrorContext outError)
        {
            ErrorContext error = null;
            if (string.IsNullOrEmpty(@this))
            {
                result = default;
                outError = error;
                return false;
            }

            bool success = true;
            var settings = new JsonSerializerSettings
            {
                Error = (sender, args) => { success = false; args.ErrorContext.Handled = true; error = args.ErrorContext; },
                MissingMemberHandling = MissingMemberHandling.Error
            };
            result = JsonConvert.DeserializeObject<T>(@this, settings);
            outError = error;
            return success;
        }

        public static string TryGet(this Dictionary<string, string> dict, string key)
            => dict.ContainsKey(key) ? dict[key] : null;

        public static string TryGetName(this Dictionary<string, string> dict, string key)
            => dict.ContainsKey(key) ? dict[key] : key;
    }
}