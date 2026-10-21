using System.Globalization;
using System.Text.Json;

namespace WorldAlerts.Api.Markets;

/// <summary>
/// Parses the six keyless market payload shapes into normalized observations.
/// Timestamps come from each provider whenever it supplies one; fetch time is
/// only used as a marked fallback and is always stored separately.
/// </summary>
public static class MarketParser
{
    public const string BinanceTicker = "binance-ticker";
    public const string BinanceKlines = "binance-klines";
    public const string Frankfurter = "frankfurter";
    public const string Cboe = "cboe";
    public const string Twse = "twse";
    public const string Moex = "moex";

    public static MarketParseResult Parse(string body, MarketSource source, DateTimeOffset fetchedUtc)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(source);

        // An empty 2xx body is a valid empty result, not a parse crash.
        if (string.IsNullOrWhiteSpace(body))
        {
            return new MarketParseResult(Array.Empty<MarketObservation>(), 0);
        }

        return source.Kind switch
        {
            BinanceTicker => ParseBinanceTicker(body, source, fetchedUtc),
            BinanceKlines => ParseBinanceKlines(body, source, fetchedUtc),
            Frankfurter => ParseFrankfurter(body, source, fetchedUtc),
            Cboe => ParseCboe(body, source, fetchedUtc),
            Twse => ParseTwse(body, source, fetchedUtc),
            Moex => ParseMoex(body, source, fetchedUtc),
            _ => throw new InvalidOperationException($"Unsupported market source kind '{source.Kind}'."),
        };
    }

    private static MarketParseResult ParseBinanceTicker(
        string body,
        MarketSource source,
        DateTimeOffset fetchedUtc)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var value = DoubleValue(root, "lastPrice") ?? DoubleValue(root, "price");
        if (value is null)
        {
            return new MarketParseResult(Array.Empty<MarketObservation>(), 1);
        }

        var observation = UnixMilliseconds(root, "closeTime") ?? fetchedUtc;
        return new MarketParseResult(
            new[]
            {
                Create(source, source.Instrument, value.Value, observation, fetchedUtc),
            },
            0);
    }

    private static MarketParseResult ParseBinanceKlines(
        string body,
        MarketSource source,
        DateTimeOffset fetchedUtc)
    {
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new MarketParseResult(Array.Empty<MarketObservation>(), 1);
        }

        var observations = new List<MarketObservation>();
        var skipped = 0;
        foreach (var candle in document.RootElement.EnumerateArray())
        {
            if (candle.ValueKind != JsonValueKind.Array || candle.GetArrayLength() < 5)
            {
                skipped++;
                continue;
            }

            var openTime = UnixMilliseconds(candle[0]);
            var close = Number(candle[4]);
            if (openTime is null || close is null)
            {
                skipped++;
                continue;
            }

            observations.Add(Create(source, source.Instrument, close.Value, openTime.Value, fetchedUtc));
        }

        return new MarketParseResult(observations, skipped);
    }

    private static MarketParseResult ParseFrankfurter(
        string body,
        MarketSource source,
        DateTimeOffset fetchedUtc)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!TryProperty(root, "rates", out var rates) || rates.ValueKind != JsonValueKind.Object)
        {
            return new MarketParseResult(Array.Empty<MarketObservation>(), 1);
        }

        var quoteCurrency = source.ToCurrency;
        if (string.IsNullOrWhiteSpace(quoteCurrency))
        {
            quoteCurrency = rates.EnumerateObject().Select(property => property.Name).FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(quoteCurrency)
            || !TryProperty(rates, quoteCurrency, out var rateElement)
            || Number(rateElement) is not { } rate
            || !DateValue(root, "date").HasValue)
        {
            return new MarketParseResult(Array.Empty<MarketObservation>(), 1);
        }

        // The ECB reference date is a date, never an intraday quote.
        var referenceDate = DateOnly.FromDateTime(DateValue(root, "date")!.Value.UtcDateTime);
        var observation = new DateTimeOffset(
            referenceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        return new MarketParseResult(
            new[]
            {
                Create(source, source.Instrument, rate, observation, fetchedUtc),
            },
            0);
    }

    private static MarketParseResult ParseCboe(
        string body,
        MarketSource source,
        DateTimeOffset fetchedUtc)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var quote = TryProperty(root, "data", out var data) && data.ValueKind == JsonValueKind.Object
            ? data
            : root;

        var value = DoubleValue(quote, "last_trade_price")
            ?? DoubleValue(quote, "current_price")
            ?? DoubleValue(quote, "close")
            ?? DoubleValue(quote, "last")
            ?? DoubleValue(quote, "price");
        if (value is null)
        {
            return new MarketParseResult(Array.Empty<MarketObservation>(), 1);
        }

        var observation = DateValue(quote, "last_trade_time")
            ?? DateValue(quote, "updated")
            ?? DateValue(quote, "quote_time")
            ?? DateValue(quote, "timestamp")
            ?? fetchedUtc;
        var instrument = StringValue(quote, "symbol") ?? source.Instrument;

        return new MarketParseResult(
            new[]
            {
                Create(source, instrument, value.Value, observation, fetchedUtc),
            },
            0);
    }

    private static MarketParseResult ParseTwse(
        string body,
        MarketSource source,
        DateTimeOffset fetchedUtc)
    {
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new MarketParseResult(Array.Empty<MarketObservation>(), 1);
        }

        var observations = new List<MarketObservation>();
        var skipped = 0;
        var allowlist = source.Instruments
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Safety default: without an explicit allowlist, never persist a full venue listing.
        if (allowlist.Count == 0)
        {
            return new MarketParseResult(
                Array.Empty<MarketObservation>(),
                document.RootElement.GetArrayLength());
        }

        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                skipped++;
                continue;
            }

            var code = StringValue(row, "Code", "StockCode", "code");
            if (string.IsNullOrWhiteSpace(code) || !allowlist.Contains(code))
            {
                skipped++;
                continue;
            }

            var value = DoubleValue(row, "ClosingPrice", "Close", "closingPrice");
            if (value is null)
            {
                skipped++;
                continue;
            }

            // Every row is an end-of-day price. Use the ROC trading date when the
            // payload supplies one; otherwise mark the fetch date in Taipei as the
            // trading date. The stored time is a date marker at 00:00Z.
            var observation = ParseRocDate(StringValue(row, "Date", "TradeDate", "date"))
                ?? TaipeiDateMarker(fetchedUtc);

            observations.Add(Create(source, code, value.Value, observation, fetchedUtc));
        }

        return new MarketParseResult(observations, skipped);
    }

    private static MarketParseResult ParseMoex(
        string body,
        MarketSource source,
        DateTimeOffset fetchedUtc)
    {
        using var document = JsonDocument.Parse(body);
        if (!TryProperty(document.RootElement, "marketdata", out var marketData)
            || marketData.ValueKind != JsonValueKind.Object
            || !TryProperty(marketData, "columns", out var columnsElement)
            || columnsElement.ValueKind != JsonValueKind.Array
            || !TryProperty(marketData, "data", out var rows)
            || rows.ValueKind != JsonValueKind.Array
            || rows.GetArrayLength() == 0)
        {
            return new MarketParseResult(Array.Empty<MarketObservation>(), 1);
        }

        var columns = columnsElement.EnumerateArray()
            .Select((column, index) => (Name: column.GetString(), Index: index))
            .Where(column => column.Name is not null)
            .ToDictionary(column => column.Name!, column => column.Index, StringComparer.OrdinalIgnoreCase);

        var row = rows[0];
        if (row.ValueKind != JsonValueKind.Array)
        {
            return new MarketParseResult(Array.Empty<MarketObservation>(), 1);
        }

        var value = ColumnDouble(row, columns, "CURRENTVALUE", "LASTVALUE", "LAST", "VALUE");
        if (value is null)
        {
            return new MarketParseResult(Array.Empty<MarketObservation>(), 1);
        }

        var instrument = ColumnString(row, columns, "SECID") ?? source.Instrument;
        var observation = MoexTimestamp(row, columns) ?? fetchedUtc;

        return new MarketParseResult(
            new[]
            {
                Create(source, instrument, value.Value, observation, fetchedUtc),
            },
            0);
    }

    private static DateTimeOffset? MoexTimestamp(JsonElement row, IReadOnlyDictionary<string, int> columns)
    {
        var tradeDate = ColumnString(row, columns, "UPDATEDATE", "TRADEDATE");
        var updateTime = ColumnString(row, columns, "UPDATETIME", "TIME");

        if (tradeDate is not null)
        {
            var combined = updateTime is null ? $"{tradeDate}T00:00:00" : $"{tradeDate}T{updateTime}";
            // MOEX ISS date/time columns are Moscow exchange time (UTC+03:00).
            if (!combined.EndsWith('Z') && !combined.Contains('+', StringComparison.Ordinal))
            {
                combined += "+03:00";
            }

            if (TryParseDate(combined, out var parsed))
            {
                return parsed;
            }
        }

        var systemTime = ColumnString(row, columns, "SYSTIME");
        if (systemTime is not null)
        {
            if (!systemTime.EndsWith('Z') && !systemTime.Contains('+', StringComparison.Ordinal))
            {
                systemTime += "+03:00";
            }

            if (TryParseDate(systemTime, out var systemParsed))
            {
                return systemParsed;
            }
        }

        return null;
    }

    private static MarketObservation Create(
        MarketSource source,
        string instrument,
        double value,
        DateTimeOffset observation,
        DateTimeOffset fetchedUtc) =>
        new(
            SourceId: source.Id,
            Venue: source.Venue,
            Region: source.Region,
            Instrument: instrument,
            QuoteUnit: source.QuoteUnit,
            Value: value,
            ObservationUtc: observation.ToUniversalTime(),
            FetchedUtc: fetchedUtc.ToUniversalTime(),
            Delayed: source.Delayed,
            DelaySemantics: source.DelaySemantics,
            Interval: source.Interval,
            Provenance: source.Url);

    private static DateTimeOffset? ParseRocDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();

        // TWSE also emits the ROC date without separators, e.g. 1151020.
        if (trimmed.Length == 7
            && trimmed.All(char.IsAsciiDigit)
            && int.TryParse(trimmed.AsSpan(0, 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var compactYear)
            && int.TryParse(trimmed.AsSpan(3, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var compactMonth)
            && int.TryParse(trimmed.AsSpan(5, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var compactDay))
        {
            return TryCreateDateMarker(compactYear + 1911, compactMonth, compactDay);
        }

        var parts = trimmed.Split('/', '-');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
        {
            return TryParseDate(text, out var gregorian) ? DateMarker(gregorian) : null;
        }

        if (year < 1911)
        {
            year += 1911;
        }

        return TryCreateDateMarker(year, month, day);
    }

    private static DateTimeOffset TaipeiDateMarker(DateTimeOffset fetchedUtc)
    {
        var taipei = fetchedUtc.ToOffset(TimeSpan.FromHours(8));
        return new DateTimeOffset(taipei.Year, taipei.Month, taipei.Day, 0, 0, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset? DateMarker(DateTimeOffset value) =>
        TryCreateDateMarker(value.Year, value.Month, value.Day);

    private static DateTimeOffset? TryCreateDateMarker(int year, int month, int day)
    {
        if (year < 1 || year > 9999 || month < 1 || month > 12 || day < 1 || day > 31)
        {
            return null;
        }

        try
        {
            return new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool TryParseDate(string? text, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return DateTimeOffset.TryParse(
            text.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out value);
    }

    private static string? ColumnString(
        JsonElement row,
        IReadOnlyDictionary<string, int> columns,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (columns.TryGetValue(name, out var index) && index < row.GetArrayLength())
            {
                var element = row[index];
                if (element.ValueKind == JsonValueKind.String)
                {
                    var text = element.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
                else if (element.ValueKind == JsonValueKind.Number)
                {
                    return element.GetRawText();
                }
            }
        }

        return null;
    }

    private static double? ColumnDouble(
        JsonElement row,
        IReadOnlyDictionary<string, int> columns,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (columns.TryGetValue(name, out var index) && index < row.GetArrayLength())
            {
                var value = Number(row[index]);
                if (value is not null)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static DateTimeOffset? UnixMilliseconds(JsonElement element, string propertyName) =>
        TryProperty(element, propertyName, out var value) ? UnixMilliseconds(value) : null;

    private static DateTimeOffset? UnixMilliseconds(JsonElement element)
    {
        var milliseconds = Number(element);
        if (milliseconds is null)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)milliseconds.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? StringValue(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryProperty(element, name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
            else if (value.ValueKind == JsonValueKind.Number)
            {
                return value.GetRawText();
            }
        }

        return null;
    }

    private static double? DoubleValue(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryProperty(element, name, out var value) && Number(value) is { } number)
            {
                return number;
            }
        }

        return null;
    }

    private static DateTimeOffset? DateValue(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryProperty(element, name, out var value) && DateValue(value) is { } date)
            {
                return date;
            }
        }

        return null;
    }

    private static DateTimeOffset? DateValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return TryParseDate(element.GetString(), out var parsed) ? parsed : null;
        }

        return element.ValueKind == JsonValueKind.Number ? UnixMilliseconds(element) : null;
    }

    private static double? Number(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number))
        {
            return number;
        }

        if (element.ValueKind == JsonValueKind.String
            && double.TryParse(
                element.GetString()?.Replace(",", string.Empty, StringComparison.Ordinal),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
