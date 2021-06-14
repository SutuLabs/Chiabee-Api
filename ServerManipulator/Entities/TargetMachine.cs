#nullable enable

namespace WebApi.Models
{
    using Renci.SshNet;

    public class TargetMachine : SshClient
    {
        public TargetMachine(string name, ServerType type, string host, int port, string username, params PrivateKeyFile[] keyFiles)
            : base(host, port, username, keyFiles)
        {
            this.Name = name;
            this.Type = type;
        }

        public TargetMachine(string name, ServerType type, string host, string username, params PrivateKeyFile[] keyFiles)
            : base(host, username, keyFiles)
        {
            this.Name = name;
            this.Type = type;
        }

        public string Name { get; }
        public ServerType Type { get; }
    }

    public enum ServerType
    {
        Plotter,
        Farmer,
        Harvester,
    }
}