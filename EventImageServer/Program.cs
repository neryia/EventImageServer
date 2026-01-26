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

// Add DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        new MySqlServerVersion(new Version(8, 0, 33)) // Your MySQL version
    )
);

var app = builder.Build();

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
