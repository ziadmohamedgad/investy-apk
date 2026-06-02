using Investy.Mobile.Models;

namespace Investy.Mobile.Services;

public class PortfolioService
{
    private const decimal QuantityTolerance = 0.0000001m;
    private readonly LocalDatabase _database;

    public PortfolioService(LocalDatabase database)
    {
        _database = database;
    }

    public async Task<List<AssetSummary>> GetHoldingsAsync()
    {
        var assets = await _database.GetAssetsAsync();
        var result = new List<AssetSummary>();

        foreach (var asset in assets)
        {
            var transactions = await _database.GetTransactionsByAssetAsync(asset.AssetId);
            if (transactions.Count == 0)
            {
                continue;
            }

            var currentPrice = asset.IsDailyAccrualFund
                ? GetDailyAccrualUnitPrice(asset, DateTime.UtcNow, GetDailyAccrualStartDate(asset, transactions))
                : (await _database.GetLatestPriceAsync(asset.AssetId))?.PriceValue ?? 0m;

            result.Add(CalculateAssetSummary(asset, transactions, currentPrice));
        }

        return result;
    }

    public async Task<DashboardSummary> GetDashboardAsync()
    {
        var holdings = await GetHoldingsAsync();
        var transactions = await _database.GetTransactionsAsync();
        var totalInvested = holdings.Sum(h => h.TotalCostBasis);
        var totalCurrent = holdings.Sum(h => h.CurrentValue);
        var totalUnrealized = holdings.Sum(h => h.UnrealizedPnL);
        var totalRealized = holdings.Sum(h => h.RealizedPnL);
        var totalFees = transactions.Sum(t => t.Fees);
        var totalReturn = totalUnrealized + totalRealized;

        return new DashboardSummary(
            Math.Round(totalInvested, 2),
            Math.Round(totalCurrent, 2),
            Math.Round(totalUnrealized, 2),
            totalInvested != 0 ? Math.Round(totalUnrealized / totalInvested * 100, 2) : 0,
            Math.Round(totalRealized, 2),
            Math.Round(totalFees, 2),
            totalInvested != 0 ? Math.Round(totalReturn / totalInvested * 100, 2) : 0,
            holdings.Count,
            transactions.Count);
    }

    public async Task<InvestmentTransaction> BuildTransactionAsync(
        Asset asset,
        TransactionKind kind,
        DateTime transactionDate,
        decimal quantityOrAmount,
        decimal pricePerUnit,
        decimal fees,
        decimal manufacturingFeePerGram,
        string? notes,
        int transactionId = 0)
    {
        if (quantityOrAmount <= 0)
        {
            throw new InvalidOperationException("الكمية يجب أن تكون أكبر من صفر.");
        }

        EnsureNotFutureDate(transactionDate);

        if (!asset.IsDailyAccrualFund && pricePerUnit < 0)
        {
            throw new InvalidOperationException("السعر لا يمكن أن يكون سالبًا.");
        }

        var existing = await _database.GetTransactionsByAssetAsync(asset.AssetId);
        if (transactionId != 0)
        {
            existing = existing.Where(t => t.TransactionId != transactionId).ToList();
        }

        var accrualStartDate = GetDailyAccrualStartDate(asset, existing, transactionDate);
        var normalized = NormalizeTransaction(asset, kind, transactionDate, quantityOrAmount, pricePerUnit, fees, manufacturingFeePerGram, accrualStartDate);

        var transaction = new InvestmentTransaction
        {
            TransactionId = transactionId == 0 ? int.MaxValue : transactionId,
            AssetId = asset.AssetId,
            TransactionType = kind,
            TransactionDate = transactionDate.Date,
            Quantity = normalized.Quantity,
            PricePerUnit = normalized.PricePerUnit,
            TotalAmount = normalized.TotalAmount,
            Fees = fees,
            ManufacturingFeePerGram = manufacturingFeePerGram,
            NetAmount = normalized.NetAmount,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };

        ValidateTransactionSequence(asset, existing.Append(transaction).ToList(), accrualStartDate);
        transaction.TransactionId = transactionId;
        return transaction;
    }

    public static AssetSummary CalculateAssetSummary(Asset asset, List<InvestmentTransaction> transactions, decimal currentPrice)
    {
        return asset.IsDailyAccrualFund
            ? CalculateDailyAccrualFundSummary(asset, transactions)
            : CalculateStandardAssetSummary(asset, transactions, currentPrice);
    }

