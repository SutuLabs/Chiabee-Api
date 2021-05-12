namespace UChainDB.Sutu.Backend.Services
{
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using MongoDB.Bson;
    using MongoDB.Driver;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using WebApi.Models;
    using WebApi.Services;
    using WebApi.Services.ServerCommands;

    internal class DataRefreshService : BaseRefreshService
    {
        private DateTime lastPlotterUpdate = DateTime.MinValue;
        private readonly ServerService server;
        private readonly AppSettings appSettings;
        private readonly MongoClient client;

        public DataRefreshService(
            ILogger<DataRefreshService> logger,
            IServiceProvider serviceProvider,
            ServerService server,
            IOptions<AppSettings> appSettings)
            : base(logger, serviceProvider)
        {
            this.server = server;
            this.appSettings = appSettings.Value;
            this.client = new MongoClient(this.appSettings.ConnectionString);
        }

        protected override string ServiceName => "DataRefresh";
        protected override int DefaultIntervalSeconds => 120;
        protected override int DelayStartSeconds => 3;
        protected override int GetIntervalSeconds() => 5;

        protected override async Task DoWorkAsync()
        {
            var db = this.client.GetDatabase("firstdb");
            var machineCol = db.GetCollection<BsonDocument>("firstcontainer");
            var plotterCol = db.GetCollection<BsonDocument>("plotter");

            await machineCol.InsertOneAsync(new MachineDocRoot(await this.server.GetServersInfo()).ToBsonDocument());

            if ((DateTime.UtcNow - this.lastPlotterUpdate).TotalSeconds > 20)
            {
                await machineCol.InsertOneAsync((await this.server.GetPlotterInfo()).ToBsonDocument());
                //var info = await server.GetFarmerInfo();
                this.lastPlotterUpdate = DateTime.UtcNow;
            }

        }

        private record MachineDocRoot(ServerStatus[] Servers);

    }
}