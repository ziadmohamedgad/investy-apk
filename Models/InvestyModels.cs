using SQLite;

namespace Investy.Mobile.Models;

public enum AssetType
{
    Stock,
    Gold,
    Fund,
    Other
}

public enum TransactionKind
{
    Buy,
    Sell
}

public enum PriceSource
{
    Manual,
    Eodhd
}

public class Asset
{
    [PrimaryKey, AutoIncrement]
    public int AssetId { get; set; }
    [Indexed(Unique = true)]
    public string AssetCode { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public AssetType AssetType { get; set; }
    public string Currency { get; set; } = "EGP";
    public string? ExternalTicker { get; set; }
    public string? Notes { get; set; }
    public bool IsDailyAccrualFund { get; set; }
    public decimal DailyAccrualAnnualRatePercent { get; set; } = 16m;
    public decimal GoldCashbackPerGram { get; set; } = 28.5m;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class InvestmentTransaction
{
    [PrimaryKey, AutoIncrement]
    public int TransactionId { get; set; }
    [Indexed]
    public int AssetId { get; set; }
    public TransactionKind TransactionType { get; set; }
    public DateTime TransactionDate { get; set; } = DateTime.Today;
    public decimal Quantity { get; set; }
    public decimal PricePerUnit { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal Fees { get; set; }
    public decimal ManufacturingFeePerGram { get; set; }
    public decimal NetAmount { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AssetPrice
{
    [PrimaryKey, AutoIncrement]
    public int PriceId { get; set; }
    [Indexed]
    public int AssetId { get; set; }
    public DateTime PriceDate { get; set; } = DateTime.Today;
    public decimal PriceValue { get; set; }
    public PriceSource Source { get; set; } = PriceSource.Manual;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public record AssetSummary(
    Asset Asset,
    decimal TotalUnitsHeld,
    decimal AverageBuyPrice,
    decimal TotalCostBasis,
    decimal TotalFeesPaid,
    decimal TotalPaidIncludingFees,
    decimal CurrentPrice,
    decimal CurrentValue,
    decimal UnrealizedPnL,
    decimal UnrealizedPnLPercent,
    decimal RealizedPnL,
    decimal RealizedPnLPercent,
    decimal TotalPnL,
    decimal TotalPnLPercent);

public record DashboardSummary(
    decimal TotalInvestedCapital,
    decimal TotalCurrentValue,
    decimal TotalUnrealizedPnL,
    decimal TotalUnrealizedPnLPercent,
    decimal TotalRealizedPnL,
    decimal TotalFeesPaid,
    decimal PortfolioReturnSinceInception,
    int AssetCount,
    int TransactionCount);

public record StockSearchResult(string Code, string Name, string Currency, string ExternalTicker);

public record EodhdUserStatus(
    string Name,
    string Email,
    string SubscriptionType,
    int ApiRequestsUsedToday,
    int DailyRateLimit,
    int ExtraLimit,
    int TotalAvailable,
    int Remaining);