    public static void ValidateTransactionSequence(Asset asset, IEnumerable<InvestmentTransaction> transactions, DateTime accrualStartDate)
    {
        decimal unitsHeld = 0;
        var hasBuy = false;

        foreach (var transaction in transactions.OrderBy(t => t.TransactionDate).ThenBy(t => t.TransactionId))
        {
            var quantity = GetEffectiveQuantity(asset, transaction, accrualStartDate);

            if (transaction.TransactionType == TransactionKind.Buy)
            {
                hasBuy = true;
                unitsHeld += quantity;
                continue;
            }

            if (!hasBuy)
            {
                throw new InvalidOperationException(asset.IsDailyAccrualFund
                    ? "لا يمكن تسجيل سحب قبل وجود إيداع سابق لهذا الأصل."
                    : "لا يمكن تسجيل بيع قبل وجود عملية شراء سابقة لهذا الأصل.");
            }

            if (quantity > unitsHeld + QuantityTolerance)
            {
                throw new InvalidOperationException(asset.IsDailyAccrualFund
                    ? $"لا يمكن سحب مبلغ يتخطى المتاح لهذا الأصل. المتاح حاليًا {unitsHeld:N5} وحدة."
                    : $"لا يمكن بيع {quantity:N5}. المتاح عند تاريخ العملية {unitsHeld:N5}.");
            }

            unitsHeld -= quantity;
        }
    }

    private static AssetSummary CalculateStandardAssetSummary(Asset asset, List<InvestmentTransaction> transactions, decimal currentPrice)
    {
        decimal unitsHeld = 0;
        decimal avgCost = 0;
        decimal realizedPnL = 0;
        decimal totalFeesPaid = 0;
        decimal totalBuyOutflow = 0;

        foreach (var transaction in transactions.OrderBy(t => t.TransactionDate).ThenBy(t => t.TransactionId))
        {
            var goldPerGramAmount = asset.AssetType == AssetType.Gold
                ? transaction.Quantity * transaction.ManufacturingFeePerGram
                : 0m;

            if (transaction.TransactionType == TransactionKind.Buy)
            {
                var previousTotal = avgCost * unitsHeld;
                var newTotal = transaction.TotalAmount + goldPerGramAmount + transaction.Fees;
                totalFeesPaid += transaction.Fees;
                totalBuyOutflow += newTotal;
                unitsHeld += transaction.Quantity;
                avgCost = unitsHeld > 0 ? (previousTotal + newTotal) / unitsHeld : 0;
            }
            else
            {
                totalFeesPaid += transaction.Fees;
                var saleProceeds = transaction.TotalAmount + goldPerGramAmount - transaction.Fees;
                realizedPnL += saleProceeds - avgCost * transaction.Quantity;
                unitsHeld -= transaction.Quantity;
            }
        }

        var costBasis = avgCost * unitsHeld;
        var currentValue = asset.AssetType == AssetType.Gold
            ? unitsHeld * (currentPrice + asset.GoldCashbackPerGram)
            : unitsHeld * currentPrice;
        var unrealizedPnL = currentValue - costBasis;

        return new AssetSummary(
            asset,
            Math.Round(unitsHeld, 5),
            Math.Round(avgCost, 5),
            Math.Round(costBasis, 2),
            Math.Round(totalFeesPaid, 2),
            Math.Round(totalBuyOutflow, 2),
            Math.Round(currentPrice, 5),
            Math.Round(currentValue, 2),
            Math.Round(unrealizedPnL, 2),
            costBasis != 0 ? Math.Round(unrealizedPnL / costBasis * 100, 2) : 0,
            Math.Round(realizedPnL, 2),
            Math.Round(unrealizedPnL + realizedPnL, 2),
            costBasis != 0 ? Math.Round((unrealizedPnL + realizedPnL) / costBasis * 100, 2) : 0);
    }

    private static AssetSummary CalculateDailyAccrualFundSummary(Asset asset, List<InvestmentTransaction> transactions)
    {
        decimal unitsHeld = 0;
        decimal avgCost = 0;
        decimal realizedPnL = 0;
        decimal totalFeesPaid = 0;
        decimal totalBuyOutflow = 0;
        var accrualStartDate = GetDailyAccrualStartDate(asset, transactions);

        foreach (var transaction in transactions.OrderBy(t => t.TransactionDate).ThenBy(t => t.TransactionId))
        {
            var unitPrice = GetDailyAccrualUnitPrice(asset, transaction.TransactionDate, accrualStartDate);
            if (unitPrice <= 0)
            {
                continue;
            }

            var units = transaction.TotalAmount / unitPrice;

            if (transaction.TransactionType == TransactionKind.Buy)
            {
                var previousTotal = avgCost * unitsHeld;
                var newTotal = transaction.TotalAmount + transaction.Fees;
                totalFeesPaid += transaction.Fees;
                totalBuyOutflow += newTotal;
                unitsHeld += units;
                avgCost = unitsHeld > 0 ? (previousTotal + newTotal) / unitsHeld : 0;
            }
            else
            {
                totalFeesPaid += transaction.Fees;
                var saleProceeds = transaction.TotalAmount - transaction.Fees;
                realizedPnL += saleProceeds - avgCost * units;
                unitsHeld -= units;
            }
        }

        var currentPrice = GetDailyAccrualUnitPrice(asset, DateTime.UtcNow, accrualStartDate);
        var costBasis = avgCost * unitsHeld;
        var currentValue = unitsHeld * currentPrice;
        var unrealizedPnL = currentValue - costBasis;

        return new AssetSummary(
            asset,
            Math.Round(unitsHeld, 5),
            Math.Round(avgCost, 5),
            Math.Round(costBasis, 2),
            Math.Round(totalFeesPaid, 2),
            Math.Round(totalBuyOutflow, 2),
            Math.Round(currentPrice, 5),
            Math.Round(currentValue, 2),
            Math.Round(unrealizedPnL, 2),
            costBasis != 0 ? Math.Round(unrealizedPnL / costBasis * 100, 2) : 0,
            Math.Round(realizedPnL, 2),
            Math.Round(unrealizedPnL + realizedPnL, 2),
            costBasis != 0 ? Math.Round((unrealizedPnL + realizedPnL) / costBasis * 100, 2) : 0);
    }

