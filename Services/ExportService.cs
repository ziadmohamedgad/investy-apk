using ClosedXML.Excel;
using Investy.Mobile.Models;

namespace Investy.Mobile.Services;

public class ExportService
{
    private readonly LocalDatabase _database;
    private readonly PortfolioService _portfolioService;

    public ExportService(LocalDatabase database, PortfolioService portfolioService)
    {
        _database = database;
        _portfolioService = portfolioService;
    }

    public async Task<string> ExportWorkbookAsync()
    {
        var assets = await _database.GetAssetsAsync();
        var transactions = await _database.GetTransactionsAsync();
        var holdings = await _portfolioService.GetHoldingsAsync();
        var dashboard = await _portfolioService.GetDashboardAsync();

        using var workbook = new XLWorkbook();
        workbook.RightToLeft = true;

        var dashboardSheet = workbook.Worksheets.Add("لوحة التحكم");
        WriteTitle(dashboardSheet, "ملخص إنڤيستي", 2);
        WriteHeaders(dashboardSheet, 3, "البند", "القيمة");
        WriteDashboardRow(dashboardSheet, 4, "إجمالي القيمة", dashboard.TotalCurrentValue, "money");
        WriteDashboardRow(dashboardSheet, 5, "إجمالي المدفوع", dashboard.TotalInvestedCapital, "money");
        WriteDashboardRow(dashboardSheet, 6, "الربح/الخسارة غير المحققة", dashboard.TotalUnrealizedPnL, "money");
        WriteDashboardRow(dashboardSheet, 7, "نسبة الربح/الخسارة غير المحققة", dashboard.TotalUnrealizedPnLPercent / 100m, "percent");
        WriteDashboardRow(dashboardSheet, 8, "الربح/الخسارة المحققة", dashboard.TotalRealizedPnL, "money");
        WriteDashboardRow(dashboardSheet, 9, "إجمالي الرسوم", dashboard.TotalFeesPaid, "money");
        WriteDashboardRow(dashboardSheet, 10, "العائد الكلي", dashboard.PortfolioReturnSinceInception / 100m, "percent");
        WriteDashboardRow(dashboardSheet, 11, "عدد الأصول", dashboard.AssetCount, "number");
        WriteDashboardRow(dashboardSheet, 12, "عدد العمليات", dashboard.TransactionCount, "number");
        StyleDataRange(dashboardSheet, 3, 12, 2);

        var assetsSheet = workbook.Worksheets.Add("الأصول");
        WriteTitle(assetsSheet, "حالة الأصول", 13);
        WriteHeaders(assetsSheet, 3, "الكود", "الاسم", "النوع", "السعر الحالي", "الوحدات", "إجمالي المدفوع", "القيمة السوقية", "الربح/الخسارة غير المحققة", "نسبة غير المحقق", "الربح/الخسارة المحققة", "إجمالي الربح/الخسارة", "نسبة إجمالية");
        for (var i = 0; i < holdings.Count; i++)
        {
            var row = i + 4;
            var item = holdings[i];
            assetsSheet.Cell(row, 1).Value = item.Asset.AssetCode;
            assetsSheet.Cell(row, 2).Value = item.Asset.AssetName;
            assetsSheet.Cell(row, 3).Value = AssetTypeLabel(item.Asset);
            assetsSheet.Cell(row, 4).Value = item.CurrentPrice;
            assetsSheet.Cell(row, 5).Value = item.TotalUnitsHeld;
            assetsSheet.Cell(row, 6).Value = item.TotalPaidIncludingFees;
            assetsSheet.Cell(row, 7).Value = item.CurrentValue;
            assetsSheet.Cell(row, 8).Value = item.UnrealizedPnL;
            assetsSheet.Cell(row, 9).Value = item.UnrealizedPnLPercent / 100m;
            assetsSheet.Cell(row, 10).Value = item.RealizedPnL;
            assetsSheet.Cell(row, 11).Value = item.TotalPnL;
            assetsSheet.Cell(row, 12).Value = item.TotalPnLPercent / 100m;
        }
        StyleDataRange(assetsSheet, 3, holdings.Count + 3, 12);
        FormatMoneyColumns(assetsSheet, 4, 6, 7, 8, 10, 11);
        FormatNumberColumns(assetsSheet, 5);
        FormatPercentColumns(assetsSheet, 9, 12);

        var transactionsSheet = workbook.Worksheets.Add("العمليات");
        WriteTitle(transactionsSheet, "سجل العمليات", 8);
        WriteHeaders(transactionsSheet, 3, "التاريخ", "الكود", "اسم الأصل", "النوع", "الوحدات", "سعر الوحدة", "الرسوم", "الصافي");
        for (var i = 0; i < transactions.Count; i++)
        {
            var row = i + 4;
            var transaction = transactions[i];
            var asset = assets.FirstOrDefault(a => a.AssetId == transaction.AssetId);
            transactionsSheet.Cell(row, 1).Value = transaction.TransactionDate;
            transactionsSheet.Cell(row, 2).Value = asset?.AssetCode ?? string.Empty;
            transactionsSheet.Cell(row, 3).Value = asset?.AssetName ?? string.Empty;
            transactionsSheet.Cell(row, 4).Value = TransactionTypeLabel(transaction, asset);
            if (transaction.TransactionType != TransactionKind.Dividend)
            {
                transactionsSheet.Cell(row, 5).Value = transaction.Quantity;
                transactionsSheet.Cell(row, 6).Value = transaction.PricePerUnit;
                transactionsSheet.Cell(row, 7).Value = transaction.Fees;
            }
            transactionsSheet.Cell(row, 8).Value = transaction.NetAmount;
        }
        StyleDataRange(transactionsSheet, 3, transactions.Count + 3, 8);
        transactionsSheet.Column(1).Style.DateFormat.Format = "yyyy/mm/dd";
        FormatNumberColumns(transactionsSheet, 5);
        FormatMoneyColumns(transactionsSheet, 6, 7, 8);

        var analysisSheet = workbook.Worksheets.Add("تحليل الأنواع");
        WriteTitle(analysisSheet, "تحليل حسب نوع الأصل", 7);
        WriteHeaders(analysisSheet, 3, "النوع", "عدد الأصول", "إجمالي المدفوع", "القيمة السوقية", "غير المحقق", "المحقق", "الوزن");
        var groupedHoldings = holdings
            .GroupBy(h => AssetTypeLabel(h.Asset))
            .OrderByDescending(g => g.Sum(h => h.CurrentValue))
            .ToList();
        for (var i = 0; i < groupedHoldings.Count; i++)
        {
            var row = i + 4;
            var group = groupedHoldings[i];
            var currentValue = group.Sum(h => h.CurrentValue);
            analysisSheet.Cell(row, 1).Value = group.Key;
            analysisSheet.Cell(row, 2).Value = group.Count();
            analysisSheet.Cell(row, 3).Value = group.Sum(h => h.TotalPaidIncludingFees);
            analysisSheet.Cell(row, 4).Value = currentValue;
            analysisSheet.Cell(row, 5).Value = group.Sum(h => h.UnrealizedPnL);
            analysisSheet.Cell(row, 6).Value = group.Sum(h => h.RealizedPnL);
            analysisSheet.Cell(row, 7).Value = dashboard.TotalCurrentValue != 0 ? currentValue / dashboard.TotalCurrentValue : 0;
        }
        StyleDataRange(analysisSheet, 3, groupedHoldings.Count + 3, 7);
        FormatMoneyColumns(analysisSheet, 3, 4, 5, 6);
        FormatPercentColumns(analysisSheet, 7);

        foreach (var sheet in workbook.Worksheets)
        {
            sheet.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            sheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            sheet.Columns().AdjustToContents();
            sheet.Rows().AdjustToContents();
        }

        var fileName = $"Investy-{DateTime.Now:yyyyMMdd-HHmm}.xlsx";
        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var bytes = stream.ToArray();

#if ANDROID
        var downloadsPath = await SaveToAndroidDownloadsAsync(fileName, bytes);
        if (!string.IsNullOrWhiteSpace(downloadsPath))
        {
            return downloadsPath;
        }
#endif

        var path = Path.Combine(FileSystem.AppDataDirectory, fileName);
        await File.WriteAllBytesAsync(path, bytes);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Export Investy data",
            File = new ShareFile(path)
        });
        return path;
    }

    private static void WriteTitle(IXLWorksheet sheet, string title, int columns)
    {
        var range = sheet.Range(1, 1, 1, columns);
        range.Merge();
        range.Value = title;
        range.Style.Font.Bold = true;
        range.Style.Font.FontSize = 16;
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f172a");
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(1).Height = 28;
    }

    private static void WriteHeaders(IXLWorksheet sheet, int row, params string[] headers)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(row, i + 1).Value = headers[i];
        }
    }

    private static void WriteDashboardRow(IXLWorksheet sheet, int row, string label, decimal value, string format)
    {
        sheet.Cell(row, 1).Value = label;
        sheet.Cell(row, 2).Value = value;
        ApplyCellFormat(sheet.Cell(row, 2), format);
    }

    private static void WriteDashboardRow(IXLWorksheet sheet, int row, string label, int value, string format)
    {
        sheet.Cell(row, 1).Value = label;
        sheet.Cell(row, 2).Value = value;
        ApplyCellFormat(sheet.Cell(row, 2), format);
    }

    private static void StyleDataRange(IXLWorksheet sheet, int headerRow, int lastRow, int lastColumn)
    {
        var header = sheet.Range(headerRow, 1, headerRow, lastColumn);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f8f6f");
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

        if (lastRow <= headerRow)
        {
            return;
        }

        var body = sheet.Range(headerRow + 1, 1, lastRow, lastColumn);
        body.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        body.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        body.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fbff");
    }

    private static void FormatMoneyColumns(IXLWorksheet sheet, params int[] columns)
    {
        foreach (var column in columns)
        {
            sheet.Column(column).Style.NumberFormat.Format = "#,##0.00";
        }
    }

    private static void FormatNumberColumns(IXLWorksheet sheet, params int[] columns)
    {
        foreach (var column in columns)
        {
            sheet.Column(column).Style.NumberFormat.Format = "#,##0.00";
        }
    }

    private static void FormatPercentColumns(IXLWorksheet sheet, params int[] columns)
    {
        foreach (var column in columns)
        {
            sheet.Column(column).Style.NumberFormat.Format = "0.00%";
        }
    }

    private static void ApplyCellFormat(IXLCell cell, string format)
    {
        cell.Style.NumberFormat.Format = format switch
        {
            "money" => "#,##0.00",
            "percent" => "0.00%",
            _ => "#,##0"
        };
    }

    private static string AssetTypeLabel(Asset asset) => asset.IsDailyAccrualFund
        ? "Cloud"
        : asset.AssetType switch
        {
            AssetType.Stock => "Stock",
            AssetType.Gold => "Gold",
            AssetType.Fund => "Fund",
            _ => "Other"
        };

    private static string TransactionTypeLabel(InvestmentTransaction transaction, Asset? asset)
    {
        var isTcd = asset?.IsDailyAccrualFund == true;
        return transaction.TransactionType switch
        {
            TransactionKind.Dividend => "أرباح",
            TransactionKind.Buy => isTcd ? "إيداع" : "شراء",
            _ => isTcd ? "سحب" : "بيع"
        };
    }

#if ANDROID
    private static async Task<string?> SaveToAndroidDownloadsAsync(string fileName, byte[] bytes)
    {
        try
        {
            if (Android.OS.Build.VERSION.SdkInt < Android.OS.BuildVersionCodes.Q)
            {
                return null;
            }

            var resolver = Android.App.Application.Context.ContentResolver;
            var values = new Android.Content.ContentValues();
            values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
            values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, Android.OS.Environment.DirectoryDownloads);

            var uri = resolver?.Insert(Android.Provider.MediaStore.Downloads.ExternalContentUri, values);
            if (uri == null || resolver == null)
            {
                return null;
            }

            await using var output = resolver.OpenOutputStream(uri);
            if (output == null)
            {
                return null;
            }

            await output.WriteAsync(bytes);
            return $"Downloads/{fileName}";
        }
        catch
        {
            return null;
        }
    }
#endif
}
