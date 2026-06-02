using Investy.Mobile.Models;
using SQLite;

namespace Investy.Mobile.Services;

public class LocalDatabase
{
    private SQLiteAsyncConnection? _database;

    private async Task<SQLiteAsyncConnection> GetDatabaseAsync()
    {
        if (_database != null)
        {
            return _database;
        }

        var path = Path.Combine(FileSystem.AppDataDirectory, "investy.db3");
        _database = new SQLiteAsyncConnection(path, SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache);
        await _database.CreateTableAsync<Asset>();
        await _database.CreateTableAsync<InvestmentTransaction>();
        await _database.CreateTableAsync<AssetPrice>();
        await EnsureAssetSchemaAsync(_database);
        return _database;
    }

    private static async Task EnsureAssetSchemaAsync(SQLiteAsyncConnection db)
    {
        var columns = await db.QueryAsync<TableColumn>("PRAGMA table_info(Asset)");
        if (!columns.Any(c => string.Equals(c.Name, nameof(Asset.ClosedRealizedPnL), StringComparison.OrdinalIgnoreCase)))
        {
            await db.ExecuteAsync($"ALTER TABLE Asset ADD COLUMN {nameof(Asset.ClosedRealizedPnL)} TEXT NOT NULL DEFAULT '0'");
        }
    }

    public async Task<List<Asset>> GetAssetsAsync()
    {
        var db = await GetDatabaseAsync();
        return await db.Table<Asset>().OrderBy(a => a.AssetCode).ToListAsync();
    }

