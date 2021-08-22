namespace WebApi.Entities
{
    using Microsoft.Azure.Cosmos.Table;

    public class ReceiverInfoEntity : TableEntity
    {
        public const string DefaultPartitionKey = nameof(ReceiverInfoEntity);

        public ReceiverInfoEntity()
        {
            PartitionKey = DefaultPartitionKey;
        }

        public ReceiverInfoEntity(string key) : this()
        {
            this.Key = key;
        }

        public string ReceiverJson { get; set; }

        [IgnoreProperty]
        public string Key
        {
            get => this.RowKey;
            set => this.RowKey = value;
        }
    }
}