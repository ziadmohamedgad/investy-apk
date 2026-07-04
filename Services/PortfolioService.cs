using Investy.Mobile.Models;

namespace Investy.Mobile.Services;

public class PortfolioService
{
    private const decimal QuantityTolerance = 0.0000001m;
    private const decimal ClosedPositionQuantityTolerance = 0.005m;
    private readonly LocalDatabase _database;

    /// <summary>Egypt does not currently observe DST; UTC+3 is always correct.</summary>
    private static readonly TimeSpan EgyptOffset = TimeSpan.FromHours(3);

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
            if (transactions.Count == 0 && Math.Abs(asset.ClosedRealizedPnL) <= 0.005m)
            {
                continue;
            }

            var currentPrice = asset.IsDailyAccrualFund
                ? GetDailyAccrualUnitPrice(asset, DateTime.UtcNow, GetDailyAccrualStartDate(asset, transactions))
                : (await _database.GetLatestPriceAsync(asset.AssetId))?.PriceValue ?? 0m;

            var summary = CalculateAssetSummary(asset, transactions, currentPrice);
            if (summary.IsClosedPosition && Math.Abs(summary.RealizedPnL + summary.UnrealizedPnL) <= 0.005m)
            {
                continue;
            }

            result.Add(summary);
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
        int transactionId = 0,
        DividendKind dividendKind = DividendKind.Cash)
    {
        if (quantityOrAmount <= 0)
        {
            throw new InvalidOperationException(kind == TransactionKind.Dividend ? "الربح يجب أن يكون أكبر من صفر." : "الوحدات يجب أن تكون أكبر من صفر.");
        }

        EnsureNotFutureDate(transactionDate);

        if (kind == TransactionKind.Dividend && asset.AssetType != AssetType.Stock)
        {
            throw new InvalidOperationException("أرباح الأسهم متاحة للأصول من نوع سهم فقط.");
        }

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
        var normalized = NormalizeTransaction(asset, kind, transactionDate, quantityOrAmount, pricePerUnit, fees, manufacturingFeePerGram, accrualStartDate, dividendKind);
        await ValidateSellAgainstCurrentMarketValueAsync(asset, existing, kind, normalized.NetAmount);

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
            DividendKind = dividendKind,
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

    private async Task ValidateSellAgainstCurrentMarketValueAsync(
        Asset asset,
        List<InvestmentTransaction> existingTransactions,
        TransactionKind kind,
        decimal netAmount)
    {
        if (kind != TransactionKind.Sell || netAmount <= 0)
        {
            return;
        }

        var currentPrice = asset.IsDailyAccrualFund
            ? GetDailyAccrualUnitPrice(asset, DateTime.Today, GetDailyAccrualStartDate(asset, existingTransactions))
            : (await _database.GetLatestPriceAsync(asset.AssetId))?.PriceValue ?? 0m;
        var currentSummary = CalculateAssetSummary(asset, existingTransactions, currentPrice);
        if (netAmount > currentSummary.CurrentValue + 0.01m)
        {
            throw new InvalidOperationException(asset.IsDailyAccrualFund
                ? "صافي السحب بعد الرسوم لا يمكن أن يكون أكبر من القيمة السوقية للأصل"
                : "صافي البيع بعد الرسوم لا يمكن أن يكون أكبر من القيمة السوقية للأصل");
        }
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

            if (transaction.TransactionType == TransactionKind.Dividend)
            {
                if (!hasBuy)
                {
                    throw new InvalidOperationException("لا يمكن تسجيل أرباح قبل وجود عملية شراء سابقة للسهم.");
                }

                // Stock dividends add free shares to units held
                if (transaction.DividendKind == DividendKind.Stock)
                {
                    unitsHeld += transaction.Quantity;
                }

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
                    ? "لا يمكن إتمام هذه العملية لأن مبلغ السحب يتجاوز المبلغ المتاح من هذا الأصل."
                    : "لا يمكن إتمام هذه العملية لأن الوحدات المباعة تتجاوز الوحدات المتاحة من هذا الأصل.");
            }

            unitsHeld -= quantity;
        }
    }

    private static AssetSummary CalculateStandardAssetSummary(Asset asset, List<InvestmentTransaction> transactions, decimal currentPrice)
    {
        var isPreciousMetal = asset.AssetType == AssetType.Gold || asset.AssetType == AssetType.Silver;
        decimal unitsHeld = 0;
        decimal avgCost = 0;
        decimal realizedPnL = asset.ClosedRealizedPnL;
        decimal realizedCostBasis = 0;
        decimal totalFeesPaid = 0;

        foreach (var transaction in transactions.OrderBy(t => t.TransactionDate).ThenBy(t => t.TransactionId))
        {
            var metalPerGramAmount = isPreciousMetal
                ? transaction.Quantity * transaction.ManufacturingFeePerGram
                : 0m;

            if (transaction.TransactionType == TransactionKind.Buy)
            {
                var previousTotal = avgCost * unitsHeld;
                var newTotal = transaction.TotalAmount + metalPerGramAmount + transaction.Fees;
                totalFeesPaid += transaction.Fees;
                unitsHeld += transaction.Quantity;
                avgCost = unitsHeld > 0 ? (previousTotal + newTotal) / unitsHeld : 0;
            }
            else if (transaction.TransactionType == TransactionKind.Sell)
            {
                totalFeesPaid += transaction.Fees;
                var saleProceeds = transaction.TotalAmount + metalPerGramAmount - transaction.Fees;
                var soldCostBasis = avgCost * transaction.Quantity;
                realizedCostBasis += soldCostBasis;
                realizedPnL += saleProceeds - soldCostBasis;
                unitsHeld -= transaction.Quantity;
            }
            else // Dividend
            {
                if (transaction.DividendKind == DividendKind.Stock)
                {
                    var previousTotal = avgCost * unitsHeld;
                    unitsHeld += transaction.Quantity;
                    avgCost = unitsHeld > 0 ? (previousTotal + transaction.NetAmount) / unitsHeld : 0;
                }
                else
                {
                    // Cash dividend: goes directly to realized P&L
                    realizedPnL += transaction.NetAmount;
                }
            }
        }

        unitsHeld = Math.Abs(unitsHeld) <= QuantityTolerance ? 0m : unitsHeld;
        var costBasis = avgCost * unitsHeld;
        var remainingAverageCost = unitsHeld > QuantityTolerance ? avgCost : 0m;
        var isClosedPosition = Math.Abs(unitsHeld) < ClosedPositionQuantityTolerance;
        var currentValue = isPreciousMetal
            ? unitsHeld * (currentPrice + asset.GoldCashbackPerGram)
            : unitsHeld * currentPrice;
        var unrealizedPnL = currentValue - costBasis;

        return new AssetSummary(
            asset,
            isClosedPosition,
            Math.Round(unitsHeld, 5),
            Math.Round(remainingAverageCost, 5),
            Math.Round(costBasis, 2),
            Math.Round(totalFeesPaid, 2),
            Math.Round(costBasis, 2),
            Math.Round(currentPrice, 5),
            Math.Round(currentValue, 2),
            Math.Round(unrealizedPnL, 2),
            costBasis != 0 ? Math.Round(unrealizedPnL / costBasis * 100, 2) : 0,
            Math.Round(realizedPnL, 2),
            realizedCostBasis != 0 ? Math.Round(realizedPnL / realizedCostBasis * 100, 2) : 0,
            Math.Round(unrealizedPnL + realizedPnL, 2),
            costBasis != 0 ? Math.Round((unrealizedPnL + realizedPnL) / costBasis * 100, 2) : 0,
            0m); // TotalAccruedReturn is only meaningful for TCD
    }

    private static AssetSummary CalculateDailyAccrualFundSummary(Asset asset, List<InvestmentTransaction> transactions)
    {
        decimal unitsHeld = 0;
        decimal avgCost = 0;
        decimal totalDepositedNet = 0; // sum of all buy netAmounts (deposits including fees)
        decimal totalWithdrawnNet = 0; // sum of all sell netAmounts (proceeds after fees)
        decimal realizedPnL = asset.ClosedRealizedPnL;
        decimal totalFeesPaid = 0;
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
                totalDepositedNet += transaction.NetAmount;
                unitsHeld += units;
                avgCost = unitsHeld > 0 ? (previousTotal + newTotal) / unitsHeld : 0;
            }
            else if (transaction.TransactionType == TransactionKind.Sell)
            {
                totalFeesPaid += transaction.Fees;
                var saleProceeds = transaction.TotalAmount - transaction.Fees;
                var soldCostBasis = avgCost * units;
                totalWithdrawnNet += transaction.NetAmount;
                realizedPnL += saleProceeds - soldCostBasis;
                unitsHeld -= units;
            }
        }

        unitsHeld = Math.Abs(unitsHeld) <= QuantityTolerance ? 0m : unitsHeld;
        var currentPrice = GetDailyAccrualUnitPrice(asset, DateTime.UtcNow, accrualStartDate);
        var costBasis = avgCost * unitsHeld;
        var remainingAverageCost = unitsHeld > QuantityTolerance ? avgCost : 0m;
        var isClosedPosition = Math.Abs(unitsHeld) < ClosedPositionQuantityTolerance;
        var currentValue = unitsHeld * currentPrice;
        var unrealizedPnL = currentValue - costBasis;

        // TotalAccruedReturn = total growth regardless of withdrawals
        // = (current balance + all withdrawn proceeds) - all deposited amounts
        var totalAccruedReturn = Math.Round((currentValue + totalWithdrawnNet) - totalDepositedNet, 2);

        return new AssetSummary(
            asset,
            isClosedPosition,
            Math.Round(unitsHeld, 5),
            Math.Round(remainingAverageCost, 5),
            Math.Round(costBasis, 2),
            Math.Round(totalFeesPaid, 2),
            Math.Round(costBasis, 2),
            Math.Round(currentPrice, 5),
            Math.Round(currentValue, 2),
            Math.Round(unrealizedPnL, 2),
            costBasis != 0 ? Math.Round(unrealizedPnL / costBasis * 100, 2) : 0,
            Math.Round(realizedPnL, 2),
            0m,
            Math.Round(unrealizedPnL + realizedPnL, 2),
            costBasis != 0 ? Math.Round((unrealizedPnL + realizedPnL) / costBasis * 100, 2) : 0,
            totalAccruedReturn);
    }

    private static (decimal Quantity, decimal PricePerUnit, decimal TotalAmount, decimal NetAmount) NormalizeTransaction(
        Asset asset,
        TransactionKind kind,
        DateTime transactionDate,
        decimal quantity,
        decimal pricePerUnit,
        decimal fees,
        decimal manufacturingFeePerGram,
        DateTime accrualStartDate,
        DividendKind dividendKind = DividendKind.Cash)
    {
        var isPreciousMetal = asset.AssetType == AssetType.Gold || asset.AssetType == AssetType.Silver;
        var metalPerGramAmount = isPreciousMetal ? quantity * manufacturingFeePerGram : 0m;

        if (kind == TransactionKind.Dividend)
        {
            if (dividendKind == DividendKind.Stock)
            {
                // quantity = free shares, pricePerUnit = current market price (stored for record-keeping only)
                // Free shares are added at zero cost: total cost basis is unchanged, avg cost decreases.
                // NetAmount = 0 (no cash changes hands)
                var stockDivTotal = quantity * pricePerUnit;
                return (quantity, pricePerUnit, stockDivTotal, 0m);
            }

            // Cash dividend
            return (0m, 0m, quantity, quantity);
        }

        if (!asset.IsDailyAccrualFund)
        {
            var totalAmount = quantity * pricePerUnit;
            var standardNetAmount = kind == TransactionKind.Buy
                ? totalAmount + metalPerGramAmount + fees
                : totalAmount + metalPerGramAmount - fees;

            if (kind == TransactionKind.Sell && standardNetAmount < 0)
            {
                throw new InvalidOperationException("صافي البيع بعد الرسوم لا يمكن أن تكون أكبر من القيمة السوقية للأصل");
            }

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
        if (kind == TransactionKind.Sell && accrualNetAmount < 0)
        {
            throw new InvalidOperationException("صافي السحب بعد الرسوم لا يمكن أن تكون أكبر من القيمة السوقية للأصل");
        }

        return (units, unitPrice, amount, accrualNetAmount);
    }

    public static decimal CalculateUnitsHeld(Asset asset, IEnumerable<InvestmentTransaction> transactions, DateTime accrualStartDate)
    {
        decimal unitsHeld = 0;

        foreach (var transaction in transactions)
        {
            if (transaction.TransactionType == TransactionKind.Dividend)
            {
                // Stock dividends add free shares to holding
                if (transaction.DividendKind == DividendKind.Stock)
                {
                    unitsHeld += transaction.Quantity;
                }
                continue;
            }

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

    /// <summary>
    /// Calculates the TCD unit price considering Egyptian market rules:
    /// - The "day" rolls over at 5 PM Cairo time (UTC+3, no DST).
    /// - Thursday's accrual covers 3 days (Thursday + Friday + Saturday, pre-paid).
    /// - Friday and Saturday show the same price as Thursday after 5 PM — no new accrual.
    /// - Sunday resumes normal daily accrual.
    /// </summary>
    public static decimal GetDailyAccrualUnitPrice(Asset asset, DateTime asOf, DateTime? accrualStartDate = null)
    {
        var annualRate = asset.DailyAccrualAnnualRatePercent > 0 ? asset.DailyAccrualAnnualRatePercent : 16m;
        var anchorDate = (accrualStartDate ?? asset.CreatedAt).Date;

        // Convert to Egypt local time (UTC+3, no DST)
        var egyptTime = asOf.Kind == DateTimeKind.Utc
            ? asOf + EgyptOffset
            : asOf.ToUniversalTime() + EgyptOffset;

        // Accrual for a given day is posted at 5 PM Egypt time
        var effectiveDate = egyptTime.Hour < 17
            ? egyptTime.Date.AddDays(-1)   // Before 5 PM: yesterday's rate applies
            : egyptTime.Date;              // At/after 5 PM: today's rate is posted

        // Friday and Saturday roll back to the preceding Thursday
        // (Thursday already pre-paid Fri & Sat returns)
        if (effectiveDate.DayOfWeek == DayOfWeek.Friday)
            effectiveDate = effectiveDate.AddDays(-1);   // → Thursday
        else if (effectiveDate.DayOfWeek == DayOfWeek.Saturday)
            effectiveDate = effectiveDate.AddDays(-2);   // → Thursday

        if (effectiveDate <= anchorDate)
            return 1m;

        // Count accrual days from anchorDate+1 to effectiveDate:
        //   Sunday–Wednesday: +1 each
        //   Thursday:         +3 (covers Thu + Fri + Sat)
        //   Friday, Saturday: +0 (pre-counted in Thursday)
        double accrualDays = 0;
        for (var d = anchorDate.AddDays(1); d <= effectiveDate; d = d.AddDays(1))
        {
            switch (d.DayOfWeek)
            {
                case DayOfWeek.Friday:
                case DayOfWeek.Saturday:
                    break;
                case DayOfWeek.Thursday:
                    accrualDays += 3;
                    break;
                default:
                    accrualDays += 1;
                    break;
            }
        }

        var dailyGrowth = Math.Pow(1d + (double)annualRate / 100d, accrualDays / 365.25d);
        return Math.Round((decimal)dailyGrowth, 6);
    }
}