    public async Task<Asset?> GetAssetAsync(int assetId)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<Asset>().FirstOrDefaultAsync(a => a.AssetId == assetId);
    }

    public async Task<Asset?> GetAssetByCodeAsync(string code)
    {
        var db = await GetDatabaseAsync();
        var normalized = NormalizeCode(code);
        return await db.Table<Asset>().FirstOrDefaultAsync(a => a.AssetCode == normalized);
    }

    public async Task<List<Asset>> SearchAssetsByPrefixAsync(string query, int limit = 12)
    {
        var db = await GetDatabaseAsync();
        var normalized = NormalizeSearchCode(query);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new();
        }

        return await db.Table<Asset>()
            .Where(a => a.AssetCode.StartsWith(normalized) || a.AssetName.StartsWith(normalized))
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<Asset>> SearchAssetsByPrefixAsync(string query, AssetType assetType, bool? isDailyAccrualFund = null, int limit = 12)
    {
        var db = await GetDatabaseAsync();
        var normalized = NormalizeSearchCode(query);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new();
        }

        var assets = await db.Table<Asset>()
            .Where(a => a.AssetType == assetType && (a.AssetCode.StartsWith(normalized) || a.AssetName.StartsWith(normalized)))
            .Take(limit * 2)
            .ToListAsync();

        if (isDailyAccrualFund.HasValue)
        {
            assets = assets.Where(a => a.IsDailyAccrualFund == isDailyAccrualFund.Value).ToList();
        }

        return assets
            .OrderBy(a => a.AssetCode.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(a => a.AssetCode)
            .Take(limit)
            .ToList();
    }

    public async Task<int> SaveAssetAsync(Asset asset)
    {
        var db = await GetDatabaseAsync();
        asset.AssetCode = NormalizeCode(asset.AssetCode);
        asset.AssetName = NormalizeEnglishText(asset.AssetName, nameof(asset.AssetName));
        asset.Currency = string.IsNullOrWhiteSpace(asset.Currency) ? "EGP" : asset.Currency.Trim().ToUpperInvariant();
        asset.ExternalTicker = string.IsNullOrWhiteSpace(asset.ExternalTicker) ? null : ToAscii(asset.ExternalTicker).Trim().ToUpperInvariant();
        asset.Notes = string.IsNullOrWhiteSpace(asset.Notes) ? null : asset.Notes.Trim();
        asset.GoldCashbackPerGram = asset.AssetType == AssetType.Gold ? asset.GoldCashbackPerGram : 28.5m;
        asset.DailyAccrualAnnualRatePercent = asset.IsDailyAccrualFund && asset.DailyAccrualAnnualRatePercent > 0
            ? asset.DailyAccrualAnnualRatePercent
            : 16m;

        if (asset.AssetId == 0)
        {
            asset.CreatedAt = DateTime.UtcNow;
            return await db.InsertAsync(asset);
        }

        return await db.UpdateAsync(asset);
    }

    public async Task DeleteAssetAsync(int assetId)
    {
        var db = await GetDatabaseAsync();
        await db.Table<InvestmentTransaction>().Where(t => t.AssetId == assetId).DeleteAsync();
        await db.Table<AssetPrice>().Where(p => p.AssetId == assetId).DeleteAsync();
        await db.DeleteAsync<Asset>(assetId);
    }

    public async Task<List<InvestmentTransaction>> GetTransactionsAsync()
    {
        var db = await GetDatabaseAsync();
        return await db.Table<InvestmentTransaction>().OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.TransactionId).ToListAsync();
    }

    public async Task<InvestmentTransaction?> GetTransactionAsync(int transactionId)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<InvestmentTransaction>().FirstOrDefaultAsync(t => t.TransactionId == transactionId);
    }

    public async Task<List<InvestmentTransaction>> GetTransactionsByAssetAsync(int assetId)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<InvestmentTransaction>().Where(t => t.AssetId == assetId).OrderBy(t => t.TransactionDate).ThenBy(t => t.TransactionId).ToListAsync();
    }

    public async Task<int> SaveTransactionAsync(InvestmentTransaction transaction)
    {
        var db = await GetDatabaseAsync();
        if (transaction.TransactionId == 0)
        {
            transaction.CreatedAt = DateTime.UtcNow;
            return await db.InsertAsync(transaction);
        }

        return await db.UpdateAsync(transaction);
    }

    public async Task DeleteTransactionAsync(int transactionId)
    {
        var db = await GetDatabaseAsync();
        var transaction = await db.Table<InvestmentTransaction>().FirstOrDefaultAsync(t => t.TransactionId == transactionId);
        if (transaction == null)
        {
            return;
        }

        await db.DeleteAsync(transaction);
        var remaining = await db.Table<InvestmentTransaction>().Where(t => t.AssetId == transaction.AssetId).CountAsync();
        if (remaining == 0)
        {
            var asset = await db.Table<Asset>().FirstOrDefaultAsync(a => a.AssetId == transaction.AssetId);
            var transactionsBeforeDelete = await db.Table<InvestmentTransaction>()
                .Where(t => t.AssetId == transaction.AssetId)
                .OrderBy(t => t.TransactionDate)
                .ThenBy(t => t.TransactionId)
                .ToListAsync();

            transactionsBeforeDelete.Add(transaction);
            var latestPrice = await GetLatestPriceAsync(transaction.AssetId);
            var closingSummary = asset == null
                ? null
                : PortfolioService.CalculateAssetSummary(asset, transactionsBeforeDelete, latestPrice?.PriceValue ?? 0m);
            var realizedPnL = closingSummary?.RealizedPnL ?? 0m;

            if (Math.Abs(realizedPnL) <= 0.005m)
            {
                await DeleteAssetAsync(transaction.AssetId);
            }
            else if (asset != null)
            {
                asset.ClosedRealizedPnL = realizedPnL;
                await db.UpdateAsync(asset);
            }
        }
    }

    public async Task<List<AssetPrice>> GetPricesAsync()
    {
        var db = await GetDatabaseAsync();
        return await db.Table<AssetPrice>().OrderByDescending(p => p.PriceDate).ToListAsync();
    }

    public async Task<AssetPrice?> GetLatestPriceAsync(int assetId)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<AssetPrice>()
            .Where(p => p.AssetId == assetId)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.PriceId)
            .FirstOrDefaultAsync();
    }

    public async Task SavePriceAsync(AssetPrice price)
    {
        var db = await GetDatabaseAsync();
        var existing = await db.Table<AssetPrice>()
            .FirstOrDefaultAsync(p => p.AssetId == price.AssetId && p.PriceDate == price.PriceDate);

        if (existing == null)
        {
            price.CreatedAt = DateTime.UtcNow;
            await db.InsertAsync(price);
            return;
        }

        existing.PriceValue = price.PriceValue;
        existing.Source = price.Source;
        existing.CreatedAt = DateTime.UtcNow;
        await db.UpdateAsync(existing);
    }

    private static string NormalizeCode(string code)
    {
        var normalized = ToAssetCode(code).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Asset code must use English letters.");
        }

        return normalized;
    }

    private static string NormalizeSearchCode(string code) => ToAssetCode(code).Trim().ToUpperInvariant();

    private static string NormalizeEnglishText(string value, string fieldName)
    {
        var normalized = ToAscii(value).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{fieldName} must use English letters.");
        }

        return normalized;
    }

    private static string ToAscii(string value) => new(value.Where(c => c <= 127).ToArray());

    private static string ToAssetCode(string value) => new(value
        .Where(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
        .ToArray());

    private sealed class TableColumn
    {
        [Column("name")]
        public string Name { get; set; } = string.Empty;
    }
}
