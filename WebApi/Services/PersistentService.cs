namespace WebApi.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Table;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using WebApi.Models;

    public class PersistentService
    {
        internal const string LatestStateTableName = "LatestState";
        internal const string LatestStateKeyName = "Latest";
        internal const string StateLogTableName = "StateLog";
        private readonly AppSettings appSettings;
        private CloudTable latestStateTable;
        private CloudTable stateLogTable;

        public PersistentService(
            ILogger<PersistentService> logger,
            IOptions<AppSettings> appSettings)
        {
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

        public async Task LogEntityAsync<T>(T entity)
            where T : class, ITableEntity
        {
            entity.RowKey = LatestStateKeyName;
            await UpdateEntityAsync(this.latestStateTable, entity);

            //var date = DateTime.UtcNow;
            //entity.PartitionKey += date.ToString("yyyyMMdd");
            //entity.RowKey = date.ToString("s");
            //await UpdateEntityAsync(this.stateLogTable, entity);
        }

        public async Task WriteEntityAsync<T>(T entity)
            where T : class, ITableEntity
        {
            await UpdateEntityAsync(this.latestStateTable, entity);
        }

        public async Task<T> RetrieveEntityAsync<T>()
            where T : class, ITableEntity, new()
        {
            var pk = new T().PartitionKey;
            return await RetrieveEntityAsync<T>(
                this.latestStateTable, pk, LatestStateKeyName);
        }

        public IAsyncEnumerable<T> RetrieveEntitiesAsync<T>()
            where T : class, ITableEntity, new()
        {
            var pk = new T().PartitionKey;
            return RetrieveEntitiesAsync<T>(this.latestStateTable, pk);
        }

        private CloudTable GetTable(CloudTableClient tableClient, string tableName)
        {
            var table = tableClient.GetTableReference(tableName);
            table.CreateIfNotExists();
            return table;
        }

        private async Task<T> UpdateEntityAsync<T>(CloudTable table, T entity)
            where T : class, ITableEntity
        {
            var insertOrMergeOperation = TableOperation.InsertOrMerge(entity);

            var result = await table.ExecuteAsync(insertOrMergeOperation);
            var inserted = result.Result as T;
            return inserted;
        }

        private async Task<T> RetrieveEntityAsync<T>(CloudTable table, string partitionKey, string rowKey)
            where T : class, ITableEntity
        {
            TableOperation retrieve = TableOperation.Retrieve<T>(partitionKey, rowKey);
            var result = await table.ExecuteAsync(retrieve);
            return result.Result as T;
        }

        private async IAsyncEnumerable<T> RetrieveEntitiesAsync<T>(CloudTable table, string partitionKey)
            where T : class, ITableEntity, new()
        {
            var query = new TableQuery<T>()
                .Where(TableQuery.GenerateFilterCondition(nameof(ITableEntity.PartitionKey), QueryComparisons.Equal, partitionKey));

            TableContinuationToken token = null;
            query.TakeCount = 50;
            var segmentNumber = 0;
            do
            {
                var segment = await table.ExecuteQuerySegmentedAsync(query, token);
                if (segment.Results.Count > 0) segmentNumber++;
                token = segment.ContinuationToken; // Save the continuation token for the next call to ExecuteQuerySegmentedAsync

                foreach (var entity in segment) yield return entity;
            }
            while (token != null);
        }
    }
}