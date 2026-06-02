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
        dashboardSheet.Cell(1, 1).Value = "البند";
        dashboardSheet.Cell(1, 2).Value = "القيمة";
        dashboardSheet.Cell(2, 1).Value = "القيمة السوقية";
        dashboardSheet.Cell(2, 2).Value = dashboard.TotalCurrentValue;
        dashboardSheet.Cell(3, 1).Value = "رأس المال المستثمر";
        dashboardSheet.Cell(3, 2).Value = dashboard.TotalInvestedCapital;
        dashboardSheet.Cell(4, 1).Value = "الربح غير المحقق";
        dashboardSheet.Cell(4, 2).Value = dashboard.TotalUnrealizedPnL;
        dashboardSheet.Cell(5, 1).Value = "نسبة الربح غير المحقق";
        dashboardSheet.Cell(5, 2).Value = dashboard.TotalUnrealizedPnLPercent / 100m;
        dashboardSheet.Cell(5, 2).Style.NumberFormat.Format = "0.00%";
        dashboardSheet.Cell(6, 1).Value = "الربح المحقق";
        dashboardSheet.Cell(6, 2).Value = dashboard.TotalRealizedPnL;
        dashboardSheet.Cell(7, 1).Value = "إجمالي الرسوم";
        dashboardSheet.Cell(7, 2).Value = dashboard.TotalFeesPaid;
        dashboardSheet.Cell(8, 1).Value = "العائد الكلي";
        dashboardSheet.Cell(8, 2).Value = dashboard.PortfolioReturnSinceInception / 100m;
        dashboardSheet.Cell(8, 2).Style.NumberFormat.Format = "0.00%";
        dashboardSheet.Cell(9, 1).Value = "عدد الأصول";
        dashboardSheet.Cell(9, 2).Value = dashboard.AssetCount;
        dashboardSheet.Cell(10, 1).Value = "عدد العمليات";
        dashboardSheet.Cell(10, 2).Value = dashboard.TransactionCount;

        var assetsSheet = workbook.Worksheets.Add("الأصول");
        assetsSheet.Cell(1, 1).Value = "الكود";
        assetsSheet.Cell(1, 2).Value = "الاسم";
        assetsSheet.Cell(1, 3).Value = "النوع";
        assetsSheet.Cell(1, 4).Value = "العملة";
        assetsSheet.Cell(1, 5).Value = "السعر الحالي";
        assetsSheet.Cell(1, 6).Value = "الكمية";
        assetsSheet.Cell(1, 7).Value = "القيمة السوقية";
        assetsSheet.Cell(1, 8).Value = "الربح/الخسارة غير المحققة";
        assetsSheet.Cell(1, 9).Value = "الربح/الخسارة المحققة";
        assetsSheet.Cell(1, 10).Value = "نسبة الربح/الخسارة المحققة";
        for (var i = 0; i < holdings.Count; i++)
        {
            var row = i + 2;
            var item = holdings[i];
            assetsSheet.Cell(row, 1).Value = item.Asset.AssetCode;
            assetsSheet.Cell(row, 2).Value = item.Asset.AssetName;
            assetsSheet.Cell(row, 3).Value = item.Asset.AssetType.ToString();
            assetsSheet.Cell(row, 4).Value = item.Asset.Currency;
            assetsSheet.Cell(row, 5).Value = item.CurrentPrice;
            assetsSheet.Cell(row, 6).Value = item.TotalUnitsHeld;
            assetsSheet.Cell(row, 7).Value = item.CurrentValue;
            assetsSheet.Cell(row, 8).Value = item.UnrealizedPnL;
            assetsSheet.Cell(row, 9).Value = item.RealizedPnL;
            assetsSheet.Cell(row, 10).Value = item.RealizedPnLPercent;
        }

        var transactionsSheet = workbook.Worksheets.Add("العمليات");
        transactionsSheet.Cell(1, 1).Value = "التاريخ";
        transactionsSheet.Cell(1, 2).Value = "الكود";
        transactionsSheet.Cell(1, 3).Value = "النوع";
        transactionsSheet.Cell(1, 4).Value = "الكمية";
        transactionsSheet.Cell(1, 5).Value = "سعر الوحدة";
        transactionsSheet.Cell(1, 6).Value = "الرسوم";
        transactionsSheet.Cell(1, 7).Value = "الإجمالي";
        for (var i = 0; i < transactions.Count; i++)
        {
            var row = i + 2;
            var transaction = transactions[i];
            var asset = assets.FirstOrDefault(a => a.AssetId == transaction.AssetId);
            transactionsSheet.Cell(row, 1).Value = transaction.TransactionDate;
            transactionsSheet.Cell(row, 2).Value = asset?.AssetCode ?? string.Empty;
            transactionsSheet.Cell(row, 3).Value = transaction.TransactionType == TransactionKind.Buy ? "شراء" : "بيع";
            transactionsSheet.Cell(row, 4).Value = transaction.Quantity;
            transactionsSheet.Cell(row, 5).Value = transaction.PricePerUnit;
            transactionsSheet.Cell(row, 6).Value = transaction.Fees;
            transactionsSheet.Cell(row, 7).Value = transaction.NetAmount;
        }

        foreach (var sheet in workbook.Worksheets)
        {
            sheet.Row(1).Style.Font.Bold = true;
            sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#0f8f6f");
            sheet.Row(1).Style.Font.FontColor = XLColor.White;
            sheet.Columns().AdjustToContents();
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
