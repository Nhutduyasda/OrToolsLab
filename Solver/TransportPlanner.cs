using Google.OrTools.ConstraintSolver;
using Google.Protobuf.WellKnownTypes;
using OrToolsLab.Models;

namespace OrToolsLab.Solver;

/// <summary>
/// Lập kế hoạch vận tải bằng OR-Tools Routing (VRPTW + CVRP).
/// Áp dụng pattern từ Solver Academy: Dimension Time/Capacity, hard sync convoy (Ch.12).
/// </summary>
public class TransportPlanner
{
  private const int SpeedMPerMin = 500; // ~30 km/h quy đổi
  private const int ServiceMinutes = 10;

  public PlanResult Plan(Warehouse warehouse, List<Vehicle> allVehicles, List<Order> selectedOrders, int balanceLevel = 80)
  {
    return PlanAsync(warehouse, allVehicles, selectedOrders, balanceLevel).GetAwaiter().GetResult();
  }

  public async Task<PlanResult> PlanAsync(Warehouse warehouse, List<Vehicle> allVehicles, List<Order> selectedOrders, int balanceLevel = 80)
  {
    if (selectedOrders.Count == 0)
      return Fail("Chưa chọn đơn hàng nào.");

    var fleet = allVehicles.Where(v => v.Status == VehicleStatus.Available).ToList();
    if (fleet.Count == 0)
      return Fail("Không có xe available.");

    var routingStops = BuildRoutingStops(selectedOrders, fleet);
    int numVehicles = fleet.Count;
    int numNodes = routingStops.Count + 1; // 0 = kho

    var locations = BuildLocations(warehouse, routingStops);
    var (distanceMatrix, timeMatrix, fromOsrm) = await OsrmMatrixProvider.BuildMatricesAsync(locations);

    var manager = new RoutingIndexManager(numNodes, numVehicles, 0);
    var routing = new RoutingModel(manager);

    // Arc cost = khoảng cách (mét)
    int distCallbackIdx = routing.RegisterTransitCallback((fromIdx, toIdx) =>
    {
      int from = manager.IndexToNode(fromIdx);
      int to = manager.IndexToNode(toIdx);
      return distanceMatrix[from, to];
    });
    routing.SetArcCostEvaluatorOfAllVehicles(distCallbackIdx);

    routing.AddDimension(distCallbackIdx, 0, 1_000_000_000, true, "Distance");

    // Time dimension (phút) — transit = di chuyển + service tại điểm đến
    int timeCallbackIdx = routing.RegisterTransitCallback((fromIdx, toIdx) =>
    {
      int from = manager.IndexToNode(fromIdx);
      int to = manager.IndexToNode(toIdx);
      long travel = timeMatrix[from, to];
      return travel + (to == 0 ? 0 : ServiceMinutes);
    });

    routing.AddDimension(
      timeCallbackIdx,
      120,
      24 * 60,
      false,
      "Time");
    var timeDim = routing.GetDimensionOrDie("Time");

    // Capacity — weight (kg)
    int weightDemandIdx = routing.RegisterUnaryTransitCallback(idx =>
    {
      int node = manager.IndexToNode(idx);
      return node == 0 ? 0 : routingStops[node - 1].WeightKg;
    });
    routing.AddDimensionWithVehicleCapacity(
      weightDemandIdx, 0,
      fleet.Select(v => v.MaxWeightKg).ToArray(),
      true,
      "Weight");

    // Capacity — volume (m³ × 100 để dùng long)
    int volumeDemandIdx = routing.RegisterUnaryTransitCallback(idx =>
    {
      int node = manager.IndexToNode(idx);
      return node == 0 ? 0 : routingStops[node - 1].VolumeM3 * 100;
    });
    routing.AddDimensionWithVehicleCapacity(
      volumeDemandIdx, 0,
      fleet.Select(v => v.MaxVolumeM3 * 100).ToArray(),
      true,
      "Volume");

    // Cân bằng số điểm giao giữa các xe (soft — SetGlobalSpanCostCoefficient)
    int stopCountIdx = routing.RegisterUnaryTransitCallback(idx =>
    {
      int node = manager.IndexToNode(idx);
      return node == 0 ? 0 : 1;
    });
    int maxStopsPerVehicle = Math.Max(routingStops.Count, 1);
    routing.AddDimensionWithVehicleCapacity(
      stopCountIdx, 0,
      Enumerable.Repeat((long)maxStopsPerVehicle, numVehicles).ToArray(),
      true,
      "StopCount");
    var stopCountDim = routing.GetDimensionOrDie("StopCount");
    ApplyBalanceTuning(routing, stopCountDim, balanceLevel, numVehicles, routingStops.Count);

    routing.GetDimensionOrDie("Distance").SetGlobalSpanCostCoefficient(
      (long)(5 + balanceLevel * 0.15));

  // Khung giờ kho: 6h–20h (phút từ 0h)
  int depotOpen = 6 * 60;
  int depotClose = 20 * 60;
  for (int v = 0; v < numVehicles; v++)
    timeDim.CumulVar(routing.Start(v)).SetRange(depotOpen, depotClose);

    // Convoy: cùng mã đơn trên xe khác nhau → cùng giờ xuất phát & cùng giờ giao
    ApplyConvoyConstraints(routing, manager, timeDim, routingStops, fleet);

    // Kho: lịch bốc tính sau solve (greedy) — hard dock trong solver dễ infeasible khi fleet nhỏ (6 xe / 50 đơn)

    var parameters = operations_research_constraint_solver.DefaultRoutingSearchParameters();
    parameters.FirstSolutionStrategy = FirstSolutionStrategy.Types.Value.ParallelCheapestInsertion;
    parameters.LocalSearchMetaheuristic = LocalSearchMetaheuristic.Types.Value.GuidedLocalSearch;
    parameters.TimeLimit = new Duration { Seconds = 60 };
    parameters.LogSearch = false;

    var solution = SolveWithFallback(routing, parameters);
    if (solution == null)
      return Fail("Không tìm được lịch thỏa ràng buộc (infeasible). Thử giảm số đơn hoặc tăng xe.");

    return BuildResult(routing, manager, timeDim, solution, warehouse, fleet, routingStops, selectedOrders, distanceMatrix, fromOsrm, balanceLevel);
  }

