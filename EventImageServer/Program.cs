using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.FileProviders;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using System.Threading.RateLimiting;
using EventImageServer.Contexts;
using EventImageServer.Filters;
using EventImageServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();

// Enable CORS so React can call the API
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
            "https://spoiled-dandy-diminish.ngrok-free.dev" // ngrok tunnel (only allowed origin)
        )
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Add controllers
// Serialize enums as camelCase strings (e.g. RsvpStatus.Confirmed -> "confirmed")
// since the React client works with lowercase status strings throughout.
// Also serialize all property names to camelCase to match JavaScript conventions.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
    });

// Configure Firebase JWT Authentication
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://securetoken.google.com/eventimage-72337";
        options.RequireHttpsMetadata = false;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "https://securetoken.google.com/eventimage-72337",
            ValidateAudience = true,
            ValidAudience = "eventimage-72337",
            ValidateLifetime = true
        };
    });

builder.Services.AddAuthorization();

// Trust forwarded client IP and scheme headers only from configured proxy networks.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();

    var knownNetworks = builder.Configuration
        .GetSection("ForwardedHeaders:KnownNetworks")
        .Get<string[]>() ?? Array.Empty<string>();

    foreach (var network in knownNetworks)
    {
        var parts = network.Split('/', 2);
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var address) ||
            !int.TryParse(parts[1], out var prefixLength))
        {
            throw new InvalidOperationException(
                $"Invalid ForwardedHeaders:KnownNetworks entry '{network}'. Expected CIDR notation.");
        }

        options.KnownIPNetworks.Add(new System.Net.IPNetwork(address, prefixLength));
    }
});

// Add DbContext (local SQLite file — no external DB server required)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=eventimage.db")
);

builder.Services.AddHttpClient<SeatingServiceClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Seating:BaseUrl"] ?? "http://localhost:8000/");
    client.Timeout = TimeSpan.FromSeconds(20);

    // Must match SEATING_API_KEY configured on the Python seating service.
    var apiKey = builder.Configuration["Seating:ApiKey"];
    if (!string.IsNullOrWhiteSpace(apiKey))
    {
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
    }
});

// Twilio (SMS/WhatsApp) messaging configuration + service
builder.Services.AddOptions<TwilioOptions>()
    .Bind(builder.Configuration.GetSection("Twilio"))
    .Validate(options =>
            Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var publicBaseUri)
            && (publicBaseUri.Scheme == Uri.UriSchemeHttp || publicBaseUri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrWhiteSpace(publicBaseUri.Host),
        "Twilio:PublicBaseUrl must be a valid absolute HTTP or HTTPS URI.")
    .ValidateOnStart();
builder.Services.AddScoped(sp =>
    new TwilioMessagingService(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TwilioOptions>>().Value));

// SMTP email configuration + service (used for collaborator invite emails).
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddScoped(sp =>
    new EmailService(
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SmtpOptions>>().Value,
        sp.GetRequiredService<ILogger<EmailService>>()));

// Shared "resolve the caller as an EventOwner" logic used by Seating/Budget/Vendors.
builder.Services.AddScoped<EventOwnerResolver>();
builder.Services.AddScoped<EventOwnerFilter>();

// Hourly background service sending automatic RSVP reminders (Phase 5.A).
builder.Services.AddHostedService<ReminderScheduler>();

// Asynchronously dispatch owner-requested RSVP invites and reminders.
builder.Services.AddSingleton<MessageDispatchQueue>();
builder.Services.AddHostedService<MessageDispatchWorker>();

// Rate limit the public, unauthenticated RSVP and Wall endpoints by their
// secret route token, rather than the shared proxy connection IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("rsvp", context =>
    {
        var controller = context.Request.RouteValues["controller"]?.ToString() ?? "unknown";
        var token = context.Request.RouteValues["token"]?.ToString() ?? "missing-token";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"{controller.ToLowerInvariant()}:{token}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

var app = builder.Build();

// Must run before routing/rate limiting so downstream code sees the original
// client IP and public HTTPS scheme supplied by a trusted reverse proxy.
app.UseForwardedHeaders();
app.UseExceptionHandler();

// Apply any pending EF Core migrations (creates the SQLite DB on first run).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    BaselineLegacyDatabase(db);
    db.Database.Migrate();
}

app.UseRouting();          // first
app.UseCors();             // then
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();


var uploadedImagesPath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedImages");
if (!Directory.Exists(uploadedImagesPath))
{
    Directory.CreateDirectory(uploadedImagesPath);
}
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadedImagesPath),
    RequestPath = "/UploadedImages"
});

app.Run();

// Databases created by the former EnsureCreated() flow already contain the full
// schema but have no __EFMigrationsHistory table, so applying the initial
// migration would fail with "table already exists". Mark the initial migration
// as applied for such databases; later migrations then apply normally.
static void BaselineLegacyDatabase(AppDbContext db)
{
    var initialMigration = db.Database.GetMigrations().FirstOrDefault();
    if (initialMigration is null)
    {
        return;
    }

    var connection = db.Database.GetDbConnection();
    connection.Open();
    try
    {
        using var check = connection.CreateCommand();
        check.CommandText =
            "SELECT " +
            "(SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory'), " +
            "(SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory')";
        using var reader = check.ExecuteReader();
        reader.Read();
        var hasLegacyTables = reader.GetInt64(0) > 0;
        var hasHistory = reader.GetInt64(1) > 0;
        reader.Close();

        if (!hasLegacyTables || hasHistory)
        {
            return;
        }

        using var baseline = connection.CreateCommand();
        baseline.CommandText =
            "CREATE TABLE \"__EFMigrationsHistory\" (" +
            "\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, " +
            "\"ProductVersion\" TEXT NOT NULL); " +
            "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ($id, $version);";
        var id = baseline.CreateParameter();
        id.ParameterName = "$id";
        id.Value = initialMigration;
        baseline.Parameters.Add(id);
        var version = baseline.CreateParameter();
        version.ParameterName = "$version";
        version.Value = typeof(DbContext).Assembly.GetName().Version?.ToString(3) ?? "10.0.0";
        baseline.Parameters.Add(version);
        baseline.ExecuteNonQuery();
    }
    finally
    {
        connection.Close();
    }
}

public partial class Program { }
