using OrToolsLab.DTOs;
using OrToolsLab.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OrToolsLab.Services;

public class PdfExportService : IExportService
{
    private readonly ExcelExportService _excelService; // Reusing data preparation

    public PdfExportService()
    {
        _excelService = new ExcelExportService();
    }

    public byte[] ExportToExcel(PlanResult plan, List<Order> allRequestedOrders, List<Vehicle> fleet, Warehouse warehouse)
    {
        throw new NotImplementedException();
    }

    public byte[] ExportToPdf(PlanResult plan, List<Order> allRequestedOrders, List<Vehicle> fleet, Warehouse warehouse)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        
        var dtos = _excelService.PrepareData(plan, allRequestedOrders, fleet, warehouse);
        var planned = dtos.Where(d => d.PlanningStatus == "Thành công").ToList();
        var unplanned = dtos.Where(d => d.PlanningStatus == "Bị rớt").ToList();

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(ComposeHeader);
                page.Content().Element(x => ComposeContent(x, planned, unplanned));
                page.Footer().Element(ComposeFooter);
            });
        });

        return document.GeneratePdf();
    }

    private void ComposeHeader(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("BÁO CÁO KẾT QUẢ LẬP KẾ HOẠCH VẬN CHUYỂN").FontSize(20).SemiBold();
                column.Item().Text($"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            });
        });
    }

    private void ComposeContent(IContainer container, List<OrderExportDto> planned, List<OrderExportDto> unplanned)
    {
        container.PaddingVertical(1, Unit.Centimetre).Column(column =>
        {
            column.Item().PaddingBottom(5).Text($"1. Đơn hàng đã lập kế hoạch ({planned.Count})").FontSize(14).SemiBold();
            if (planned.Any())
                column.Item().Element(c => ComposeTable(c, planned, false));
            else
                column.Item().Text("Không có đơn hàng nào.").Italic();

            column.Item().PaddingTop(20);
            
            column.Item().PaddingBottom(5).Text($"2. Đơn hàng bị rớt ({unplanned.Count})").FontSize(14).SemiBold();
            if (unplanned.Any())
                column.Item().Element(c => ComposeTable(c, unplanned, true));
            else
                column.Item().Text("Không có đơn hàng bị rớt.").Italic();
        });
    }

    private void ComposeTable(IContainer container, List<OrderExportDto> items, bool isDropped)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(30); // STT
                columns.RelativeColumn(2); // Mã đơn
                columns.RelativeColumn(3); // Khách hàng
                columns.RelativeColumn(3); // Địa chỉ
                columns.RelativeColumn(1); // KL
                columns.RelativeColumn(1); // TT
                
                if (isDropped)
                {
                    columns.RelativeColumn(3); // Lý do rớt
                }
                else
                {
                    columns.RelativeColumn(2); // Xe
                    columns.RelativeColumn(2); // Tài xế
                    columns.RelativeColumn(3); // Thời gian
                }
            });

            table.Header(header =>
            {
                header.Cell().Element(CellStyle).Text("STT");
                header.Cell().Element(CellStyle).Text("Mã đơn");
                header.Cell().Element(CellStyle).Text("Khách hàng");
                header.Cell().Element(CellStyle).Text("Địa chỉ");
                header.Cell().Element(CellStyle).Text("KL (kg)");
                header.Cell().Element(CellStyle).Text("TT (m3)");
                
                if (isDropped)
                {
                    header.Cell().Element(CellStyle).Text("Lý do rớt");
                }
                else
                {
                    header.Cell().Element(CellStyle).Text("Biển số xe");
                    header.Cell().Element(CellStyle).Text("Tài xế");
                    header.Cell().Element(CellStyle).Text("Giờ giao");
                }

                static IContainer CellStyle(IContainer container)
                {
                    return container.DefaultTextStyle(x => x.SemiBold()).PaddingVertical(5).BorderBottom(1).BorderColor(Colors.Black);
                }
            });

            foreach (var item in items)
            {
                table.Cell().Element(CellStyle).Text(item.Stt.ToString());
                table.Cell().Element(CellStyle).Text(item.OrderCode);
                table.Cell().Element(CellStyle).Text(item.Customer ?? "");
                table.Cell().Element(CellStyle).Text($"{item.Address}, {item.District}");
                table.Cell().Element(CellStyle).Text(item.WeightKg.ToString());
                table.Cell().Element(CellStyle).Text(item.VolumeM3.ToString());
                
                if (isDropped)
                {
                    table.Cell().Element(CellStyle).Text(item.DropReason ?? "");
                }
                else
                {
                    table.Cell().Element(CellStyle).Text(item.VehiclePlate ?? "");
                    table.Cell().Element(CellStyle).Text(item.DriverName ?? "");
                    table.Cell().Element(CellStyle).Text(item.EstimatedDeliveryTime ?? "");
                }

                static IContainer CellStyle(IContainer container)
                {
                    return container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5);
                }
            }
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.AlignCenter().Text(x =>
        {
            x.Span("Trang ");
            x.CurrentPageNumber();
            x.Span(" / ");
            x.TotalPages();
        });
    }
}
