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
    }

    public static class SshEntityExtensions
    {
        public static SshEntity? BasedOn(this SshEntity? entity, SshEntity? baseEntity)
        {
            if (entity == null) return default;
            if (baseEntity == null) baseEntity = new SshEntity();

            return new SshEntity
            {
                Host = entity.Host ?? baseEntity.Host,
                Port = entity.Port ?? baseEntity.Port,
                PrivateKeyFile = entity.PrivateKeyFile ?? baseEntity.PrivateKeyFile,
                Username = entity.Username ?? baseEntity.Username,
                Name = entity.Name ?? baseEntity.Name,
            };
        }

        public static IEnumerable<SshEntity> BasedOn(this IEnumerable<SshEntity>? entities, SshEntity? baseEntity)
        {
            if (entities == null) yield break;
            if (baseEntity == null) baseEntity = new SshEntity();
            foreach (var ent in entities)
            {
                var nent = ent.BasedOn(baseEntity);
                if (nent != null) yield return nent;
            }
        }
    }
}