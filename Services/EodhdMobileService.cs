using System.Globalization;
using System.Net;
using System.Text.Json;
using Investy.Mobile.Models;

namespace Investy.Mobile.Services;

public class EodhdMobileService
{
    private const string ApiKeyStorageKey = "eodhd_api_key";
    private readonly HttpClient _httpClient;

    public EodhdMobileService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string?> GetApiKeyAsync()
    {
        return await SecureStorage.Default.GetAsync(ApiKeyStorageKey);
    }

    public async Task<bool> HasApiKeyAsync()
    {
        return !string.IsNullOrWhiteSpace(await GetApiKeyAsync());
    }

    public async Task<EodhdUserStatus?> SaveAndValidateApiKeyAsync(string apiKey)
    {
        var cleaned = apiKey.Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        var status = await GetUserStatusAsync(cleaned);
        if (status == null)
        {
            return null;
        }

        await SecureStorage.Default.SetAsync(ApiKeyStorageKey, cleaned);
        return status;
    }

    public Task ClearApiKeyAsync()
    {
        SecureStorage.Default.Remove(ApiKeyStorageKey);
        return Task.CompletedTask;
    }

    public async Task<EodhdUserStatus?> GetUserStatusAsync(string? apiKey = null)
    {
        apiKey ??= await GetApiKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        try
        {
            var url = $"https://eodhd.com/api/user?api_token={WebUtility.UrlEncode(apiKey)}&fmt=json";
            using var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            var user = await JsonSerializer.DeserializeAsync<EodhdUserInfo>(stream, JsonOptions);
            if (user == null || string.IsNullOrWhiteSpace(user.Name))
            {
                return null;
            }

            var usedToday = GetApiRequestsUsedToday(user);
            var totalAvailable = user.DailyRateLimit + user.ExtraLimit;
            var remaining = Math.Max(0, user.DailyRateLimit - usedToday + user.ExtraLimit);
            return new EodhdUserStatus(user.Name, user.Email, user.SubscriptionType, usedToday, user.DailyRateLimit, user.ExtraLimit, totalAvailable, remaining);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<StockSearchResult>> SearchEgyptStocksAsync(string query, int limit = 20)
    {
        var apiKey = await GetApiKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(query))
        {
            return new();
        }

        try
        {
            var normalized = query.Trim();
            var url = $"https://eodhd.com/api/exchange-symbol-list/EGX?api_token={WebUtility.UrlEncode(apiKey)}&fmt=json";
            using var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return new();
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            var symbols = await JsonSerializer.DeserializeAsync<List<EodhdSymbol>>(stream, JsonOptions);
            if (symbols == null)
            {
                return new();
            }

            return symbols
                .Where(item => item.Code.StartsWith(normalized, StringComparison.OrdinalIgnoreCase)
                    || item.Name.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .Select(item =>
                {
                    var exchange = string.IsNullOrWhiteSpace(item.Exchange) ? "EGX" : item.Exchange.Trim().ToUpperInvariant();
                    var code = item.Code.Trim().ToUpperInvariant();
                    var externalTicker = code.Contains('.') ? code : $"{code}.{exchange}";
                    return new StockSearchResult(
                        code.Contains('.') ? code.Split('.')[0] : code,
                        string.IsNullOrWhiteSpace(item.Name) ? code : item.Name.Trim(),
                        string.IsNullOrWhiteSpace(item.Currency) ? "EGP" : item.Currency.Trim().ToUpperInvariant(),
                        externalTicker);
                })
                .ToList();
        }
        catch
        {
            return new();
        }
    }

    public async Task<(decimal Price, DateTime Date)?> FetchLatestPriceAsync(string ticker)
    {
        var apiKey = await GetApiKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(ticker))
        {
            return null;
        }

        try
        {
            var symbol = NormalizeSymbol(ticker);
            var url = $"https://eodhd.com/api/eod/{WebUtility.UrlEncode(symbol)}?api_token={WebUtility.UrlEncode(apiKey)}&fmt=json&limit=1";
            using var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            var data = await JsonSerializer.DeserializeAsync<List<EodhdEodPrice>>(stream, JsonOptions);
            var latest = data?
                .Select(item => new
                {
                    Item = item,
                    Parsed = DateTime.TryParse(item.Date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date),
                    Date = date
                })
                .Where(item => item.Parsed)
                .OrderBy(item => item.Date)
                .LastOrDefault();

            if (latest == null || latest.Item.Close <= 0)
            {
                return null;
            }

            return (latest.Item.Close, latest.Date);
        }
        catch
        {
            return null;
        }
    }

    private static int GetApiRequestsUsedToday(EodhdUserInfo info)
    {
        if (!DateTime.TryParse(info.ApiRequestsDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var apiRequestsDate))
        {
            return info.ApiRequests;
        }

        return apiRequestsDate.Date == DateTime.UtcNow.Date ? info.ApiRequests : 0;
    }

    private static string NormalizeSymbol(string ticker)
    {
        var trimmed = ticker.Trim().ToUpperInvariant();
        return trimmed.Contains('.') ? trimmed : $"{trimmed}.EGX";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class EodhdUserInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string SubscriptionType { get; set; } = string.Empty;
        public int ApiRequests { get; set; }
        public string ApiRequestsDate { get; set; } = string.Empty;
        public int DailyRateLimit { get; set; }
        public int ExtraLimit { get; set; }
    }

    private sealed class EodhdSymbol
    {
        public string Code { get; set; } = string.Empty;
        public string Exchange { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
    }

    private sealed class EodhdEodPrice
    {
        public string Date { get; set; } = string.Empty;
        public decimal Close { get; set; }
    }
}
