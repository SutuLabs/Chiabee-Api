namespace WebApi.Helpers
{
    using System.Collections.Generic;
    using System.Linq;
    using Renci.SshNet;
    using WebApi.Entities;
    using static WebApi.Models.AppSettings;

    public static class ExtensionMethods
    {
        public static IEnumerable<User> WithoutPasswords(this IEnumerable<User> users)
        {
            return users.Select(x => x.WithoutPassword());
        }

        public static User WithoutPassword(this User user)
        {
            user.Password = null;
            return user;
        }

        public static SshClient ToSshClient(this SshEntity entity)
        {
            if (entity.Port is int port)
                return new SshClient(entity.Host, port, entity.Username, new PrivateKeyFile(entity.PrivateKeyFile));

            return new SshClient(entity.Host, entity.Username, new PrivateKeyFile(entity.PrivateKeyFile));
        }
    }
}