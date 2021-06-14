namespace WebApi.Entities
{
    using System;
    using Microsoft.Azure.Cosmos.Table;

    public class StatisticsStateEntity : TableEntity
    {
        public const string DefaultPartitionKey = nameof(StatisticsStateEntity);

        public StatisticsStateEntity()
        {
            PartitionKey = DefaultPartitionKey;
        }

        public StatisticsStateEntity(string key) : this()
        {
            this.Key = key;
        }

        public DateTime? LastHour { get; set; }
        public string LastHourJsonGzip { get; set; }
        public DateTime? LastDay{ get; set; }
        public string LastDayJsonGzip { get; set; }
        public string LastCheckJsonGzip { get; set; }

        [IgnoreProperty]
        public string Key
        {
            get => this.RowKey;
            set => this.RowKey = value;
        }
    }
}