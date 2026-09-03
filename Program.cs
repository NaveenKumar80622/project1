using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
//using Npgsql;
using PickNBook.Api.Data;
using PickNBook.Api.Middleware;
using PickNBook.Api.Models;
using PickNBook.Api.Services;
using PickNBook.Api.Services.Interfaces;
using PickNBook.Api.Services.Implementations;
using System.Text;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning) // Ignore noisy system logs
    .WriteTo.Console() // Console gets everything
    .WriteTo.Logger(lc => lc
        // Only capture our specific Bus loggers into the file
        .Filter.ByIncludingOnly(evt => 
            evt.Properties.TryGetValue("SourceContext", out var ctx) && 
            (ctx.ToString().Contains("BusRequestLoggingMiddleware") || 
             ctx.ToString().Contains("SrdvBusLoggingHandler")))
        .WriteTo.Async(a => a.File("Logs/bus-api-.txt", rollingInterval: RollingInterval.Day), blockWhenFull: false)
    )
    .WriteTo.Logger(lc => lc
        // Only capture our specific Flight loggers into the file
        .Filter.ByIncludingOnly(evt => 
            evt.Properties.TryGetValue("SourceContext", out var ctx) && 
            (ctx.ToString().Contains("FlightRequestLoggingMiddleware") || 
             ctx.ToString().Contains("SrdvFlightLoggingHandler")))
        .WriteTo.Async(a => a.File("Logs/flight-api-.txt", rollingInterval: RollingInterval.Day), blockWhenFull: false)
    )
    .WriteTo.Logger(lc => lc
        // A dedicated file just for your current testing session so it's easy to find
        .Filter.ByIncludingOnly(evt => 
            evt.Properties.TryGetValue("SourceContext", out var ctx) && 
            (ctx.ToString().Contains("FlightRequestLoggingMiddleware") || 
             ctx.ToString().Contains("SrdvFlightLoggingHandler")))
        .WriteTo.Async(a => a.File("Logs/test-session-logs.txt", rollingInterval: RollingInterval.Infinite), blockWhenFull: false)
    )
    .WriteTo.Logger(lc => lc
        // Only capture our specific Hotel loggers into the file
        .Filter.ByIncludingOnly(evt => 
            evt.Properties.TryGetValue("SourceContext", out var ctx) && 
            (ctx.ToString().Contains("HotelRequestLoggingMiddleware") || 
             ctx.ToString().Contains("SrdvHotelLoggingHandler")))
        .WriteTo.Async(a => a.File("Logs/hotel-api-.txt", rollingInterval: RollingInterval.Day), blockWhenFull: false)
    )
    .WriteTo.Logger(lc =>
        // Dedicated SMS OTP log file — clean, shareable, no noise
        lc.Filter.ByIncludingOnly(evt =>
            evt.Properties.TryGetValue("SourceContext", out var ctx) &&
            (ctx.ToString().Contains("SmsOtpRequestLoggingMiddleware") ||
             ctx.ToString().Contains("PointerItLoggingHandler")))
        .WriteTo.Async(a => a.File("Logs/sms-api-.txt", rollingInterval: RollingInterval.Day), blockWhenFull: false)
    )
    .CreateLogger();

builder.Host.UseSerilog();

// ---------------- SERVICES ----------------
builder.Services.Configure<PickNBook.Api.Models.Config.SrdvSettings>(
    builder.Configuration.GetSection("Srdv"));

builder.Services.Configure<CashfreeSettings>(
    builder.Configuration.GetSection("Cashfree"));

builder.Services.AddHttpClient<ICashfreeService, CashfreeService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IBookingOrchestratorService, BookingOrchestratorService>();
builder.Services.AddScoped<ICancellationRefundCalculator, CancellationRefundCalculator>();