  private static void ApplyBalanceTuning(
    RoutingModel routing,
    RoutingDimension stopCountDim,
    int balanceLevel,
    int numVehicles,
    int routingStopCount)
  {
    balanceLevel = Math.Clamp(balanceLevel, 0, 100);
    double t = balanceLevel / 100.0;

    long spanCost = (long)(5_000 + t * 395_000);
    long fixedCost = (long)(50_000 - t * 47_000);
    long softPenalty = (long)(1_000 + t * 59_000);

    stopCountDim.SetGlobalSpanCostCoefficient(spanCost);

    for (int v = 0; v < numVehicles; v++)
      routing.SetFixedCostOfVehicle(fixedCost, v);

    if (balanceLevel >= 25 && routingStopCount > 0)
    {
      int targetPerVehicle = balanceLevel >= 60
        ? (int)Math.Ceiling((double)routingStopCount / Math.Min(numVehicles, routingStopCount))
        : routingStopCount;
      for (int v = 0; v < numVehicles; v++)
        stopCountDim.SetCumulVarSoftUpperBound(routing.End(v), targetPerVehicle, softPenalty);
    }
  }

  private static List<RoutingStop> BuildRoutingStops(List<Order> orders, List<Vehicle> fleet)
  {
    var result = new List<RoutingStop>();

    foreach (var grp in orders.GroupBy(o => o.OrderCode).OrderBy(g => g.Key))
    {
      var children = grp.OrderBy(o => o.Id).ToList();
      // Chia shard theo xe nhỏ nhất — mỗi node gán được cho mọi xe Available
      long capW = fleet.Min(v => v.MaxWeightKg);
      long capV = fleet.Min(v => v.MaxVolumeM3);
      var shards = SplitByCapacity(children, capW, capV);
      for (int i = 0; i < shards.Count; i++)
      {
        var shard = shards[i];
        result.Add(new RoutingStop
        {
          OrderCode = grp.Key,
          ChildOrders = shard,
          WeightKg = shard.Sum(o => o.WeightKg),
          VolumeM3 = shard.Sum(o => o.VolumeM3),
          Lat = children[0].Lat,
          Lng = children[0].Lng,
          ShardIndex = i,
          ShardCount = shards.Count
        });
      }
    }

    return result;
  }

