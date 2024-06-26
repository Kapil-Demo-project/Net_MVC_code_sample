using Blt.RemoteManagement.REST.Authentication;
using Blt.RemoteManagement.REST.BackgroundServices;
using Blt.RemoteManagement.REST.Data;
using Blt.RemoteManagement.REST.Middlewares;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using VueCliMiddleware;

namespace Blt.RemoteManagement.REST
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        private bool IsSharedHostUsedForClientAndServer => Configuration.GetValue<bool>("Setting:IsSharedHostUsedForClientAndServer");

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCors();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(
                    Configuration.GetConnectionString("DefaultConnection")));
            services.AddDatabaseDeveloperPageExceptionFilter();
            services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>();

            services.AddResponseCompression(options =>
            {
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
            });
            services.Configure<BrotliCompressionProviderOptions>(options => { options.Level = CompressionLevel.Fastest; });
            services.Configure<GzipCompressionProviderOptions>(options => { options.Level = CompressionLevel.Fastest; });

            services.AddRouting(options => options.LowercaseUrls = true);
            services.AddControllers();

            services.AddSpaStaticFiles(configuration =>
            {
                configuration.RootPath = "dist";
            });

            services.AddSwaggerApi();

            services.AddJwtTokenAuthentication(Configuration);

            services.AddMemoryCache();

            // Config
            services.Configure<TokenManagementConfig>(Configuration.GetSection(nameof(TokenManagementConfig)));
            services.Configure<InitialAdminUserIfDbIsEmpty>(Configuration.GetSection(nameof(InitialAdminUserIfDbIsEmpty)));

            // Bg services
            services.AddHostedService<MigrateDatabaseBgService>();
            services.AddHostedService<CreateInitialDataIfDbIsEmptyBgService>();
            services.AddScoped((provider) =>
            {
                provider = provider.CreateScope().ServiceProvider;
                return new Func<SingletonBackgroundJobConfig, SingletonBackgroundJobService>(
                    (jobConfig) => new SingletonBackgroundJobService(
                        jobConfig,
                        provider.GetRequiredService<IMemoryCache>(),
                        provider.GetRequiredService<ILogger<SingletonBackgroundJobService>>()
                    )
                );
            });

            // Blt services
            services.AddSingleton<Printers.PrinterHelper>();
            services.AddSingleton<Network.NetworkHelper>();
            services.AddSingleton<Passepartout.PassepartoutHelper>();
            services.AddSingleton<Windows.WindowsHelper>();
            services.AddSingleton<Setup.SetupHelper>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseMigrationsEndPoint();
            }

            if (!IsSharedHostUsedForClientAndServer)
            {
                app.UseCors(builder => builder
                    .AllowCredentials()
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    //.AllowAnyOrigin()
                    .SetIsOriginAllowedToAllowWildcardSubdomains()
                    .SetIsOriginAllowed(origin => true)
                );
            }

            if (env.IsDevelopment() && !IsSharedHostUsedForClientAndServer)
            {
                app.UseRewriter(new RewriteOptions().AddRedirect("^$", SwaggerApiMiddleware.GetSwaggerUiUrl(Configuration)));
                app.UseRewriter(new RewriteOptions().AddRedirect("^index.html$", SwaggerApiMiddleware.GetSwaggerUiUrl(Configuration)));
            }

            app.UseHttpsRedirection();          

            // put it before static files
            app.UseResponseCompression();

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(env.WebRootPath)
            });
            app.UseSpaStaticFiles();
            app.UseDefaultFiles();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            if (Configuration.GetValue<bool?>("skip") == null)
            {
                app.UseLicenseMiddleware();
            }

            // Should be after authentication
            app.UseSwaggerApi(Configuration);

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            app.UseSpa(spa =>
            {
                spa.Options.StartupTimeout = TimeSpan.FromMinutes(4);
                spa.Options.SourcePath = "../Blt.RemoteManagement.WebDashboard";
                if (env.IsDevelopment() && IsSharedHostUsedForClientAndServer)
                {
                    spa.UseVueCli(npmScript: "serve");
                }
            });            
        }
    }
}
