using GW2Stonks.Components;
using GW2Stonks.Data;
using Microsoft.EntityFrameworkCore;
using Radzen;

namespace GW2Stonks
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            // Radzen UI services (dialogs, notifications, tooltips, context menus).
            builder.Services.AddRadzenComponents();

            // EF Core against the local MariaDB instance. The connection string lives in
            // user-secrets (see README); a placeholder default is in appsettings.json so
            // migrations can be generated offline. Server version is pinned to avoid a
            // database round-trip at startup / design time.
            var connectionString = builder.Configuration.GetConnectionString("GW2Stonks")
                ?? throw new InvalidOperationException(
                    "Connection string 'GW2Stonks' not found. Set it with: " +
                    "dotnet user-secrets set \"ConnectionStrings:GW2Stonks\" \"<value>\"");
            var serverVersion = new MariaDbServerVersion(new Version(10, 11, 6));
            builder.Services.AddDbContextFactory<AppDbContext>(options =>
                options.UseMySql(connectionString, serverVersion));

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            app.UseHttpsRedirection();

            app.UseAntiforgery();

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
