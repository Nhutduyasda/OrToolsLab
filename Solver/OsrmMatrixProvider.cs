using System.Text.Json;

namespace OrToolsLab.Solver;

/// <summary>
/// Ma trận khoảng cách/thời gian từ OSRM (đường thực tế). Fallback Haversine nếu OSRM lỗi.
/// </summary>
public static class OsrmMatrixProvider
{
  private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
  private const string OsrmBase = "https://router.project-osrm.org";

  public static async Task<(long[,] distanceM, long[,] timeMin, bool fromOsrm)> BuildMatricesAsync(
    List<(double lat, double lng)> locations)
  {
    try
    {
      var coordStr = string.Join(";", locations.Select(l => $"{l.lng.ToString(System.Globalization.CultureInfo.InvariantCulture)},{l.lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
      var url = $"{OsrmBase}/table/v1/driving/{coordStr}?annotations=distance,duration";
      using var resp = await Http.GetAsync(url);
      resp.EnsureSuccessStatusCode();
      using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
      if (doc.RootElement.GetProperty("code").GetString() != "Ok")
        throw new InvalidOperationException("OSRM table failed");

      var distances = doc.RootElement.GetProperty("distances");
      var durations = doc.RootElement.GetProperty("durations");
      int n = locations.Count;
      var dist = new long[n, n];
      var time = new long[n, n];

      for (int i = 0; i < n; i++)
      {
        for (int j = 0; j < n; j++)
        {
          double dM = distances[i][j].GetDouble();
          double dSec = durations[i][j].GetDouble();
          dist[i, j] = i == j ? 0 : (long)Math.Round(dM);
          time[i, j] = i == j ? 0 : Math.Max(1, (long)Math.Round(dSec / 60.0));
        }
      }

      return (dist, time, true);
    }
    catch
    {
      return (BuildHaversineDistanceMatrix(locations), BuildHaversineTimeMatrix(locations), false);
    }
  }

  private static long[,] BuildHaversineDistanceMatrix(List<(double lat, double lng)> locs)
  {
    int n = locs.Count;
    var m = new long[n, n];
    for (int i = 0; i < n; i++)
      for (int j = 0; j < n; j++)
        m[i, j] = i == j ? 0 : HaversineM(locs[i], locs[j]);
    return m;
  }

  private static long[,] BuildHaversineTimeMatrix(List<(double lat, double lng)> locs)
  {
    const int speedMPerMin = 500;
    var d = BuildHaversineDistanceMatrix(locs);
    int n = locs.Count;
    var m = new long[n, n];
    for (int i = 0; i < n; i++)
      for (int j = 0; j < n; j++)
        m[i, j] = i == j ? 0 : Math.Max(1, d[i, j] / speedMPerMin);
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
