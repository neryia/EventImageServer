using EventImageServer.Models;
using Microsoft.EntityFrameworkCore;

namespace EventImageServer.Contexts
{
    public class AppDbContext : DbContext
    {
        public DbSet<Users> Clients { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<UserMedia> UserMedia { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Role>()
                .HasMany(r => r.Users)
                .WithOne(c => c.Role)
                .HasForeignKey(c => c.RoleId);

            modelBuilder.Entity<Users>()
                .HasMany(c => c.Media)
                .WithOne(m => m.User)
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

}
