using OrToolsLab.Data;
using OrToolsLab.Models;
using OrToolsLab.Solver;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5190");

var app = builder.Build();
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

app.MapPost("/api/plan", async (PlanRequest req) =>
{
  var selected = SampleData.Orders
    .Where(o => req.SelectedOrderIds.Contains(o.Id))
    .ToList();

  var fleet = FleetStore.All.ToList();
  var result = await planner.PlanAsync(SampleData.Warehouse, fleet, selected, req.BalanceLevel);
  result.FleetAvailable = FleetStore.AvailableCount;
  result.FleetTotal = FleetStore.Count;
  return Results.Ok(result);
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
