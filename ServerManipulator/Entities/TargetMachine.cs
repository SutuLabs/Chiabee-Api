#nullable enable

namespace WebApi.Models
{
    using Renci.SshNet;

    public class TargetMachine : SshClient
    {
        public TargetMachine(MachineProperty properties, string host, int port, string username, params PrivateKeyFile[] keyFiles)
            : base(host, port, username, keyFiles)
        {
            this.Properties = properties;
        }

        public TargetMachine(MachineProperty properties, string host, string username, params PrivateKeyFile[] keyFiles)
            : base(host, username, keyFiles)
        {
            this.Properties = properties;
        }

        public MachineProperty Properties { get; }
        public string Location => Properties.Location;
        public string Name => Properties.Name;
        public ServerType Type => Properties.Type;
    }

    public record MachineProperty(string Name, ServerType Type, string Location, PlotProgram Program, string[] Hosts);

    public enum PlotProgram
    {
        MadmaxPlotter,
        Plotman,
    }

    public enum ServerType
    {
        Undefined,
        Plotter,
        Farmer,
        Harvester,
    }
}