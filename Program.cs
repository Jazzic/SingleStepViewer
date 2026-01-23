using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SingleStepViewer.Configuration;
using SingleStepViewer.Data;
using SingleStepViewer.Data.Entities;
using SingleStepViewer.Services;
using SingleStepViewer.Services.Interfaces;
using SingleStepViewer.BackgroundServices;
using SingleStepViewer.Components;
using SingleStepViewer.Components.Hubs;
using FluentValidation;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .Build())
    .CreateLogger();

try
{
    Log.Information("Starting SingleStepViewer web application");

    var builder = WebApplication.CreateBuilder(args);

    // Add Serilog
    builder.Host.UseSerilog();

    // Add services to the container
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    // Configure options
    builder.Services.Configure<PlaybackOptions>(builder.Configuration.GetSection(PlaybackOptions.SectionName));
    builder.Services.Configure<SchedulingOptions>(builder.Configuration.GetSection(SchedulingOptions.SectionName));
    builder.Services.Configure<VideoOptions>(builder.Configuration.GetSection(VideoOptions.SectionName));

    // Add database
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

    // Add Identity
    builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

    // Add authentication and authorization
    builder.Services.AddAuthentication();
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    });

    // Add SignalR
    builder.Services.AddSignalR();

    // Add services
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<IPlaylistService, PlaylistService>();
    builder.Services.AddScoped<IPlaylistItemService, PlaylistItemService>();
    builder.Services.AddScoped<IVideoService, VideoService>();
    builder.Services.AddScoped<IDownloadService, DownloadService>();
    builder.Services.AddSingleton<IPlaybackService, PlaybackService>(); // Singleton to maintain VLC state
    builder.Services.AddScoped<IQueueManager, QueueManager>();
    builder.Services.AddScoped<IConfigurationService, ConfigurationService>();

    // Add background services
    builder.Services.AddHostedService<PlaybackEngineService>();
    builder.Services.AddHostedService<VideoDownloaderService>();

    // Add FluentValidation
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    // Add API services
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // Add HTTP context accessor
    builder.Services.AddHttpContextAccessor();

    var app = builder.Build();

    // Configure the HTTP request pipeline
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    else
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();

    app.UseAuthentication();
    app.UseAuthorization();

    app.UseAntiforgery();

    // Map Razor components
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    // Map SignalR hub
    app.MapHub<PlaybackHub>("/playbackHub");

    // Map API endpoints (will be implemented in Phase 5)
    // MapUserEndpoints(app);
    // MapPlaylistEndpoints(app);
    // MapPlaybackEndpoints(app);
    // MapAdminEndpoints(app);

    // Ensure database is created and migrated
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Database.Migrate();

        // Create default admin user if it doesn't exist
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        await EnsureRolesAsync(roleManager);
        await EnsureAdminUserAsync(userManager);
    }

    // Ensure video storage directory exists
    var videoOptions = builder.Configuration.GetSection(VideoOptions.SectionName).Get<VideoOptions>();
    if (videoOptions != null && !string.IsNullOrEmpty(videoOptions.StoragePath))
    {
        Directory.CreateDirectory(videoOptions.StoragePath);
    }

    Log.Information("SingleStepViewer web application started successfully");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Helper methods
static async Task EnsureRolesAsync(RoleManager<IdentityRole> roleManager)
{
    var roles = new[] { "Admin", "Regular" };
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }
}

static async Task EnsureAdminUserAsync(UserManager<ApplicationUser> userManager)
{
    const string adminEmail = "admin@singlestep.local";
    const string adminPassword = "Admin123!";

    var adminUser = await userManager.FindByEmailAsync(adminEmail);
    if (adminUser == null)
    {
        adminUser = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(adminUser, adminPassword);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(adminUser, "Admin");
            Log.Information("Default admin user created: {Email} / {Password}", adminEmail, adminPassword);
        }
        else
        {
            Log.Error("Failed to create default admin user: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }
}
