using OrToolsLab.Models;

namespace OrToolsLab.Data;

public static class SampleData
{
  private static readonly Random Rng = new(42);

  public static Warehouse Warehouse { get; } = new()
  {
    Id = "WH-01",
    Name = "Kho HCM — Quận 9",
    Lat = 10.8411,
    Lng = 106.8090,
    ConcurrentLoadCapacity = 2,
    LoadTimeMinutes = 15
  };

  public static IReadOnlyList<Vehicle> Vehicles => FleetStore.All;

  public static List<Order> Orders { get; } = GenerateOrders();

  private static List<Order> GenerateOrders()
  {
    var orders = new List<Order>();
    // 10 mã đơn × 5 đơn con = 50 đơn; cùng mã → cùng tọa độ giao (10 điểm khách)
    var orderCodes = Enumerable.Range(1, 10).Select(i => $"ORD-{i:D3}").ToList();

    int idx = 1;
    foreach (var code in orderCodes)
    {
      var (lat, lng) = RandomCoordNearHcm();
      for (int j = 0; j < 5; j++)
      {
        orders.Add(new Order
        {
          Id = $"D{idx:D3}",
          OrderCode = code,
          WeightKg = Rng.Next(80, 400),
          VolumeM3 = Rng.Next(1, 5),
          Lat = lat,
          Lng = lng
        });
        idx++;
      }
    }

    return orders;
  }

  private static (double lat, double lng) RandomCoordNearHcm()
  {
    double lat = 10.75 + Rng.NextDouble() * 0.35;
    double lng = 106.60 + Rng.NextDouble() * 0.45;
    return (Math.Round(lat, 5), Math.Round(lng, 5));
  }
}
