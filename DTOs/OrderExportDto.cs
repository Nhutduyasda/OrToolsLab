namespace OrToolsLab.DTOs;

public class OrderExportDto
{
    public int Stt { get; set; }
    public string OrderCode { get; set; } = "";
    public string? Customer { get; set; }
    public string? Receiver { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Province { get; set; }
    public string? District { get; set; }
    public string? Ward { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public long WeightKg { get; set; }
    public long VolumeM3 { get; set; }
    public int Packages { get; set; }
    public decimal OrderValue { get; set; }
    public string? ItemType { get; set; }
    public string? VehiclePlate { get; set; }
    public string? DriverName { get; set; }
    public string? Route { get; set; }
    public string? EstimatedDeliveryTime { get; set; }
    public string? DepartureWarehouse { get; set; }
    public string? PlanningStatus { get; set; }
    public string? DropReason { get; set; }
    public string? Note { get; set; }
}
