namespace OrToolsLab.Models;

public class PlanResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public bool UsedRoadNetwork { get; set; }
    public int BalanceLevel { get; set; }
    public int FleetTotal { get; set; }
    public int FleetAvailable { get; set; }
    public List<RouteStop> Stops { get; set; } = [];
    public List<DockSlot> DockSchedule { get; set; } = [];
    public List<VehicleSummary> VehicleSummaries { get; set; } = [];
    public List<OptimizationInsight> OptimizationInsights { get; set; } = [];
    public List<ValidationRow> Validations { get; set; } = [];
    public List<ConvoyCheck> ConvoyChecks { get; set; } = [];
    public long TotalDistanceM { get; set; }
    public long EstimatedNaiveDistanceM { get; set; }
    public int VehiclesUsed { get; set; }
}

public class VehicleSummary
{
    public string VehicleId { get; set; } = "";
    public string VehiclePlate { get; set; } = "";
    public int OrderCount { get; set; }
    public int StopCount { get; set; }
    public long TotalWeightKg { get; set; }
    public long TotalVolumeM3 { get; set; }
    public long RouteDistanceM { get; set; }
    public long MaxWeightKg { get; set; }
    public long MaxVolumeM3 { get; set; }
    public double WeightUtilizationPct { get; set; }
}

public class OptimizationInsight
{
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool Positive { get; set; }
}

public class DockSlot
{
    public string VehicleId { get; set; } = "";
    public string VehiclePlate { get; set; } = "";
    public long LoadStartMin { get; set; }
    public long LoadEndMin { get; set; }
    public bool Used { get; set; }
}

public class RouteStop
{
    public int Sequence { get; set; }
    public string VehicleId { get; set; } = "";
    public string VehiclePlate { get; set; } = "";
    public string OrderId { get; set; } = "";
    public string OrderCode { get; set; } = "";
    public string Action { get; set; } = "";
    public long DepartWarehouseMin { get; set; }
    public long ArrivalMin { get; set; }
    public long WeightKg { get; set; }
    public long VolumeM3 { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
}

public class ValidationRow
{
    public string Rule { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool Passed { get; set; }
}

public class ConvoyCheck
{
    public string OrderCode { get; set; } = "";
    public List<string> VehicleIds { get; set; } = [];
    public List<string> OrderIds { get; set; } = [];
    public long DepartWarehouseMin { get; set; }
    public Dictionary<string, long> ArrivalByOrderId { get; set; } = new();
    public bool SameDeparture { get; set; }
    public bool SameArrival { get; set; }
}
