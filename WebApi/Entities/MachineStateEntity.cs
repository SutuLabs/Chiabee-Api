namespace WebApi.Entities
{
    using Microsoft.Azure.Cosmos.Table;

    public class MachineStateEntity : TableEntity
    {
        public const string DefaultPartitionKey = nameof(MachineStateEntity);

        public MachineStateEntity()
        {
            PartitionKey = DefaultPartitionKey;
        }

        public MachineStateEntity(string key) : this()
        {
            this.Key = key;
        }

        public string MachinesJsonGzip { get; set; }

        [IgnoreProperty]
        public string Key
        {
            get => this.RowKey;
            set => this.RowKey = value;
        }
    }
}