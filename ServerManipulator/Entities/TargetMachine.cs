#nullable enable

namespace WebApi.Models
{
    using Renci.SshNet;

    public class TargetMachine : SshClient
    {
        public TargetMachine(string name, ServerType type, string location, string host, int port, string username, params PrivateKeyFile[] keyFiles)
            : base(host, port, username, keyFiles)
        {
            this.Name = name;
            this.Type = type;
            this.Location = location;
        }

        public TargetMachine(string name, ServerType type, string location, string host, string username, params PrivateKeyFile[] keyFiles)
            : base(host, username, keyFiles)
        {
            this.Name = name;
            this.Type = type;
            this.Location = location;
        }

        public string Name { get; }
        public ServerType Type { get; }
        public string Location { get; }
    }

    public enum ServerType
    {
        Plotter,
        Farmer,
        Harvester,
    }
}