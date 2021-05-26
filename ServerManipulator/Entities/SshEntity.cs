#nullable enable

namespace WebApi.Models
{
    using System.Collections.Generic;

    public class SshEntity
    {
        public string? Host { get; set; }
        public int? Port { get; set; }
        public string? Username { get; set; }
        public string? Name { get; set; }
        public string? PrivateKeyFile { get; set; }

        public static IEnumerable<SshEntity> InheritSshEntities(SshEntity? baseEntity, SshEntity[]? entities)
        {
            if (entities == null) yield break;
            if (baseEntity == null) baseEntity = new SshEntity();
            foreach (var ent in entities)
            {
                yield return new SshEntity
                {
                    Host = ent.Host ?? baseEntity.Host,
                    Port = ent.Port ?? baseEntity.Port,
                    PrivateKeyFile = ent.PrivateKeyFile ?? baseEntity.PrivateKeyFile,
                    Username = ent.Username ?? baseEntity.Username,
                    Name = ent.Name ?? baseEntity.Name,
                };
            }
        }
    }
}