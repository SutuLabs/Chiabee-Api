namespace WebApi.Entities
{
    using Microsoft.Azure.Cosmos.Table;

    public class FarmStateEntity : TableEntity
    {
        public const string DefaultPartitionKey = nameof(FarmStateEntity);

        public FarmStateEntity()
        {
            PartitionKey = DefaultPartitionKey;
        }

        public FarmStateEntity(string key) : this()
        {
            this.Key = key;
        }

        public string PlotterJson { get; set; }
        public string FarmerJson { get; set; }

        [IgnoreProperty]
        public string Key
        {
            get => this.RowKey;
            set => this.RowKey = value;
        }
    }
}