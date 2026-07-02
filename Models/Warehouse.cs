namespace OrToolsLab.Models;

public class Warehouse
{
    public string Id { get; set; } = "WH-01";
    public string Name { get; set; } = "Kho trung tâm";
    public double Lat { get; set; }
    public double Lng { get; set; }
    /// <summary>Số xe có thể bốc hàng đồng thời (1 hoặc 2).</summary>
    public int ConcurrentLoadCapacity { get; set; } = 2;
    /// <summary>Thời gian bốc hàng mỗi xe (phút).</summary>
    public int LoadTimeMinutes { get; set; } = 15;
}
