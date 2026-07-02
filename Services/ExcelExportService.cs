using ClosedXML.Excel;
using OrToolsLab.DTOs;
using OrToolsLab.Models;

namespace OrToolsLab.Services;

public class ExcelExportService : IExportService
{
    public byte[] ExportToExcel(PlanResult plan, List<Order> allRequestedOrders, List<Vehicle> fleet, Warehouse warehouse)
    {
        var dtos = PrepareData(plan, allRequestedOrders, fleet, warehouse);
        var planned = dtos.Where(d => d.PlanningStatus == "Thành công").ToList();
        var unplanned = dtos.Where(d => d.PlanningStatus == "Bị rớt").ToList();

        using var workbook = new XLWorkbook();
        
        CreateSheet(workbook, "Đơn hàng đã lập kế hoạch", planned);
        CreateSheet(workbook, "Đơn hàng bị rớt", unplanned);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] ExportToPdf(PlanResult plan, List<Order> allRequestedOrders, List<Vehicle> fleet, Warehouse warehouse)
    {
        throw new NotImplementedException(); // Will be implemented in PdfExportService or by DI
    }

    private void CreateSheet(XLWorkbook workbook, string sheetName, List<OrderExportDto> data)
    {
        var ws = workbook.Worksheets.Add(sheetName);

        // Header Title
        ws.Cell("A1").Value = $"BÁO CÁO: {sheetName.ToUpper()}";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 16;
        ws.Range("A1:K1").Merge();

        ws.Cell("A2").Value = $"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
        ws.Cell("A3").Value = $"Tổng số đơn: {data.Count}";

        // Columns
        string[] headers = {
            "STT", "Mã đơn hàng", "Khách hàng", "Người nhận", "Số điện thoại", "Địa chỉ giao hàng",
            "Tỉnh/Thành phố", "Quận/Huyện", "Phường/Xã", "Tọa độ", "Khối lượng (kg)", "Thể tích (m3)",
            "Số kiện", "Giá trị đơn hàng", "Loại hàng", "Biển số xe", "Tài xế", "Tuyến giao",
            "Thời gian giao dự kiến", "Kho xuất phát", "Trạng thái lập kế hoạch", "Lý do rớt", "Ghi chú"
        };

        int startRow = 5;
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(startRow, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        // Freeze pane
        ws.SheetView.FreezeRows(startRow);

        int row = startRow + 1;
        foreach (var item in data)
        {
            ws.Cell(row, 1).Value = item.Stt;
            ws.Cell(row, 2).Value = item.OrderCode;
            ws.Cell(row, 3).Value = item.Customer;
            ws.Cell(row, 4).Value = item.Receiver;
            ws.Cell(row, 5).Value = item.Phone;
            ws.Cell(row, 6).Value = item.Address;
            ws.Cell(row, 7).Value = item.Province;
            ws.Cell(row, 8).Value = item.District;
            ws.Cell(row, 9).Value = item.Ward;
            ws.Cell(row, 10).Value = $"{item.Lat}, {item.Lng}";
            ws.Cell(row, 11).Value = item.WeightKg;
            ws.Cell(row, 12).Value = item.VolumeM3;
            ws.Cell(row, 13).Value = item.Packages;
            ws.Cell(row, 14).Value = item.OrderValue;
            ws.Cell(row, 15).Value = item.ItemType;
            ws.Cell(row, 16).Value = item.VehiclePlate ?? "Chưa phân công";
            ws.Cell(row, 17).Value = item.DriverName ?? "Chưa phân công";
            ws.Cell(row, 18).Value = item.Route ?? "Chưa phân công";
            ws.Cell(row, 19).Value = item.EstimatedDeliveryTime ?? "";
            ws.Cell(row, 20).Value = item.DepartureWarehouse ?? "";
            ws.Cell(row, 21).Value = item.PlanningStatus;
            ws.Cell(row, 22).Value = item.DropReason ?? "";
            ws.Cell(row, 23).Value = item.Note ?? "";
            
            row++;
        }

        ws.Range(startRow, 1, row - 1, headers.Length).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        ws.Range(startRow, 1, row - 1, headers.Length).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

        // AutoFilter
        ws.Range(startRow, 1, row - 1, headers.Length).SetAutoFilter();

        ws.Columns().AdjustToContents();
    }

    public List<OrderExportDto> PrepareData(PlanResult plan, List<Order> allRequestedOrders, List<Vehicle> fleet, Warehouse warehouse)
    {
        var result = new List<OrderExportDto>();
        int stt = 1;

        var plannedOrdersMap = plan.Stops.ToDictionary(s => s.OrderId, s => s);
        var fleetMap = fleet.ToDictionary(v => v.Id, v => v);

        foreach (var order in allRequestedOrders)
        {
            var dto = new OrderExportDto
            {
                Stt = stt++,
                OrderCode = order.OrderCode,
                Customer = order.Customer,
                Receiver = order.Receiver,
                Phone = order.Phone,
                Address = order.Address,
                Province = order.Province,
                District = order.District,
                Ward = order.Ward,
                Lat = order.Lat,
                Lng = order.Lng,
                WeightKg = order.WeightKg,
                VolumeM3 = order.VolumeM3,
                Packages = order.Packages,
                OrderValue = order.OrderValue,
                ItemType = order.ItemType,
                Note = order.Note
            };

            if (plannedOrdersMap.TryGetValue(order.Id, out var stop))
            {
                var vehicle = fleetMap.GetValueOrDefault(stop.VehicleId);
                dto.PlanningStatus = "Thành công";
                dto.VehiclePlate = stop.VehiclePlate;
                dto.DriverName = vehicle?.DriverName;
                dto.Route = $"Tuyến xe {stop.VehiclePlate}";
                
                long arrivalMin = stop.ArrivalMin;
                long departMin = stop.DepartWarehouseMin;
                
                // Giả sử mốc 0 = 00:00 của ngày hiện tại (hoặc logic time window tương đương)
                var baseTime = DateTime.Today;
                dto.EstimatedDeliveryTime = baseTime.AddMinutes(arrivalMin).ToString("dd/MM/yyyy HH:mm");
                dto.DepartureWarehouse = warehouse.Name;
            }
            else
            {
                dto.PlanningStatus = "Bị rớt";
                dto.DropReason = "Chưa phân công được do giới hạn ràng buộc";
            }

            result.Add(dto);
        }

        return result;
    }
}
