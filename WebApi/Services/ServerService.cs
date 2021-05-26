namespace WebApi.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
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
        private TargetMachine[] plotterClients;
        private TargetMachine farmerLogClient;
        private TargetMachine[] farmerClients;
        private bool disposedValue;

        internal FixedSizedQueue<EligibleFarmerEvent> eventList = new();
        internal Dictionary<string, ErrorEvent> errorList = new();
        private readonly AppSettings appSettings;

        public ServerService(IOptions<AppSettings> appSettings)
        {
            this.appSettings = appSettings.Value;

            var farmer = this.appSettings.GetFarmers();
            var plotter = this.appSettings.GetPlotters();

            this.plotterClients = plotter.ToMachineClients().ToArray();
            this.farmerLogClient = farmer.First().ToMachineClient();
            this.farmerClients = farmer.ToMachineClients().ToArray();

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

        public async Task<ServerStatus[]> GetServersInfo() =>
            new[] { this.farmerClients, this.plotterClients }
                .SelectMany(_ => _.Select(_ => _.GetServerStatus()))
                .Where(_ => _ != null)
                .ToArray();

        public async Task<PlotterStatus[]> GetPlotterInfo() =>
            this.plotterClients
                .Select(_ => _.GetPlotterStatus())
                .Where(_ => _ != null)
                .ToArray();

        public async Task<FarmerNodeStatus[]> GetFarmerInfo() =>
            this.farmerClients
                .Select(_ => new FarmerNodeStatus(_.Name, _.GetFarmerStatus(), _.GetNodeStatus()))
                .Where(_ => _ != null)
                .ToArray();

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    var cs = new[] { this.farmerClients, this.plotterClients, new[] { this.farmerLogClient } }
                        .SelectMany(_ => _);
                    foreach (var c in cs)
                    {
                        if (c.IsConnected) c.Disconnect();
                        c.Dispose();
                    }
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