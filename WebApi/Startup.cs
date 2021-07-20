namespace WebApi
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using WebApi.Helpers;
    using WebApi.Services;
    using Microsoft.AspNetCore.Authentication;
    using WebApi.Models;
    using WebApi.Entities;
    using WebApi.Controllers;
    using System.Threading.Tasks;
    using System.Collections.Generic;
    using System;

    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<AppSettings>(Configuration.GetSection("AppSettings"));
            services.AddCors();
            services.AddControllers();
            services.AddSignalR();
            services.AddMemoryCache();

            services.AddAuthentication("BasicAuthentication")
                .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("BasicAuthentication", null);

            services.AddAuthorization(options =>
            {
                options.AddPolicy(nameof(UserRole.Admin), policy => policy.RequireClaim(nameof(UserRole), nameof(UserRole.Admin)));
                options.AddPolicy(nameof(UserRole.Operator), policy => policy.RequireClaim(nameof(UserRole), nameof(UserRole.Operator), nameof(UserRole.Admin)));
                options.AddPolicy(nameof(UserRole.Accountant), policy => policy.RequireClaim(nameof(UserRole), nameof(UserRole.Accountant), nameof(UserRole.Admin)));
                options.AddPolicy(nameof(UserRole.Guest), policy => policy.RequireClaim(nameof(UserRole), nameof(UserRole.Guest), nameof(UserRole.Admin)));
                options.AddPolicy(nameof(UserRole.Vip), policy => policy.RequireClaim(nameof(UserRole), nameof(UserRole.Vip), nameof(UserRole.Admin)));
            });

            services.AddScoped<IUserService, UserService>();
            services.AddSingleton<ServerService, ServerService>();
            services.AddSingleton<PersistentService, PersistentService>();
            services.AddHostedService<RefreshServerInfoService>();
            services.AddHostedService<RefreshFarmInfoService>();
            services.AddHostedService<RefreshPriceService>();
            services.AddHostedService<StatisticsService>();
            services.AddHostedService<RsyncScheduleService>();

            Services.ServerCommands.CommandHelper.SetSshNetConcurrency(100);
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, PersistentService persistentService)
        {
            app.UseRouting();

            app.UseCors(x => x
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader());

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHub<EventHub>("/hub/events");
            });

            this.InitUsers(persistentService).Wait();
        }

        private async Task InitUsers(PersistentService persistentService)
        {
            var nus = await persistentService.RetrieveEntitiesAsync<UserEntity>()
                .ToListAsync();
            if (nus.Count > 0) return;

            var users = new List<UserEntity>
            {
                new UserEntity { Id = Guid.NewGuid().ToString(), FirstName = "Test", LastName = "User", Username = "test", Password = "test@123".Sha256(), Role = UserRole.Operator, },
                new UserEntity { Id = Guid.NewGuid().ToString(), FirstName = "Admin", LastName = "User", Username = "admin", Password = "admin@123".Sha256(), Role = UserRole.Admin, },
            };

            foreach (var u in users)
            {
                await persistentService.WriteEntityAsync(u);
            }
        }
    }
}
