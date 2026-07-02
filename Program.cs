using Microsoft.EntityFrameworkCore;
using OrToolsLab.Data;
using OrToolsLab.Models;
using OrToolsLab.Services;
using OrToolsLab.Solver;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5190");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddMemoryCache();
builder.Services.AddScoped<DraftPlanService>();
builder.Services.AddScoped<ExcelExportService>();
builder.Services.AddScoped<PdfExportService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    AppDbContextSeed.Seed(db);
}
app.UseDefaultFiles();
app.UseStaticFiles();

var planner = new TransportPlanner();

static object MapVehicle(Vehicle v) => new
{
  v.Id,
  v.Plate,
  Status = v.Status.ToString(),
  v.MaxWeightKg,
  v.MaxVolumeM3,
  v.Preference,
  Available = v.Status == VehicleStatus.Available
};

static FleetInfo FleetMeta() => new()
{
  Total = FleetStore.Count,
  Available = FleetStore.AvailableCount,
  MinSize = FleetStore.MinFleetSize,
  MaxSize = FleetStore.MaxFleetSize
};

app.MapGet("/api/fleet", () => FleetMeta());

app.MapGet("/api/vehicles", () => new
{
  Fleet = FleetMeta(),
  Items = FleetStore.All.Select(MapVehicle)
});

app.MapPut("/api/fleet/size", (FleetResizeRequest req) =>
{
  int n = FleetStore.Resize(req.Count);
  return Results.Ok(new { fleet = FleetMeta(), items = FleetStore.All.Select(MapVehicle) });
});

app.MapPut("/api/fleet/available-count", (AvailableCountRequest req) =>
{
  if (FleetStore.Count == 0)
    return Results.BadRequest("Chưa có xe trong fleet.");
  int n = FleetStore.SetAvailableCount(req.Count);
  return Results.Ok(new { fleet = FleetMeta(), availableSet = n, items = FleetStore.All.Select(MapVehicle) });
});

app.MapPut("/api/fleet/all-status", (VehicleStatusRequest req) =>
{
  if (!Enum.TryParse<VehicleStatus>(req.Status, ignoreCase: true, out var status))
    return Results.BadRequest("Status: Available | Maintenance | OnTrip");
  FleetStore.SetAllStatus(status);
  return Results.Ok(new { fleet = FleetMeta(), items = FleetStore.All.Select(MapVehicle) });
});

app.MapPut("/api/fleet/restore", (FleetRestoreRequest req) =>
{
  var statuses = new Dictionary<string, VehicleStatus>();
  if (req.Statuses != null)
  {
    foreach (var kv in req.Statuses)
    {
      if (Enum.TryParse<VehicleStatus>(kv.Value, ignoreCase: true, out var st))
        statuses[kv.Key] = st;
    }
  }
  FleetStore.Restore(req.Count, statuses);
  return Results.Ok(new { fleet = FleetMeta(), items = FleetStore.All.Select(MapVehicle) });
});

app.MapPatch("/api/vehicles/{id}/status", (string id, VehicleStatusRequest req) =>
{
  if (!Enum.TryParse<VehicleStatus>(req.Status, ignoreCase: true, out var status))
    return Results.BadRequest("Status: Available | Maintenance | OnTrip");
  if (!FleetStore.SetStatus(id, status))
    return Results.NotFound($"Không tìm thấy xe {id}.");
  return Results.Ok(new { fleet = FleetMeta(), vehicle = MapVehicle(FleetStore.All.First(v => v.Id == id)) });
});

app.MapGet("/api/orders", () => SampleData.Orders);

app.MapGet("/api/warehouse", () => SampleData.Warehouse);