    private static (decimal Quantity, decimal PricePerUnit, decimal TotalAmount, decimal NetAmount) NormalizeTransaction(
        Asset asset,
        TransactionKind kind,
        DateTime transactionDate,
        decimal quantity,
        decimal pricePerUnit,
        decimal fees,
        decimal manufacturingFeePerGram,
        DateTime accrualStartDate)
    {
        var goldPerGramAmount = asset.AssetType == AssetType.Gold ? quantity * manufacturingFeePerGram : 0m;

        if (!asset.IsDailyAccrualFund)
        {
            var totalAmount = quantity * pricePerUnit;
            var standardNetAmount = kind == TransactionKind.Buy
                ? totalAmount + goldPerGramAmount + fees
                : totalAmount + goldPerGramAmount - fees;

            return (quantity, pricePerUnit, totalAmount, standardNetAmount);
        }

        var unitPrice = GetDailyAccrualUnitPrice(asset, transactionDate, accrualStartDate);
        if (unitPrice <= 0)
        {
            throw new InvalidOperationException("تعذر حساب سعر وحدة الصندوق.");
        }

        var amount = quantity;
        var units = amount / unitPrice;
        var accrualNetAmount = kind == TransactionKind.Buy ? amount + fees : amount - fees;
        return (units, unitPrice, amount, accrualNetAmount);
    }

    public static decimal CalculateUnitsHeld(Asset asset, IEnumerable<InvestmentTransaction> transactions, DateTime accrualStartDate)
    {
        decimal unitsHeld = 0;

        foreach (var transaction in transactions)
        {
            var quantity = GetEffectiveQuantity(asset, transaction, accrualStartDate);

            unitsHeld += transaction.TransactionType == TransactionKind.Buy ? quantity : -quantity;
        }

        return unitsHeld;
    }

    private static decimal GetEffectiveQuantity(Asset asset, InvestmentTransaction transaction, DateTime accrualStartDate)
    {
        if (!asset.IsDailyAccrualFund)
        {
            return transaction.Quantity;
        }

        var unitPrice = GetDailyAccrualUnitPrice(asset, transaction.TransactionDate, accrualStartDate);
        return unitPrice > 0 ? transaction.TotalAmount / unitPrice : 0;
    }

    private static void EnsureNotFutureDate(DateTime transactionDate)
    {
        if (transactionDate.Date > DateTime.Today)
        {
            throw new InvalidOperationException("لا يمكن تسجيل عملية بتاريخ مستقبلي.");
        }
    }

    public static DateTime GetDailyAccrualStartDate(Asset asset, IEnumerable<InvestmentTransaction> transactions, DateTime? candidateTransactionDate = null)
    {
        if (!asset.IsDailyAccrualFund)
        {
            return asset.CreatedAt.Date;
        }

        var dates = transactions.Select(t => t.TransactionDate.Date).ToList();
        if (candidateTransactionDate.HasValue)
        {
            dates.Add(candidateTransactionDate.Value.Date);
        }

        return dates.Count == 0 ? asset.CreatedAt.Date : dates.Min();
    }

    public static decimal GetDailyAccrualUnitPrice(Asset asset, DateTime asOf, DateTime? accrualStartDate = null)
    {
        var annualRate = asset.DailyAccrualAnnualRatePercent > 0 ? asset.DailyAccrualAnnualRatePercent : 16m;
        var anchorDate = (accrualStartDate ?? asset.CreatedAt).Date;
        var days = Math.Max(0d, (asOf.Date - anchorDate).TotalDays);
        var dailyGrowth = Math.Pow(1d + (double)annualRate / 100d, days / 365.25d);
        return Math.Round((decimal)dailyGrowth, 6);
    }
}