builder.Services.AddTransient<PickNBook.Api.Infrastructure.Logging.SrdvFlightLoggingHandler>();
builder.Services.AddHttpClient<ISrdvFlightService, SrdvFlightService>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        UseProxy = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    .AddHttpMessageHandler<PickNBook.Api.Infrastructure.Logging.SrdvFlightLoggingHandler>();
builder.Services.AddTransient<PickNBook.Api.Infrastructure.Logging.SrdvHotelLoggingHandler>();
builder.Services.AddHttpClient<IHotelService, SrdvHotelService>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        UseProxy = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    .AddHttpMessageHandler<PickNBook.Api.Infrastructure.Logging.SrdvHotelLoggingHandler>();
builder.Services.AddTransient<PickNBook.Api.Infrastructure.Logging.SrdvBusLoggingHandler>();
builder.Services.AddHttpClient<ISrdvBusService, SrdvBusService>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        UseProxy = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    .AddHttpMessageHandler<PickNBook.Api.Infrastructure.Logging.SrdvBusLoggingHandler>();

builder.Services.AddSingleton<HotelCityCacheService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<HotelCityCacheService>());

builder.Services.AddSingleton<BusCityCacheService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<BusCityCacheService>());

builder.Services.AddHttpClient("TicketEmailApi", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});

builder.Services.AddHttpClient<IGeoIpService, GeoIpService>();

builder.Services.AddScoped<IBookingNotificationService, BookingNotificationService>();

builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IFlightAnalyticsService, FlightAnalyticsService>();
builder.Services.AddScoped<IFeaturedOffersService, FeaturedOffersService>();

// SRDV Master Data Ingestion & Optimized Places Service
builder.Services.Configure<PickNBook.Api.Models.Config.SrdvMasterDataSettings>(
    builder.Configuration.GetSection("SrdvMasterData"));
builder.Services.AddHttpClient<PickNBook.Api.Services.Interfaces.ISrdvMasterDataDownloader, PickNBook.Api.Services.Implementations.SrdvMasterDataDownloader>();
builder.Services.AddScoped<PickNBook.Api.Services.Interfaces.ISrdvSqlDumpParser, PickNBook.Api.Services.Implementations.SrdvSqlDumpParser>();
builder.Services.AddScoped<PickNBook.Api.Services.Interfaces.ISrdvMasterDataImporter, PickNBook.Api.Services.Implementations.SrdvMasterDataImporter>();
builder.Services.AddScoped<PickNBook.Api.Services.Interfaces.IPlacesService, PickNBook.Api.Services.Implementations.PlacesService>();
// Email Settings
builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection("EmailSettings"));
builder.Services.Configure<WhatsAppSettings>(
    builder.Configuration.GetSection("WhatsAppSettings"));
builder.Services.Configure<SmsSettings>(
    builder.Configuration.GetSection("SmsSettings"));
builder.Services.Configure<PickNBook.Api.Models.Config.RefundPolicyOptions>(
    builder.Configuration.GetSection("RefundPolicy"));

builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<PickNBook.Api.Services.Notifications.Interfaces.IEmailProvider, EmailService>();
builder.Services.AddScoped<IEmailTemplateService, EmailTemplateService>();
builder.Services.AddHostedService<PickNBook.Api.Services.Background.EmailReminderHostedService>();
builder.Services.AddHttpClient<IWhatsAppService, WhatsAppService>();
builder.Services.AddHttpClient<ISmsService, SmsService>();

// Centralized Notification Architecture DI
builder.Services.Configure<PickNBook.Api.Models.Config.PointerItSmsSettings>(
    builder.Configuration.GetSection("PointerItSms"));
builder.Services.Configure<PickNBook.Api.Models.Config.NotificationRoutingSettings>(
    builder.Configuration.GetSection("NotificationRouting"));

builder.Services.AddScoped<PickNBook.Api.Services.Notifications.Interfaces.INotificationService, PickNBook.Api.Services.Notifications.Implementations.NotificationService>();
builder.Services.AddScoped<PickNBook.Api.Services.Notifications.Interfaces.IOtpService, PickNBook.Api.Services.Notifications.Implementations.OtpService>();
builder.Services.AddSingleton<PickNBook.Api.Services.Notifications.Interfaces.ISmsProvider, PickNBook.Api.Services.Notifications.Providers.MockSmsProvider>();
builder.Services.AddTransient<PickNBook.Api.Infrastructure.Logging.PointerItLoggingHandler>();
builder.Services.AddHttpClient(nameof(PickNBook.Api.Services.Notifications.Providers.PointerItSmsProvider))
    .RemoveAllLoggers()
    .AddHttpMessageHandler<PickNBook.Api.Infrastructure.Logging.PointerItLoggingHandler>();