  private static List<List<Order>> SplitByCapacity(List<Order> orders, long maxW, long maxV)
  {
    var shards = new List<List<Order>> { new List<Order>() };
    foreach (var o in orders)
    {
      var cur = shards[^1];
      long w = cur.Sum(x => x.WeightKg) + o.WeightKg;
      long v = cur.Sum(x => x.VolumeM3) + o.VolumeM3;
      if (cur.Count > 0 && (w > maxW || v > maxV))
      {
        shards.Add(new List<Order> { o });
      }
      else
      {
        cur.Add(o);
      }
    }
    return shards;
  }

  private static Assignment? SolveWithFallback(RoutingModel routing, RoutingSearchParameters baseParams)
  {
    var p = baseParams.Clone();
    var solution = routing.SolveWithParameters(p);
    if (solution != null) return solution;

    var fallbacks = new[]
    {
      FirstSolutionStrategy.Types.Value.PathCheapestArc,
      FirstSolutionStrategy.Types.Value.Savings
    };

    foreach (var strategy in fallbacks)
    {
      var fp = baseParams.Clone();
      fp.FirstSolutionStrategy = strategy;
      fp.TimeLimit = new Duration { Seconds = 25 };
      solution = routing.SolveWithParameters(fp);
      if (solution != null) return solution;
    }

    return null;
  }

  private static void ApplyConvoyConstraints(
    RoutingModel routing,
    RoutingIndexManager manager,
    RoutingDimension timeDim,
    List<RoutingStop> stops,
    List<Vehicle> fleet)
  {
    var solver = routing.solver();
    int numVehicles = fleet.Count;

    var startCumuls = new IntVar[numVehicles];
    for (int v = 0; v < numVehicles; v++)
      startCumuls[v] = timeDim.CumulVar(routing.Start(v));

    var startVector = new IntVarVector();
    foreach (var s in startCumuls)
      startVector.Add(s);

    var groups = stops
      .Select((s, i) => (Stop: s, Node: i + 1))
      .GroupBy(x => x.Stop.OrderCode)
      .Where(g => g.Count() > 1);

    foreach (var group in groups)
    {
      var nodeIndices = group.Select(x => manager.NodeToIndex(x.Node)).ToList();

      // Cùng mã đơn trên xe khác nhau → cùng thời điểm giao (hard)
      for (int a = 0; a < nodeIndices.Count; a++)
      {
        for (int b = a + 1; b < nodeIndices.Count; b++)
        {
          IntVar vehA = routing.VehicleVar(nodeIndices[a]);
          IntVar vehB = routing.VehicleVar(nodeIndices[b]);
          IntVar cumulA = timeDim.CumulVar(nodeIndices[a]);
          IntVar cumulB = timeDim.CumulVar(nodeIndices[b]);
          // (cùng xe) HOẶC (cùng giờ giao) — tránh ép cùng giờ khi đi chung 1 xe
          IntVar sameVehicle = solver.MakeIsEqualVar(vehA, vehB);
          IntVar sameArrival = solver.MakeIsEqualVar(cumulA, cumulB);
          solver.Add(solver.MakeMax(new IntVar[] { sameVehicle, sameArrival }) >= 1);
        }
      }

      // Cùng mã đơn trên xe khác nhau → cùng giờ xuất phát (Ch.12 — MakeElement theo VehicleVar)
      for (int a = 0; a < nodeIndices.Count; a++)
      {
        for (int b = a + 1; b < nodeIndices.Count; b++)
        {
          IntVar vehA = routing.VehicleVar(nodeIndices[a]);
          IntVar vehB = routing.VehicleVar(nodeIndices[b]);
          IntExpr depA = solver.MakeElement(startVector, vehA);
          IntExpr depB = solver.MakeElement(startVector, vehB);
          solver.Add(depA == depB);
        }
      }

      // Ưu tiên cùng xe nếu có thể (soft)
      if (nodeIndices.Count >= 2)
        routing.AddSoftSameVehicleConstraint(nodeIndices.ToArray(), 10_000);
    }
  }

