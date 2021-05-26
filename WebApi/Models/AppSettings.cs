#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace WebApi.Models
{
    public class AppSettings
    {
        public string? ConnectionString { get; set; }
        public string? LogTablePrefix { get; set; }
        public SshEntity? PlotterDefault { get; set; }
        public SshEntity[]? Plotters { get; set; }
        public SshEntity? FarmerDefault { get; set; }
        public SshEntity[]? Farmers { get; set; }
        public SshEntity? HarvesterDefault { get; set; }
        public SshEntity[]? Harvesters { get; set; }

        public class SshEntity
        {
            public string? Host { get; set; }
            public int? Port { get; set; }
            public string? Username { get; set; }
            public string? Name { get; set; }
            public string? PrivateKeyFile { get; set; }
        }

        internal SshEntity[] GetPlotters() => InheritSshEntities(PlotterDefault, Plotters).ToArray();
        internal SshEntity[] GetFarmers() => InheritSshEntities(FarmerDefault, Farmers).ToArray();
        internal SshEntity[] GetHarvesters() => InheritSshEntities(HarvesterDefault, Harvesters).ToArray();

        private IEnumerable<SshEntity> InheritSshEntities(SshEntity? baseEntity, SshEntity[]? entities)
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
                };
            }
        }
    }
}