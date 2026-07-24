using EventImageServer.Models;
using Microsoft.EntityFrameworkCore;

namespace EventImageServer.Contexts
{
    public class AppDbContext : DbContext
    {
        public DbSet<Users> Clients { get; set; }
        public DbSet<UserMedia> UserMedia { get; set; }
        public DbSet<Table> Tables { get; set; }
        public DbSet<Guest> Guests { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Users>()
                .HasMany(c => c.Media)
                .WithOne(m => m.User)
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Users>()
                .HasMany<Table>()
                .WithOne(t => t.Owner)
                .HasForeignKey(t => t.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Users>()
                .HasMany<Guest>()
                .WithOne(g => g.Owner)
                .HasForeignKey(g => g.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Table>()
                .HasMany(t => t.Guests)
                .WithOne(g => g.Table)
                .HasForeignKey(g => g.TableId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }

}
