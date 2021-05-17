namespace WebApi.Models
{
    public class AppSettings
    {
        public string ConnectionString { get; set; }
        public string LogTablePrefix { get; set; }
        public SshEntity PlotterDefault { get; set; }
        public SshEntity[] Plotters { get; set; }
        public SshEntity FarmerDefault { get; set; }
        public SshEntity[] Farmers { get; set; }

        public class SshEntity
        {
            public string Host { get; set; }
            public int Port { get; set; }
            public string Username { get; set; }
            public string PrivateKeyFile { get; set; }
        }
    }
}