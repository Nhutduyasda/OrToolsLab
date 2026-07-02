namespace OrToolsLab.Models;

public class PlanRequest
{
    public List<string> SelectedOrderIds { get; set; } = [];
    /// <summary>0 = ưu tiên ít km/ít xe · 100 = ưu tiên chia đều đơn.</summary>
    public int BalanceLevel { get; set; } = 80;
}
