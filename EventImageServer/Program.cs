using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.FileProviders;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using EventImageServer.Contexts;
using EventImageServer.Services;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
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

// Add DbContext (local SQLite file — no external DB server required)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=eventimage.db")
);

// Twilio (SMS/WhatsApp) messaging configuration + service
builder.Services.Configure<TwilioOptions>(builder.Configuration.GetSection("Twilio"));
builder.Services.AddScoped(sp =>
    new TwilioMessagingService(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TwilioOptions>>().Value));

// Rate limit the public, unauthenticated RSVP endpoints to reduce abuse/token
// guessing risk (fixed window per client IP).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("rsvp", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

// Create the local SQLite DB file and schema if they don't exist yet.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
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
