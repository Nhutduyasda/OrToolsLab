using OrToolsLab.Models;

namespace OrToolsLab.Solver;

/// <summary>
/// Nhóm đơn con cùng mã (cùng tọa độ). Có thể tách shard nếu vượt tải 1 xe.
/// </summary>
internal sealed class RoutingStop
{
  public string OrderCode { get; init; } = "";
  public List<Order> ChildOrders { get; init; } = [];
  public long WeightKg { get; init; }
  public long VolumeM3 { get; init; }
  public double Lat { get; init; }
  public double Lng { get; init; }
  public int ShardIndex { get; init; }
  public int ShardCount { get; init; }
}
