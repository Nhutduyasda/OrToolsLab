namespace OrToolsLab.Models;

public enum VehicleStatus
{
    Available,
    Maintenance,
    OnTrip
}

public class Vehicle
{
    public string Id { get; set; } = "";
    public string Plate { get; set; } = "";
    public VehicleStatus Status { get; set; }
    public long MaxWeightKg { get; set; }
    public long MaxVolumeM3 { get; set; }
    public string? Preference { get; set; }
}
