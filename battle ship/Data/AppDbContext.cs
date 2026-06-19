using battle_ship.Models;
using Microsoft.EntityFrameworkCore;

namespace battle_ship.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Models.Game> Games => Set<Models.Game>();
    public DbSet<GamePlayer> GamePlayers => Set<GamePlayer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Username).IsUnique();
            entity.Property(u => u.Username).HasMaxLength(50);
        });

        modelBuilder.Entity<GamePlayer>(entity =>
        {
            entity.HasOne(gp => gp.Game)
                .WithMany(g => g.Players)
                .HasForeignKey(gp => gp.GameId);

            entity.HasOne(gp => gp.User)
                .WithMany(u => u.GamePlayers)
                .HasForeignKey(gp => gp.UserId);

            entity.HasIndex(gp => new { gp.GameId, gp.UserId }).IsUnique();
        });
    }
}
