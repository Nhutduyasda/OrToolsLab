namespace OrToolsLab.Models;

public class Order
{
    public string Id { get; set; } = "";
    public string OrderCode { get; set; } = "";
    public long WeightKg { get; set; }
    public long VolumeM3 { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
}
