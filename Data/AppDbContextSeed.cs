using OrToolsLab.Models;

namespace OrToolsLab.Data;

public static class AppDbContextSeed
{
    public static void Seed(AppDbContext context)
    {
        if (!context.Warehouses.Any())
        {
            context.Warehouses.Add(SampleData.Warehouse);
        }

        if (!context.Vehicles.Any())
        {
            context.Vehicles.AddRange(SampleData.Vehicles);
        }

        if (!context.Orders.Any())
        {
            context.Orders.AddRange(SampleData.Orders);
        }

        context.SaveChanges();
    }
}