  private static PlanResult BuildResult(
    RoutingModel routing,
    RoutingIndexManager manager,
    RoutingDimension timeDim,
    Assignment solution,
    Warehouse warehouse,
    List<Vehicle> fleet,
    List<RoutingStop> routingStops,
    List<Order> allOrders,
    long[,] distanceMatrix,
    bool usedRoadNetwork,
    int balanceLevel)
  {
    var stops = new List<RouteStop>();
    long totalDist = 0;
    int seq = 1;
    var vehicleRouteDist = new long[fleet.Count];

    for (int v = 0; v < fleet.Count; v++)
    {
      long depart = solution.Min(timeDim.CumulVar(routing.Start(v)));

      long index = routing.Start(v);
      long prevNode = 0;
      while (!routing.IsEnd(index))
      {
        index = solution.Value(routing.NextVar(index));
        long node = manager.IndexToNode(index);
        if (node == 0) continue;

        var rs = routingStops[(int)node - 1];
        long arrival = solution.Min(timeDim.CumulVar(index));
        totalDist += distanceMatrix[prevNode, node];
        vehicleRouteDist[v] += distanceMatrix[prevNode, node];
        prevNode = node;

        foreach (var order in rs.ChildOrders)
        {
          stops.Add(new RouteStop
          {
            Sequence = seq++,
            VehicleId = fleet[v].Id,
            VehiclePlate = fleet[v].Plate,
            OrderId = order.Id,
            OrderCode = order.OrderCode,
            Action = "Giao hàng",
            DepartWarehouseMin = depart,
            ArrivalMin = arrival,
            WeightKg = order.WeightKg,
            VolumeM3 = order.VolumeM3,
            Lat = order.Lat,
            Lng = order.Lng
          });
        }

      }
    }

    stops = stops.OrderBy(s => s.VehicleId).ThenBy(s => s.ArrivalMin).ToList();
    for (int i = 0; i < stops.Count; i++) stops[i].Sequence = i + 1;

    int vehiclesUsed = stops.Select(s => s.VehicleId).Distinct().Count();
    var dockSchedule = BuildDockScheduleGreedy(stops, fleet, warehouse);
    var vehicleSummaries = BuildVehicleSummaries(stops, fleet, vehicleRouteDist);
    var naiveDist = EstimateGreedyFleetDistance(routingStops, distanceMatrix, fleet.Count);
    var insights = BuildOptimizationInsights(totalDist, naiveDist, usedRoadNetwork, vehiclesUsed, stops.Count, vehicleSummaries, routingStops.Count, fleet.Count, balanceLevel);

    var validations = ValidatePlan(stops, fleet, allOrders, warehouse, dockSchedule);
    var convoyChecks = BuildConvoyChecks(stops, allOrders);

    return new PlanResult
    {
      Success = true,
      UsedRoadNetwork = usedRoadNetwork,
      BalanceLevel = balanceLevel,
      Message = $"Lập kế hoạch thành công — {vehiclesUsed} xe, {stops.Count} điểm giao." +
                (usedRoadNetwork ? " (ma trận OSRM — đường thực tế)" : " (ma trận chim bay — OSRM không khả dụng)"),
      Stops = stops,
      DockScheduleJson = System.Text.Json.JsonSerializer.Serialize(dockSchedule),
      VehicleSummariesJson = System.Text.Json.JsonSerializer.Serialize(vehicleSummaries),
      OptimizationInsightsJson = System.Text.Json.JsonSerializer.Serialize(insights),
      ValidationsJson = System.Text.Json.JsonSerializer.Serialize(validations),
      ConvoyChecksJson = System.Text.Json.JsonSerializer.Serialize(convoyChecks),
      TotalDistanceM = totalDist,
      EstimatedNaiveDistanceM = naiveDist,
      VehiclesUsed = vehiclesUsed
    };
  }

  private static List<VehicleSummary> BuildVehicleSummaries(
    List<RouteStop> stops, List<Vehicle> fleet, long[] vehicleRouteDist)
  {
    return fleet
      .Select((v, idx) =>
      {
        var vStops = stops.Where(s => s.VehicleId == v.Id).ToList();
        if (vStops.Count == 0) return null;
        var uniqueStops = vStops.Select(s => (s.Lat, s.Lng)).Distinct().Count();
        long w = vStops.Sum(s => s.WeightKg);
        return new VehicleSummary
        {
          VehicleId = v.Id,
          VehiclePlate = v.Plate,
          OrderCount = vStops.Count,
          StopCount = uniqueStops,
          TotalWeightKg = w,
          TotalVolumeM3 = vStops.Sum(s => s.VolumeM3),
          RouteDistanceM = vehicleRouteDist[idx],
          MaxWeightKg = v.MaxWeightKg,
          MaxVolumeM3 = v.MaxVolumeM3,
          WeightUtilizationPct = v.MaxWeightKg > 0 ? Math.Round(100.0 * w / v.MaxWeightKg, 1) : 0
        };
      })
      .Where(s => s != null)
      .Cast<VehicleSummary>()
      .OrderByDescending(s => s.OrderCount)
      .ToList();
  }

