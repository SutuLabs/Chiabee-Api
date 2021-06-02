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

            services.AddAuthentication("BasicAuthentication")
                .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("BasicAuthentication", null);

            services.AddAuthorization(options =>
            {
                options.AddPolicy(nameof(UserRole.Admin), policy => policy.RequireClaim(nameof(UserRole), nameof(UserRole.Admin)));
                options.AddPolicy(nameof(UserRole.Guest), policy => policy.RequireClaim(nameof(UserRole), nameof(UserRole.Guest)));
                options.AddPolicy(nameof(UserRole.Vip), policy => policy.RequireClaim(nameof(UserRole), nameof(UserRole.Vip)));
            });

            services.AddScoped<IUserService, UserService>();
            services.AddSingleton<ServerService, ServerService>();
            services.AddSingleton<PersistentService, PersistentService>();
            services.AddHostedService<DataRefreshService>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseRouting();

            app.UseCors(x => x
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader());

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints => endpoints.MapControllers());
        }
    }
}
