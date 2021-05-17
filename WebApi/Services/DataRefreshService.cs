namespace WebApi.Services
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Table;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using WebApi.Entities;
    using WebApi.Models;

    internal class DataRefreshService : BaseRefreshService
    {
        internal const string LatestStateTableName = "LatestState";
        internal const string LatestStateKeyName = "Latest";
        internal const string StateLogTableName = "StateLog";
        private DateTime lastPlotterUpdate = DateTime.MinValue;
        private readonly ServerService server;
        private readonly AppSettings appSettings;
        private CloudTable latestStateTable;
        private CloudTable stateLogTable;

        public DataRefreshService(
            ILogger<DataRefreshService> logger,
            IServiceProvider serviceProvider,
            ServerService server,
            IOptions<AppSettings> appSettings)
            : base(logger, serviceProvider)
        {
            this.server = server;
            this.appSettings = appSettings.Value;
            this.Init();
        }

        public void Init()
        {
            var storageAccount = CloudStorageAccount.Parse(this.appSettings.ConnectionString);
            var tableClient = storageAccount.CreateCloudTableClient(new TableClientConfiguration());
            this.latestStateTable = GetTable(tableClient, this.appSettings.LogTablePrefix + LatestStateTableName);
            this.stateLogTable = GetTable(tableClient, this.appSettings.LogTablePrefix + StateLogTableName);
        }

        protected override string ServiceName => "DataRefresh";
        protected override int DefaultIntervalSeconds => 120;
        protected override int DelayStartSeconds => 3;

        protected override int GetIntervalSeconds() => 5;

        protected override async Task DoWorkAsync()
        {
            var si = JsonSerializer.Serialize(await this.server.GetServersInfo());
            await LogEntityAsync(new MachineStateEntity { MachinesJson = si });

            if ((DateTime.UtcNow - this.lastPlotterUpdate).TotalSeconds > 20)
            {
                var pi = JsonSerializer.Serialize(await this.server.GetPlotterInfo());
                var fi = JsonSerializer.Serialize(await this.server.GetFarmerInfo());
                await LogEntityAsync(new FarmStateEntity { PlotterJson = pi, FarmerJson = fi });
                this.lastPlotterUpdate = DateTime.UtcNow;
            }
        }

        private CloudTable GetTable(CloudTableClient tableClient, string tableName)
        {
            var table = tableClient.GetTableReference(tableName);
            table.CreateIfNotExists();
            return table;
        }

        private async Task LogEntityAsync<T>(T entity)
            where T : class, ITableEntity
        {
            entity.RowKey = LatestStateKeyName;
            await UpdateEntityAsync(this.latestStateTable, entity);

            var date = DateTime.UtcNow;
            entity.PartitionKey += date.ToShortDateString();
            entity.RowKey = date.ToString("s");
            await UpdateEntityAsync(this.stateLogTable, entity);
        }

        private async Task<T> UpdateEntityAsync<T>(CloudTable table, T entity)
            where T : class, ITableEntity
        {
            var insertOrMergeOperation = TableOperation.InsertOrMerge(entity);

            var result = await table.ExecuteAsync(insertOrMergeOperation);
            var inserted = result.Result as T;
            return inserted;
        }
    }
}