  private static List<OptimizationInsight> BuildOptimizationInsights(
    long optimizedDist, long naiveDist, bool fromOsrm, int vehiclesUsed, int orderCount,
    List<VehicleSummary> summaries, int routingStopCount, int fleetSize, int balanceLevel)
  {
    balanceLevel = Math.Clamp(balanceLevel, 0, 100);
    int targetPerVehicle = (int)Math.Ceiling((double)routingStopCount / Math.Max(1, Math.Min(fleetSize, routingStopCount)));
    long spanCost = (long)(5_000 + balanceLevel / 100.0 * 395_000);
    long fixedCost = (long)(50_000 - balanceLevel / 100.0 * 47_000);
    var insights = new List<OptimizationInsight>
    {
      new()
      {
        Title = "Cân bằng tải (soft)",
        Detail = $"Slider={balanceLevel}% · span={spanCost:N0} · fixedCost={fixedCost:N0}/xe · soft cap≈{targetPerVehicle} điểm/xe (khi ≥60%)",
        Positive = balanceLevel >= 50
      },
      new()
      {
        Title = "Chiến lược OR-Tools",
        Detail = "First: ParallelCheapestInsertion · Local search: GuidedLocalSearch · Time limit: 60s",
        Positive = true
      },
      new()
      {
        Title = "Hàm mục tiêu",
        Detail = "Minimize tổng km (arc cost) + phí mở xe (8.000/ xe) + span penalty cân bằng",
        Positive = true
      },
      new()
      {
        Title = "Ma trận khoảng cách",
        Detail = fromOsrm
          ? "OSRM driving — khoảng cách đường thực tế"
          : "Haversine chim bay — nên có internet để dùng OSRM",
        Positive = fromOsrm
      }
    };

    if (naiveDist > 0 && optimizedDist > 0)
    {
      long saved = naiveDist - optimizedDist;
      double pct = 100.0 * saved / naiveDist;
      insights.Add(new OptimizationInsight
      {
        Title = "So với greedy gần nhất",
        Detail = saved > 0
          ? $"OR-Tools tiết kiệm ~{saved / 1000.0:F1} km ({pct:F1}%) so với gán greedy + nearest-neighbor"
          : $"Tổng km OR-Tools: {optimizedDist / 1000.0:F1} km · Greedy ước lượng: {naiveDist / 1000.0:F1} km",
        Positive = saved > 0
      });
    }

    if (summaries.Count > 1)
    {
      double avg = summaries.Average(s => s.OrderCount);
      double maxDev = summaries.Max(s => Math.Abs(s.OrderCount - avg));
      int minOrders = summaries.Min(s => s.OrderCount);
      int maxOrders = summaries.Max(s => s.OrderCount);
      insights.Add(new OptimizationInsight
      {
        Title = "Cân bằng đơn/xe",
        Detail = $"Min {minOrders} · TB {avg:F1} · Max {maxOrders} đơn/xe · Lệch tối đa {maxDev:F0} so với TB",
        Positive = maxDev <= 2
      });
    }

    insights.Add(new OptimizationInsight
    {
      Title = "Kết quả",
      Detail = $"{vehiclesUsed} xe phục vụ {orderCount} đơn · {(orderCount > 0 ? (double)orderCount / vehiclesUsed : 0):F1} đơn/xe",
      Positive = true
    });

    return insights;
  }

  /// <summary>Greedy: chia đều stop cho xe, mỗi xe đi nearest-neighbor — baseline so sánh.</summary>
  private static long EstimateGreedyFleetDistance(
    List<RoutingStop> stops, long[,] distMatrix, int vehicleCount)
  {
    if (stops.Count == 0) return 0;
    long total = 0;
    var buckets = Enumerable.Range(0, vehicleCount).Select(_ => new List<int>()).ToList();
    for (int i = 0; i < stops.Count; i++)
      buckets[i % vehicleCount].Add(i + 1);

    foreach (var bucket in buckets.Where(b => b.Count > 0))
    {
      var remaining = new HashSet<int>(bucket);
      int current = 0;
      while (remaining.Count > 0)
      {
        int next = remaining.MinBy(n => distMatrix[current, n]);
        total += distMatrix[current, next];
        current = next;
        remaining.Remove(next);
      }
    }
    return total;
  }

