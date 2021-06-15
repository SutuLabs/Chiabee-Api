namespace WebApi.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Humanizer;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Newtonsoft.Json;
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

        public StatisticsService(
            ILogger<StatisticsService> logger,
            PersistentService persistentService,
            ServerService server,
            IOptions<AppSettings> appSettings)
            : base(logger, nameof(StatisticsService), 5, 20)
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
                [Key.EstimateWin] = "期望成功",
            };
        }

        public record ReportState(HarvesterStatus[] HarvesterStatuses, DateTime NodeLastTime, Dictionary<string, string> Items);

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
        }

        protected override async Task DoWorkAsync()
        {
            // get all data for preparing this state data
            var farm = await this.persistentService.RetrieveEntityAsync<FarmStateEntity>();
            var farmers = JsonConvert.DeserializeObject<FarmerNodeStatus[]>(farm.FarmerJsonGzip.Decompress());
            var farmer = farmers.First();
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
            var totalPlot = harvesters.Sum(_ => _.TotalPlot) ?? -1;
            dictionary.Add(Key.TotalPlot, totalPlot.ToString());
            dictionary.Add(Key.NetSpace, farmer.Node.Space);
            dictionary.Add(Key.TotalPlotter, plotters.Length.ToString());
            dictionary.Add(Key.TotalHarvester, harvesters.Length.ToString());
            dictionary.Add(Key.CoinPrice, prices.First().Price.ToString("0.##"));
            dictionary.Add(Key.EstimateWin, totalPlot <= 0 ? "None" : GetEstimateTime(totalPlot, farmer.Node.Space));

            var thisState = new ReportState(harvesters, farmer.Node.Time, dictionary);
            var state = await this.persistentService.RetrieveEntityAsync<StatisticsStateEntity>();
            if (state == null) state = new StatisticsStateEntity();

            if (!state.LastCheckJsonGzip.Decompress().TryParseJson<ReportState>(out var lastState))
            {
                state.LastCheckJsonGzip = JsonConvert.SerializeObject(thisState).Compress();
                state.LastHour = DateTime.UtcNow.AddHours(-1);
                state.LastDay = DateTime.UtcNow.AddDays(-1);
                state.LastHourJsonGzip = state.LastCheckJsonGzip;
                state.LastDayJsonGzip = state.LastCheckJsonGzip;
                await this.persistentService.LogEntityAsync(state);
                lastState = thisState;
            }


            // send alert immediately
            var alert = GenerateAlert(lastState, thisState);
            if (!string.IsNullOrEmpty(alert.Trim()))
            {
                await SendMessageAsync(new MarkdownMessage(alert));
            }
            state.LastCheckJsonGzip = JsonConvert.SerializeObject(thisState).Compress();


            // send hour report
            var lastSendHourReportTime = state.LastHour ?? DateTime.MinValue;
            if ((DateTime.UtcNow - lastSendHourReportTime).TotalMinutes > 55
                && this.appSettings.HourlyReportMin == DateTime.UtcNow.Minute)
            {
                if (!state.LastHourJsonGzip.Decompress().TryParseJson<ReportState>(out var lastReportState))
                    lastReportState = new ReportState(Array.Empty<HarvesterStatus>(), DateTime.MinValue, new());
                var msg = GenerateReport(lastReportState, thisState, "小时");
                await this.SendMessageAsync(new MarkdownMessage(msg));

                // update time
                state.LastHour = DateTime.UtcNow;
                state.LastHourJsonGzip = state.LastCheckJsonGzip;
            }


            // send day report
            var lastSendDayReportTime = state.LastDay ?? DateTime.MinValue;
            if ((DateTime.UtcNow - lastSendDayReportTime).TotalHours > 22
                && this.appSettings.DailyReportHour == DateTime.UtcNow.Hour)
            {
                if (!state.LastDayJsonGzip.Decompress().TryParseJson<ReportState>(out var lastReportState))
                    lastReportState = new ReportState(Array.Empty<HarvesterStatus>(), DateTime.MinValue, new());
                var msg = GenerateReport(lastReportState, thisState, "每日");
                await this.SendMessageAsync(new MarkdownMessage(msg));

                // update time
                state.LastDay = DateTime.UtcNow;
                state.LastDayJsonGzip = state.LastCheckJsonGzip;
            }

            await this.persistentService.LogEntityAsync(state);
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
                .Select(_ => $"- {texts.TryGetName(_.Key)}:"
                    + (_.isNumber || string.IsNullOrEmpty(_.prev) ? "" : $" {Style("comment", _.prev)} ->")
                    + $" {Style("info", _.str)}"
                    + (!_.isNumber ? "" : ShowDelta(_.delta)))
                );

            return msg;

            string ShowDelta(decimal number)
            {
                if (number == 0) return "";
                var str = number == (int)number ? number.ToString() : number.ToString("0.##");
                return " " + Style("comment", $"({(number > 0 ? "+" : "")}{str})");
            }

            string Style(string type, string content)
                => Wrap("font", $@"color=""{type}""", content);
            string Wrap(string tag, string attributes, string content)
                => $@"<{tag} {attributes}>{content}</{tag}>";
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
                if (appear.Any()) sb.AppendLine($"`[Harvester]`New arrival: ({string.Join(",", appear)})");
                if (disappear.Any()) sb.AppendLine($"`[Harvester]`Disappeared: ({string.Join(",", disappear)})");
            }

            foreach (var now in thisState.HarvesterStatuses)
            {
                var tsb = new StringBuilder();
                var prev = lastState.HarvesterStatuses.FirstOrDefault(_ => _.Name == now.Name);
                if (prev == null) continue;

                var appearFl = now.AbnormalFarmlands.All.Except(prev.AbnormalFarmlands.All).ToArray();
                if (appearFl.Any())
                    tsb.AppendLine($"[Harvester]{now.Name}: New abnormal farmlands ({string.Join(",", appearFl)})");

                var appearDp = now.DanglingPartitions.Except(prev.DanglingPartitions).ToArray();
                if (appearDp.Any())
                    tsb.AppendLine($"[Harvester]{now.Name}: New dangling partition ({string.Join(",", appearDp)})");

                var seconds = (DateTime.UtcNow - (now.LastPlotTime ?? DateTime.MinValue)).TotalSeconds;
                if (seconds > 100 && seconds < 1000)
                    tsb.AppendLine($"[Harvester]{now.Name}: Plot verification generation time longer than {seconds}s");

                var plotMinus = prev.TotalPlot - now.TotalPlot;
                if (plotMinus > 10)
                    tsb.AppendLine($"[Harvester]{now.Name}: Plot greatly reduced from {prev.TotalPlot} to {now.TotalPlot}");

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

        private async Task SendMessageAsync(Message message)
        {
            using var wc = new WebClient();
            wc.Headers.Add(HttpRequestHeader.ContentType, "application/json");
            var json = JsonConvert.SerializeObject(message);
            await wc.UploadStringTaskAsync(this.appSettings.WeixinReportUrl, json);
        }

        public record Message(string msgtype);
        public record MarkdownMessage(MarkdownMessageBody markdown) : Message("markdown")
        {
            public MarkdownMessage(string content) : this(new MarkdownMessageBody(content)) { }
        }
        public record MarkdownMessageBody(string content);
        public record TextMessage(TextMessageBody text) : Message("text");
        public record TextMessageBody(string content, string[] mentioned_list = null, string[] mentioned_mobile_list = null);

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
        public static bool TryParseJson<T>(this string @this, out T result)
        {
            if (string.IsNullOrEmpty(@this))
            {
                result = default;
                return false;
            }

            bool success = true;
            var settings = new JsonSerializerSettings
            {
                Error = (sender, args) => { success = false; args.ErrorContext.Handled = true; },
                MissingMemberHandling = MissingMemberHandling.Error
            };
            result = JsonConvert.DeserializeObject<T>(@this, settings);
            return success;
        }

        public static string TryGet(this Dictionary<string, string> dict, string key)
            => dict.ContainsKey(key) ? dict[key] : null;

        public static string TryGetName(this Dictionary<string, string> dict, string key)
            => dict.ContainsKey(key) ? dict[key] : key;
    }
}