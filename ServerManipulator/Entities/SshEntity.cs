#nullable enable

namespace WebApi.Models
{
    using System;
    using System.Collections.Generic;

    public class SshEntity
    {
        public string[]? Hosts { get; set; }
        public string? Location { get; set; }
        public ServerType? Type { get; set; }
        public PlotProgram? Program { get; set; }
        public int? Port { get; set; }
        public string? Username { get; set; }
        public string? Name { get; set; }
        public string? PrivateKeyFile { get; set; }
    }

    public static class SshEntityExtensions
    {
        public static SshEntity? SetType(this SshEntity? entity, ServerType type)
        {
            if (entity == null) return default;
            entity.Type = type;
            return entity;
        }

        public static IEnumerable<SshEntity> SetType(this IEnumerable<SshEntity> entities, ServerType type)
        {
            if (entities == null) yield break;
            foreach (var ent in entities)
            {
                var nent = ent.SetType(type);
                if (nent != null) yield return nent;
            }
        }

        public static SshEntity? BasedOn(this SshEntity? entity, SshEntity? baseEntity)
        {
            if (entity == null) return default;
            if (baseEntity == null) baseEntity = new SshEntity();

            return new SshEntity
            {
                Hosts = entity.Hosts ?? baseEntity.Hosts,
                Location = entity.Location ?? baseEntity.Location,
                Type = entity.Type ?? baseEntity.Type,
                Program = entity.Program ?? baseEntity.Program,
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

        public static MachineProperty ToProperty(this SshEntity entity)
        {
            return new MachineProperty(
                entity.Name ?? throw new ArgumentNullException(nameof(entity.Name)),
                entity.Type ?? ServerType.Undefined,
                entity.Location ?? throw new ArgumentNullException(nameof(entity.Location)),
                entity.Program ?? PlotProgram.MadmaxPlotter,
                entity.Hosts ?? Array.Empty<string>());
            ;
        }
    }
}