  /// <summary>Gán lịch bốc kho sau solve (greedy, tôn trọng capacity).</summary>
  private static List<DockSlot> BuildDockScheduleGreedy(
    List<RouteStop> stops,
    List<Vehicle> fleet,
    Warehouse warehouse)
  {
    var departByVehicle = stops
      .GroupBy(s => s.VehicleId)
      .ToDictionary(g => g.Key, g => g.First().DepartWarehouseMin);

    var assigned = new List<(long start, long end)>();
    var slots = new List<DockSlot>();
    int L = warehouse.LoadTimeMinutes;
    int cap = warehouse.ConcurrentLoadCapacity;

    foreach (var v in fleet.Where(f => departByVehicle.ContainsKey(f.Id))
      .OrderByDescending(f => departByVehicle[f.Id]))
    {
      long depart = departByVehicle[v.Id];
      long start = depart - L;
      while (CountConcurrent(assigned, start, start + L) >= cap)
        start--;

      assigned.Add((start, start + L));
      slots.Add(new DockSlot
      {
        VehicleId = v.Id,
        VehiclePlate = v.Plate,
        LoadStartMin = start,
        LoadEndMin = start + L,
        Used = true
      });
    }

    return slots.OrderBy(s => s.LoadStartMin).ToList();
  }

  private static int CountConcurrent(List<(long start, long end)> intervals, long start, long end)
  {
    int max = 0;
    for (long t = start; t < end; t++)
    {
      int c = intervals.Count(iv => iv.start <= t && t < iv.end);
      if (c > max) max = c;
    }
    return max;
  }

  private static List<ConvoyCheck> BuildConvoyChecks(List<RouteStop> stops, List<Order> allSelected)
  {
    var delivered = stops.GroupBy(s => s.OrderCode).Where(g => g.Count() > 1).ToList();
    var checks = new List<ConvoyCheck>();

    foreach (var g in delivered)
    {
      var vehicleIds = g.Select(s => s.VehicleId).Distinct().ToList();
      if (vehicleIds.Count < 2) continue;

      var arrivals = g.ToDictionary(s => s.OrderId, s => s.ArrivalMin);
      var departs = g.Select(s => s.DepartWarehouseMin).Distinct().ToList();

      checks.Add(new ConvoyCheck
      {
        OrderCode = g.Key,
        VehicleIds = vehicleIds,
        OrderIds = g.Select(s => s.OrderId).ToList(),
        DepartWarehouseMin = departs.FirstOrDefault(),
        ArrivalByOrderId = arrivals,
        SameDeparture = departs.Count == 1,
        SameArrival = arrivals.Values.Distinct().Count() == 1
      });
    }

    return checks;
  }