// 1. Lập kế hoạch (Lưu Draft)
app.MapPost("/api/plan", async (PlanRequest req, AppDbContext db, DraftPlanService draftService) =>
{
  var warehouse = await db.Warehouses.FirstOrDefaultAsync();
  if (warehouse == null) return Results.BadRequest("Kho chưa được cấu hình.");

  var allOrders = await db.Orders.ToListAsync();
  var selected = allOrders.Where(o => req.SelectedOrderIds.Contains(o.Id)).ToList();
  var fleet = await db.Vehicles.ToListAsync();
  var availableFleet = fleet.Where(v => v.Status == VehicleStatus.Available).ToList();

  var result = await planner.PlanAsync(warehouse, availableFleet, selected, req.BalanceLevel);
  result.FleetAvailable = availableFleet.Count;
  result.FleetTotal = fleet.Count;

  // Lưu Draft
  var planId = draftService.SaveDraftPlan(result);
  
  return Results.Ok(new { PlanId = planId, Result = result });
});

app.MapPost("/api/plan/{id}/confirm", async (string id, DraftPlanService draftService, AppDbContext db) =>
{
    var draftPlan = draftService.GetDraftPlan(id);
    if (draftPlan == null) return Results.NotFound("Không tìm thấy kế hoạch hoặc đã hết hạn.");

    var plannedOrderIds = draftPlan.Stops.Select(s => s.OrderId).Distinct().ToList();
    var orders = await db.Orders.Where(o => plannedOrderIds.Contains(o.Id)).ToListAsync();
    foreach(var order in orders)
    {
        order.Status = OrderStatus.Planned;
    }

    db.PlanResults.Add(draftPlan);
    await db.SaveChangesAsync();

    draftService.RemoveDraftPlan(id);

    return Results.Ok(new { Message = "Kế hoạch đã được xác nhận và lưu vào CSDL." });
});

app.MapGet("/api/orders/export/excel", async (string planId, DraftPlanService draftService, AppDbContext db, ExcelExportService exportService) =>
{
    PlanResult? plan = draftService.GetDraftPlan(planId) ?? await db.PlanResults.Include(p => p.Stops).FirstOrDefaultAsync(p => p.Id == planId);
    if (plan == null) return Results.NotFound("Không tìm thấy kế hoạch.");

    var warehouse = await db.Warehouses.FirstAsync();
    var fleet = await db.Vehicles.ToListAsync();
    var allOrders = await db.Orders.ToListAsync();

    var requestedOrderIds = plan.Stops.Select(s => s.OrderId).ToList();
    var requestedOrders = allOrders.Where(o => requestedOrderIds.Contains(o.Id)).ToList();

    var fileContent = exportService.ExportToExcel(plan, requestedOrders, fleet, warehouse);
    return Results.File(fileContent, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"BaoCaoLichTrinh_{planId}.xlsx");
});

app.MapGet("/api/orders/export/pdf", async (string planId, DraftPlanService draftService, AppDbContext db, PdfExportService exportService) =>
{
    PlanResult? plan = draftService.GetDraftPlan(planId) ?? await db.PlanResults.Include(p => p.Stops).FirstOrDefaultAsync(p => p.Id == planId);
    if (plan == null) return Results.NotFound("Không tìm thấy kế hoạch.");

    var warehouse = await db.Warehouses.FirstAsync();
    var fleet = await db.Vehicles.ToListAsync();
    var allOrders = await db.Orders.ToListAsync();

    var requestedOrderIds = plan.Stops.Select(s => s.OrderId).ToList();
    var requestedOrders = allOrders.Where(o => requestedOrderIds.Contains(o.Id)).ToList();

    var fileContent = exportService.ExportToPdf(plan, requestedOrders, fleet, warehouse);
    return Results.File(fileContent, "application/pdf", $"BaoCaoLichTrinh_{planId}.pdf");
});

app.MapPut("/api/warehouse/load-capacity", (WarehouseLoadRequest req) =>
{
  if (req.ConcurrentLoadCapacity is < 1 or > 2)
    return Results.BadRequest("ConcurrentLoadCapacity chỉ nhận 1 hoặc 2.");
  SampleData.Warehouse.ConcurrentLoadCapacity = req.ConcurrentLoadCapacity;
  return Results.Ok(SampleData.Warehouse);
});

Console.WriteLine("OR-Tools Lab → http://localhost:5190");
app.Run();

record WarehouseLoadRequest(int ConcurrentLoadCapacity);
