using OrToolsLab.DTOs;
using OrToolsLab.Models;

namespace OrToolsLab.Services;

public interface IExportService
{
    byte[] ExportToExcel(PlanResult plan, List<Order> allRequestedOrders, List<Vehicle> fleet, Warehouse warehouse);
    byte[] ExportToPdf(PlanResult plan, List<Order> allRequestedOrders, List<Vehicle> fleet, Warehouse warehouse);
}
