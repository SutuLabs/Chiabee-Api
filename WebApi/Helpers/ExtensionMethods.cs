namespace WebApi.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text;
    using Renci.SshNet;
    using WebApi.Entities;
    using WebApi.Models;
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

        public static TargetMachine ToMachineClient(this SshEntity entity)
        {
            var machine = entity.Port is int port
                ? new TargetMachine(
                    entity.Name, entity.Host, port, entity.Username, new PrivateKeyFile(entity.PrivateKeyFile))
                : new TargetMachine(
                    entity.Name, entity.Host, entity.Username, new PrivateKeyFile(entity.PrivateKeyFile));

            machine.ConnectionInfo.Timeout = new TimeSpan(0, 0, 5);
            machine.ConnectionInfo.RetryAttempts = 1;

            return machine;
        }

        public static IEnumerable<TargetMachine> ToMachineClients(this IEnumerable<SshEntity> entity)
        {
            return entity.Select(_ => _.ToMachineClient());
        }

        public static string Compress(this string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var bytes = Encoding.Unicode.GetBytes(s);
            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(mso, CompressionMode.Compress))
                {
                    msi.CopyTo(gs);
                }
                return Convert.ToBase64String(mso.ToArray());
            }
        }

        public static string Decompress(this string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var bytes = Convert.FromBase64String(s);
            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(msi, CompressionMode.Decompress))
                {
                    gs.CopyTo(mso);
                }
                return Encoding.Unicode.GetString(mso.ToArray());
            }
        }
    }
}