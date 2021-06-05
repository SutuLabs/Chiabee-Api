namespace WebApi.Services
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using WebApi.Entities;
    using WebApi.Models;

    internal class RefreshPriceService : BaseRefreshService
    {
        private readonly PersistentService persistentService;
        private readonly ServerService server;
        private readonly AppSettings appSettings;

        public RefreshPriceService(
            ILogger<RefreshPriceService> logger,
            PersistentService persistentService,
            ServerService server,
            IOptions<AppSettings> appSettings)
            : base(logger, nameof(RefreshPriceService), 20, 30 * 60)
        {
            this.persistentService = persistentService;
            this.server = server;
            this.appSettings = appSettings.Value;
        }

        protected override async Task DoWorkAsync()
        {
            var urls = new[] {
                "https://www.coinbase.com/api/v2/assets/prices/chia-network?base=USDT",
                "https://www.coinbase.com/api/v2/assets/prices/chia-network?base=CNY",
            };
            using WebClient wc = new WebClient();
            var prices = new List<PriceEntity>();

            foreach (var url in urls)
            {
                var str = wc.DownloadString(url);

                var priceBase = Newtonsoft.Json.JsonConvert.DeserializeObject<CoinBasePriceBase>(str);
                var d = priceBase.data;
                var from = d.@base;
                var to = d.currency;
                var price = d.prices.latest;
                var time = d.prices.latest_price.timestamp;
                var entity = new PriceEntity("coinbase", from, to, price, time);
                prices.Add(entity);
            }

            var pricesJson = JsonSerializer.Serialize(prices.ToArray());
            await this.persistentService.LogEntityAsync(new PriceStateEntity { PricesJson = pricesJson });
        }
    }

    public record PriceEntity(string Source, string From, string To, decimal Price, DateTime Time);

    public record CoinBasePriceBase(CoinBasePriceData data);
    public record CoinBasePriceData(string @base, string currency, CoinBasePriceDataPrices prices);
    public record CoinBasePriceDataPrices(decimal latest, CoinBasePriceDataPrice latest_price);
    public record CoinBasePriceDataPrice(CoinBasePriceDataPriceAmount amount, DateTime timestamp);
    public record CoinBasePriceDataPriceAmount(decimal amount, string currency);
}