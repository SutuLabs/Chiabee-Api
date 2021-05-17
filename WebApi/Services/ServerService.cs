namespace WebApi.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Options;
    using Renci.SshNet;
    using WebApi.Helpers;
    using WebApi.Models;
    using WebApi.Services.ServerCommands;

    public class ServerService : IDisposable
    {
        private SshClient plotterClient;
        private SshClient farmerLogClient;
        private SshClient farmerClient;
        private bool disposedValue;

        internal FixedSizedQueue<EligibleFarmerEvent> eventList = new();
        internal Dictionary<string, ErrorEvent> errorList = new();
        private readonly AppSettings appSettings;

        public ServerService(IOptions<AppSettings> appSettings)
        {
            this.appSettings = appSettings.Value;

            this.plotterClient = new SshClient("10.177.0.133", "sutu", new PrivateKeyFile(@"P:\.ssh\id_rsa.PEM"));
            this.farmerLogClient = new SshClient("10.177.0.153", "sutu", new PrivateKeyFile(@"P:\.ssh\id_rsa.PEM"));
            this.farmerClient = new SshClient("10.177.0.153", "sutu", new PrivateKeyFile(@"P:\.ssh\id_rsa.PEM"));

            this.farmerLogClient.StartTailChiaLog((err) =>
            {
                if (this.errorList.ContainsKey(err.Error))
                {
                    this.errorList.Remove(err.Error);
                }

                this.errorList.Add(err.Error, new ErrorEvent(err.Time, err.Level, err.Error));
            }, (evt) =>
            {
                this.eventList.Enqueue(evt);
            });
        }

        public async Task<ServerStatus[]> GetServersInfo()
        {
            return new[] {
                this.farmerClient.GetServerStatus() with { Name = "Farmer" },
                this.plotterClient.GetServerStatus() with { Name = "Plotter" },
            };
        }

        public async Task<PlotterServerStatus> GetPlotterInfo()
        {
            var jobs = this.plotterClient.GetPlotStatus();
            var ss = this.plotterClient.GetServerStatus() with { Name = "Plotter" };

            return new PlotterServerStatus(ss, jobs);
        }

        public async Task<FarmServerStatus> GetFarmerInfo()
        {
            var ss = this.farmerClient.GetServerStatus() with { Name = "Farmer" };
            var farm = this.farmerClient.GetFarmStatus();
            var node = this.farmerClient.GetNodeStatus();

            return new FarmServerStatus(ss, node, farm);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (this.farmerClient.IsConnected) this.farmerClient.Disconnect();
                    this.farmerClient.Dispose();
                    if (this.plotterClient.IsConnected) this.plotterClient.Disconnect();
                    this.plotterClient.Dispose();
                    if (this.farmerLogClient.IsConnected) this.farmerLogClient.Disconnect();
                    this.farmerLogClient.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}