  private static List<ValidationRow> ValidatePlan(
    List<RouteStop> stops,
    List<Vehicle> fleet,
    List<Order> orders,
    Warehouse warehouse,
    List<DockSlot> dockSchedule)
  {
    var rows = new List<ValidationRow>();

    // Capacity per vehicle
    foreach (var v in fleet)
    {
      var vStops = stops.Where(s => s.VehicleId == v.Id).ToList();
      if (vStops.Count == 0) continue;
      long w = vStops.Sum(s => s.WeightKg);
      long vol = vStops.Sum(s => s.VolumeM3);
      rows.Add(new ValidationRow
      {
        Rule = "Tải trọng xe",
        Detail = $"{v.Plate}: {w}/{v.MaxWeightKg} kg",
        Passed = w <= v.MaxWeightKg
      });
      rows.Add(new ValidationRow
      {
        Rule = "Thể tích xe",
        Detail = $"{v.Plate}: {vol}/{v.MaxVolumeM3} m³",
        Passed = vol <= v.MaxVolumeM3
      });
    }

    // Convoy same departure & arrival
    var byCode = stops.GroupBy(s => s.OrderCode).Where(g => g.Select(x => x.VehicleId).Distinct().Count() > 1);
    foreach (var g in byCode)
    {
      var departs = g.Select(s => s.DepartWarehouseMin).Distinct().ToList();
      var arrivals = g.Select(s => s.ArrivalMin).Distinct().ToList();
      rows.Add(new ValidationRow
      {
        Rule = "Convoy — cùng giờ xuất phát",
        Detail = $"Mã {g.Key}: {string.Join(", ", departs)} phút",
        Passed = departs.Count == 1
      });
      rows.Add(new ValidationRow
      {
        Rule = "Convoy — cùng giờ giao",
        Detail = $"Mã {g.Key}: {string.Join(", ", arrivals)} phút",
        Passed = arrivals.Count == 1
      });
    }

    // Dock capacity — kiểm tra mọi thời điểm (xe đang dùng)
    var activeSlots = dockSchedule.Where(d => d.Used).ToList();
    bool dockOk = true;
    var peakDetails = new List<string>();
    if (activeSlots.Count > 0)
    {
      long minT = activeSlots.Min(d => d.LoadStartMin);
      long maxT = activeSlots.Max(d => d.LoadEndMin);
      int peak = 0;
      for (long t = minT; t <= maxT; t++)
      {
        int concurrent = activeSlots.Count(d => d.LoadStartMin <= t && t < d.LoadEndMin);
        if (concurrent > peak) peak = concurrent;
        if (concurrent > warehouse.ConcurrentLoadCapacity)
          dockOk = false;
      }
      peakDetails.Add($"Peak bốc: {peak} xe (max {warehouse.ConcurrentLoadCapacity})");
    }
    rows.Add(new ValidationRow
    {
      Rule = "Kho — bốc đồng thời (soft)",
      Detail = string.Join(" · ", peakDetails.DefaultIfEmpty("Không có xe dùng kho")) +
               " · lịch greedy sau solve",
      Passed = dockOk
    });

    foreach (var slot in activeSlots.OrderBy(d => d.LoadStartMin))
    {
      rows.Add(new ValidationRow
      {
        Rule = "Lịch bốc kho",
        Detail = $"{slot.VehiclePlate}: bốc {slot.LoadStartMin}→{slot.LoadEndMin} phút",
        Passed = true
      });
    }

    rows.Add(new ValidationRow
    {
      Rule = "Cùng mã → cùng tọa độ",
      Detail = "Mọi đơn con cùng OrderCode có cùng Lat/Lng",
      Passed = orders.GroupBy(o => o.OrderCode).All(g =>
        g.Select(o => (o.Lat, o.Lng)).Distinct().Count() == 1)
    });

    rows.Add(new ValidationRow
    {
      Rule = "Chỉ xe Available",
      Detail = "Tất cả xe trong kế hoạch đều Available",
      Passed = stops.All(s => fleet.Any(f => f.Id == s.VehicleId && f.Status == VehicleStatus.Available))
    });

    return rows;
  }

  private static PlanResult Fail(string msg) => new() { Success = false, Message = msg };

  private static List<(double lat, double lng)> BuildLocations(Warehouse wh, List<RoutingStop> stops)
  {
    var list = new List<(double, double)> { (wh.Lat, wh.Lng) };
    list.AddRange(stops.Select(s => (s.Lat, s.Lng)));
    return list;
  }

  private static long[,] BuildDistanceMatrix(List<(double lat, double lng)> locs)
  {
    int n = locs.Count;
    var m = new long[n, n];
    for (int i = 0; i < n; i++)
      for (int j = 0; j < n; j++)
        m[i, j] = i == j ? 0 : HaversineM(locs[i], locs[j]);
    return m;
  }

  private static long[,] BuildTimeMatrix(List<(double lat, double lng)> locs)
  {
    int n = locs.Count;
    var m = new long[n, n];
    for (int i = 0; i < n; i++)
      for (int j = 0; j < n; j++)
        m[i, j] = i == j ? 0 : Math.Max(1, HaversineM(locs[i], locs[j]) / SpeedMPerMin);
    return m;
  }

  private static long HaversineM((double lat, double lng) a, (double lat, double lng) b)
  {
    const double R = 6371000;
    double dLat = ToRad(b.lat - a.lat);
    double dLng = ToRad(b.lng - a.lng);
    double x = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
               Math.Cos(ToRad(a.lat)) * Math.Cos(ToRad(b.lat)) *
               Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
    return (long)(R * 2 * Math.Atan2(Math.Sqrt(x), Math.Sqrt(1 - x)));
  }

  private static double ToRad(double deg) => deg * Math.PI / 180;
}
