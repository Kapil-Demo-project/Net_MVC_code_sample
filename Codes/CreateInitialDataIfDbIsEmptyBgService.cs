using Blt.RemoteManagement.REST.Authentication;
using Blt.RemoteManagement.REST.Constants;
using Blt.RemoteManagement.REST.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Blt.RemoteManagement.REST.BackgroundServices
{
    class CreateInitialDataIfDbIsEmptyBgService : IHostedService
    {
        private readonly ILogger _logger;
        private readonly IServiceProvider _serviceProvider;

        public CreateInitialDataIfDbIsEmptyBgService(IServiceProvider serviceProvider, ILogger<MigrateDatabaseBgService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogWarning("Ensuring initial admin user exists when db is newly created...");

            using var scope = _serviceProvider.CreateScope();
            var initialUser = scope.ServiceProvider.GetRequiredService<IOptions<InitialAdminUserIfDbIsEmpty>>().Value;
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            if (!await userManager.Users.AnyAsync(cancellationToken))
            {
                var result = await userManager.CreateAsync(new IdentityUser
                {
                    UserName = initialUser.Username,
                    NormalizedUserName = "Admin",
                    Email = "admin@blt.com",
                    EmailConfirmed = true,
                }, initialUser.Password);

                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(await userManager.FindByNameAsync(initialUser.Username), RoleNames.Administrator);
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
