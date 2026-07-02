using Microsoft.EntityFrameworkCore;
using OrToolsLab.Models;

namespace OrToolsLab.Data;

public class AppDbContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<PlanResult> PlanResults => Set<PlanResult>();
    public DbSet<RouteStop> RouteStops => Set<RouteStop>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Order>().HasKey(o => o.Id);
        modelBuilder.Entity<Vehicle>().HasKey(v => v.Id);
        modelBuilder.Entity<Warehouse>().HasKey(w => w.Id);

        modelBuilder.Entity<PlanResult>().HasKey(p => p.Id);
        
        modelBuilder.Entity<RouteStop>().HasKey(r => r.Id);
        modelBuilder.Entity<RouteStop>()
            .HasOne<PlanResult>()
            .WithMany(p => p.Stops)
            .HasForeignKey(r => r.PlanResultId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
