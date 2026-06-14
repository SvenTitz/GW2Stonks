using GW2Stonks.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace GW2Stonks.Data;

/// <summary>EF Core context for the local GW2 catalog, recipes and trading-post prices.</summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Item> Items => Set<Item>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<Price> Prices => Set<Price>();
    public DbSet<SyncState> SyncStates => Set<SyncState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Item>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).ValueGeneratedNever();
            e.Property(i => i.Name).HasMaxLength(200);
            e.Property(i => i.Type).HasMaxLength(50);
            e.Property(i => i.Rarity).HasMaxLength(30);
            e.Property(i => i.Flags).HasMaxLength(255);
            e.HasIndex(i => i.Type);
            e.HasIndex(i => i.Name);
        });

        modelBuilder.Entity<Recipe>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).ValueGeneratedNever();
            e.Property(r => r.Type).HasMaxLength(50);
            e.Property(r => r.Disciplines).HasMaxLength(255);
            e.Property(r => r.Flags).HasMaxLength(255);
            e.HasIndex(r => r.OutputItemId);
            e.HasOne(r => r.OutputItem)
                .WithMany(i => i.CraftedBy)
                .HasForeignKey(r => r.OutputItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecipeIngredient>(e =>
        {
            e.HasKey(ri => ri.Id);
            e.HasIndex(ri => new { ri.RecipeId, ri.ItemId }).IsUnique();
            e.HasOne(ri => ri.Recipe)
                .WithMany(r => r.Ingredients)
                .HasForeignKey(ri => ri.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ri => ri.Item)
                .WithMany()
                .HasForeignKey(ri => ri.ItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Price>(e =>
        {
            e.HasKey(p => p.ItemId);
            e.Property(p => p.ItemId).ValueGeneratedNever();
            e.HasOne(p => p.Item)
                .WithOne(i => i.Price)
                .HasForeignKey<Price>(p => p.ItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SyncState>(e =>
        {
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasMaxLength(50);
        });
    }
}