builder.Services.AddSingleton<PickNBook.Api.Services.Notifications.Interfaces.ISmsProvider, PickNBook.Api.Services.Notifications.Providers.PointerItSmsProvider>();
builder.Services.AddSingleton<PickNBook.Api.Services.Notifications.Interfaces.IWhatsAppProvider, PickNBook.Api.Services.Notifications.Providers.MockWhatsAppProvider>();
builder.Services.AddHostedService<PickNBook.Api.Services.Background.NotificationOutboxWorker>();

builder.Services.AddScoped<IExclusiveOfferSubscriptionService, ExclusiveOfferSubscriptionService>();
builder.Services.AddScoped<ITicketPdfService, TicketPdfService>();
builder.Services.AddScoped<ITicketEmailService, TicketEmailService>();
builder.Services.AddScoped<IBookingHistoryService, BookingHistoryService>();
builder.Services.AddScoped< IAdminFeaturedOffersService, AdminFeaturedOffersService>();
builder.Services.AddScoped<
    IBusPromotionEngineService,
    BusPromotionEngineService>();
builder.Services.AddScoped<IFlightMarkupService, FlightMarkupService>();
builder.Services.AddScoped<IHotelMarkupService, HotelMarkupService>();
builder.Services.AddScoped<IFlightPromotionEngine, FlightPromotionEngine>();
builder.Services.AddScoped<IFlightPricingService, FlightPricingService>();
builder.Services.AddScoped<IUserBookingHistoryService, UserBookingHistoryService>();
builder.Services.AddScoped<IAgentWalletService, AgentWalletService>();
// JWT Service
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAboutUsService, AboutUsService>();
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
builder.Services.AddScoped<IBlogsService, BlogsService>();
builder.Services.AddScoped<Microsoft.AspNetCore.Identity.IPasswordHasher<User>, Microsoft.AspNetCore.Identity.PasswordHasher<User>>();
builder.Services.AddSingleton<IBackgroundJobQueue, BackgroundJobQueue>();
builder.Services.AddHostedService<BackgroundJobExecutor>();
builder.Services.AddScoped<ISecurityService, SecurityService>();
builder.Services.AddHostedService<SecurityNotificationHostedService>();
builder.Services.AddScoped<ISecurityCounterService, SecurityCounterService>();
builder.Services.AddHostedService<SecurityBackgroundService>();
builder.Services.AddHostedService<PickNBook.Api.Services.Background.HotelCacheWarmerHostedService>();
builder.Services.AddHostedService<PickNBook.Api.Services.Background.FulfillmentRecoveryWorker>();

// Database



var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection is not configured.");

// Serverless Postgres can close idle sockets; keepalive + retries prevents transient failures.


var serverVersion = new MySqlServerVersion(new Version(8, 0, 35));
bool isTesting = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.FullName != null && a.FullName.Contains("Test", StringComparison.OrdinalIgnoreCase));

if (isTesting)
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseMySql(
            connectionString,
            serverVersion,
            mysqlOptions =>
            {
                mysqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);
            }
        ));
}
else
{
    builder.Services.AddDbContextPool<AppDbContext>(options =>
        options.UseMySql(
            connectionString,
            serverVersion,
            mysqlOptions =>
            {
                mysqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);
            }
        ));
}


// ---------------- CORS CONFIG ----------------

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else
        {
            policy.SetIsOriginAllowed(origin => new Uri(origin).Host == "localhost")
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});

// ---------------- JWT AUTH ----------------
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;


})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,

        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],

        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"]!))
    };
});

builder.Services.AddControllers(options =>
{
    options.Filters.Add<PickNBook.Api.Filters.InjectClientIpAttribute>();
})
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddOutputCache();

