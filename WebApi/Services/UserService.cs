namespace WebApi.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Caching.Memory;
    using Microsoft.Extensions.Logging;
    using WebApi.Entities;
    using WebApi.Helpers;

    public interface IUserService
    {
        Task<User> Authenticate(string username, string password);
        Task<IEnumerable<User>> GetAll();
        Task CreateUser(string username, string password, UserRole role, string firstName, string lastName);
    }

    public class UserService : IUserService
    {
        private readonly PersistentService persistentService;
        private readonly IMemoryCache memoryCache;
        private readonly ILogger<UserService> logger;

        public UserService(
            PersistentService persistentService,
            IMemoryCache memoryCache,
            ILogger<UserService> logger)
        {
            this.persistentService = persistentService;
            this.memoryCache = memoryCache;
            this.logger = logger;
        }

        public async Task<User> Authenticate(string username, string password)
        {
            var users = await GetUsersAsync();

            var user = await Task.Run(() => users.SingleOrDefault(x => x.Username == username && x.Password == password.Sha256()));
            if (user == null) return null;

            return user.WithoutPassword();

        }

        public async Task CreateUser(string username, string password, UserRole role, string firstName, string lastName)
        {
            var u = new UserEntity { Id = Guid.NewGuid().ToString(), Username = username, Password = password, Role = role, FirstName = firstName, LastName = lastName };
            await persistentService.WriteEntityAsync(u);
        }

        public async Task<IEnumerable<User>> GetAll()
        {
            var users = await GetUsersAsync();
            return await Task.Run(() => users.WithoutPasswords());
        }

        private async Task<List<User>> GetUsersAsync()
        {
            if (!memoryCache.TryGetValue(nameof(UserService), out List<User> users))
            {
                users = (await this.persistentService.RetrieveEntitiesAsync<UserEntity>()
                    .ToListAsync())
                    .Select(_ => _.ToUser())
                    .ToList();
                memoryCache.Set(nameof(UserService), users, TimeSpan.FromMinutes(1));
            }

            return users;
        }
    }
}