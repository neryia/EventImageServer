using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.FileProviders;
using Microsoft.EntityFrameworkCore;
using EventImageServer.Contexts;

var builder = WebApplication.CreateBuilder(args);

// Enable CORS so React can call the API
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000") // React dev server
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Add controllers
builder.Services.AddControllers();

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

var app = builder.Build();

// Create the local SQLite DB file and schema if they don't exist yet.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseRouting();          // first
app.UseCors();             // then
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
