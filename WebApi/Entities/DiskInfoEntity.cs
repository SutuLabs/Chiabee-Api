namespace WebApi.Entities
{
    using Microsoft.Azure.Cosmos.Table;

    public class DiskInfoEntity : TableEntity
    {
        public const string DefaultPartitionKey = nameof(DiskInfoEntity);

        public DiskInfoEntity()
        {
            PartitionKey = DefaultPartitionKey;
        }

        public DiskInfoEntity(string key) : this()
        {
            this.Key = key;
        }

        public string SnJson { get; set; }

        [IgnoreProperty]
        public string Key
        {
            get => this.RowKey;
            set => this.RowKey = value;
        }
    }
}