namespace OrToolsLab.Models;

public class FleetResizeRequest
{
  public int Count { get; set; }
}

public class VehicleStatusRequest
{
  public string Status { get; set; } = "Available";
}

public class AvailableCountRequest
{
  public int Count { get; set; }
}

public class FleetInfo
{
  public int Total { get; set; }
  public int Available { get; set; }
  public int MinSize { get; set; }
  public int MaxSize { get; set; }
}

public class FleetRestoreRequest
{
  public int Count { get; set; }
  public Dictionary<string, string>? Statuses { get; set; }
}
