namespace WebApi.Entities
{
    using Microsoft.Azure.Cosmos.Table;

    public record User(string Id, string FirstName, string LastName, string Username, string Password, UserRole Role);

    public class UserEntity : TableEntity
    {
        public const string DefaultPartitionKey = nameof(UserEntity);

        public UserEntity()
        {
            PartitionKey = DefaultPartitionKey;
        }

        public UserEntity(string id) : this()
        {
            this.Id = id;
        }

        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public int StoreRole { get; set; }

        [IgnoreProperty]
        public UserRole Role
        {
            get => (UserRole)this.StoreRole;
            set => this.StoreRole = (int)value;
        }

        [IgnoreProperty]
        public string Id
        {
            get => this.RowKey;
            set => this.RowKey = value;
        }

        public User ToUser()
            => new User(this.Id, this.FirstName, this.LastName, this.Username, this.Password, this.Role);
    }

    public enum UserRole
    {
        Admin,
        Vip,
        Guest,
        Operator,
        Accountant,
    }
}