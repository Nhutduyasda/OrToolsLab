using OrToolsLab.Models;

namespace OrToolsLab.Data;

/// <summary>
/// Kho xe động — thay đổi số lượng (6→20→…) và status mà không sửa solver.
/// </summary>
public static class FleetStore
{
  public const int MinFleetSize = 1;
  public const int MaxFleetSize = 50;

  private static readonly object Lock = new();
  private static readonly List<Vehicle> Vehicles = [];

  private static readonly string[] Preferences = ["Nội thành", "Đường dài", "Hàng nặng", null!];
  private static readonly (long kg, long m3)[] CapacityTemplates =
  [
    (5000, 30), (5000, 30), (8000, 32), (5000, 30), (5000, 30),
    (6000, 30), (5000, 30), (5000, 30), (7000, 32), (5000, 30)
  ];

  static FleetStore() => Reset(10);

  public static IReadOnlyList<Vehicle> All
  {
    get { lock (Lock) return Vehicles.ToList(); }
  }

  public static int Count
  {
    get { lock (Lock) return Vehicles.Count; }
  }

  public static int AvailableCount
  {
    get { lock (Lock) return Vehicles.Count(v => v.Status == VehicleStatus.Available); }
  }

  public static void Reset(int size = 10)
  {
    lock (Lock)
    {
      Vehicles.Clear();
      for (int i = 1; i <= Math.Clamp(size, MinFleetSize, MaxFleetSize); i++)
        Vehicles.Add(CreateVehicle(i, VehicleStatus.Available));
    }
  }

  public static int Resize(int targetCount)
  {
    targetCount = Math.Clamp(targetCount, MinFleetSize, MaxFleetSize);
    lock (Lock)
    {
      while (Vehicles.Count < targetCount)
        Vehicles.Add(CreateVehicle(Vehicles.Count + 1, VehicleStatus.Available));
      while (Vehicles.Count > targetCount)
        Vehicles.RemoveAt(Vehicles.Count - 1);
      return Vehicles.Count;
    }
  }

  public static bool SetStatus(string vehicleId, VehicleStatus status)
  {
    lock (Lock)
    {
      var v = Vehicles.FirstOrDefault(x => x.Id == vehicleId);
      if (v == null) return false;
      v.Status = status;
      return true;
    }
  }

  /// <summary>Đặt đúng N xe Available (theo thứ tự V01…), còn lại Maintenance.</summary>
  public static int SetAvailableCount(int availableCount)
  {
    lock (Lock)
    {
      availableCount = Math.Clamp(availableCount, 0, Vehicles.Count);
      for (int i = 0; i < Vehicles.Count; i++)
      {
        Vehicles[i].Status = i < availableCount
          ? VehicleStatus.Available
          : VehicleStatus.Maintenance;
      }
      return availableCount;
    }
  }

  public static void SetAllStatus(VehicleStatus status)
  {
    lock (Lock)
    {
      foreach (var v in Vehicles)
        v.Status = status;
    }
  }

  /// <summary>Khôi phục kích thước fleet + status từng xe (persist UI).</summary>
  public static void Restore(int targetCount, IReadOnlyDictionary<string, VehicleStatus>? statuses = null)
  {
    Resize(targetCount);
    if (statuses == null || statuses.Count == 0) return;
    lock (Lock)
    {
      foreach (var v in Vehicles)
      {
        if (statuses.TryGetValue(v.Id, out var st))
          v.Status = st;
      }
    }
  }

  private static Vehicle CreateVehicle(int index, VehicleStatus status)
  {
    var tpl = CapacityTemplates[(index - 1) % CapacityTemplates.Length];
    var pref = Preferences[(index - 1) % Preferences.Length];
    int plateNum = 12345 + index * 1111;
    return new Vehicle
    {
      Id = $"V{index:D2}",
      Plate = $"51C-{plateNum}",
      Status = status,
      MaxWeightKg = tpl.kg,
      MaxVolumeM3 = tpl.m3,
      Preference = pref
    };
  }
}
