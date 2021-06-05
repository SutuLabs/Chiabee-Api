namespace WebApi.Entities
{
    using Microsoft.Azure.Cosmos.Table;

    public class PriceStateEntity : TableEntity
    {
        public const string DefaultPartitionKey = nameof(PriceStateEntity);

        public PriceStateEntity()
        {
            PartitionKey = DefaultPartitionKey;
        }

        public PriceStateEntity(string key) : this()
        {
            this.Key = key;
        }

        public string PricesJson { get; set; }

        [IgnoreProperty]
        public string Key
        {
            get => this.RowKey;
            set => this.RowKey = value;
        }
    }
}