// ---------------- SWAGGER + JWT BUTTON ----------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter JWT token like this: Bearer {your token}"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });

    // Include XML comments for Swagger descriptions and examples
    var xmlFilename = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }

    // Render CheckInDate/CheckOutDate as date pickers in Swagger UI
    options.SchemaFilter<PickNBook.Api.Models.Config.DateFormatSchemaFilter>();
});

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
});

app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
app.UseMiddleware<RequestProfilingMiddleware>();

// Custom Bus User Flow Logging Middleware
app.UseWhen(context => context.Request.Path.StartsWithSegments("/api/BusBookings", StringComparison.OrdinalIgnoreCase), appBuilder =>
{
    appBuilder.UseMiddleware<PickNBook.Api.Middleware.BusRequestLoggingMiddleware>();
});

// Custom Flight User Flow Logging Middleware
app.UseWhen(context => context.Request.Path.StartsWithSegments("/api/flight", StringComparison.OrdinalIgnoreCase), appBuilder =>
{
    appBuilder.UseMiddleware<PickNBook.Api.Middleware.FlightRequestLoggingMiddleware>();
});

// Custom Hotel User Flow Logging Middleware
app.UseWhen(context => context.Request.Path.StartsWithSegments("/api/hotels", StringComparison.OrdinalIgnoreCase), appBuilder =>
{
    appBuilder.UseMiddleware<PickNBook.Api.Middleware.HotelRequestLoggingMiddleware>();
});

// SMS OTP Logging Middleware — captures clean T1/T4 log for /api/Auth/send-login-otp
app.UseWhen(context => context.Request.Path.StartsWithSegments("/api/Auth/send-login-otp", StringComparison.OrdinalIgnoreCase), appBuilder =>
{
    appBuilder.UseMiddleware<PickNBook.Api.Middleware.SmsOtpRequestLoggingMiddleware>();
});


// ---------------- MIDDLEWARE ----------------

// Enable Swagger in all environments
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "PickNBook API v1");
    c.RoutePrefix = "swagger";
    c.ConfigObject.AdditionalItems.Add("syntaxHighlight", false);
    c.DefaultModelsExpandDepth(-1);
    c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
});

var shouldSeed = builder.Configuration.GetValue<bool>("SeedDatabase", false);
if (shouldSeed)
{
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        const int maxSeedAttempts = 3;
        for (var attempt = 1; attempt <= maxSeedAttempts; attempt++)
        {
            try
            {
                await DbSeeder.SeedAsync(dbContext);
                break;
            }
            catch (Exception) when (attempt < maxSeedAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
            }
        }
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Enable CORS (Must be before UseStaticFiles)
app.UseCors("AllowFrontend");

// 🔥 VERY IMPORTANT (Added this line only)
app.UseStaticFiles();

app.UseOutputCache();

app.UseMiddleware<RealClientIpResolverMiddleware>();
app.UseMiddleware<HealthCheckFilterMiddleware>();
app.UseMiddleware<SuperAdminEmergencyRecoveryMiddleware>();

app.UseAuthentication();

app.UseMiddleware<AccountSessionStatusMiddleware>();
app.UseMiddleware<CentralSecurityMiddleware>();

app.UseAuthorization();

app.MapControllers();

// === ONE-TIME DB SCHEMA FIX (remove after first successful run) ===
using (var scope = app.Services.CreateScope())
{
    var dbCtx = scope.ServiceProvider.GetRequiredService<PickNBook.Api.Data.AppDbContext>();
    var alterStatements = new[]
    {
        "ALTER TABLE `place_search_stats` ADD COLUMN `CityCode` varchar(50) NULL;",
        "ALTER TABLE `place_search_stats` ADD COLUMN `LastSelectedAtUtc` datetime(6) NULL;"
    };
    foreach (var sql in alterStatements)
    {
        try { dbCtx.Database.ExecuteSqlRaw(sql); }
        catch { /* column already exists - ignore */ }
    }
}

app.Run();

