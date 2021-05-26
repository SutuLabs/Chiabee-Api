#nullable enable

namespace WebApi.Models
{
    using Renci.SshNet;

    public class TargetMachine : SshClient
    {
        public TargetMachine(string name, string host, int port, string username, params PrivateKeyFile[] keyFiles)
            : base(host, port, username, keyFiles)
        {
            this.Name = name;
        }

        public TargetMachine(string name, string host, string username, params PrivateKeyFile[] keyFiles)
            : base(host, username, keyFiles)
        {
            this.Name = name;
        }

        public string Name { get; }
    }
}