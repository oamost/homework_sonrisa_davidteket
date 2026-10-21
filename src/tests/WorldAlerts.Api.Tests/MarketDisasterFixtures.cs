namespace WorldAlerts.Api.Tests;

/// <summary>
/// Minimal offline payloads shaped like each provider response. They are test
/// fixtures only; nothing here is fetched from the network.
/// </summary>
internal static class MarketFixtures
{
    public const string BinanceTicker =
        """
        {
          "symbol": "BTCUSDT",
          "priceChange": "150.00",
          "priceChangePercent": "0.22",
          "lastPrice": "67250.10",
          "openPrice": "67100.10",
          "openTime": 1761040800000,
          "closeTime": 1761127199999
        }
        """;

    public const string BinanceKlines =
        """
        [
          [1761040800000, "67000.00", "67500.00", "66900.00", "67200.00", "12.5", 1761044399999, "840000.00", 100, "6.0", "400000.00", "0"],
          [1761044400000, "67200.00", "67600.00", "67100.00", "67450.00", "10.0", 1761047999999, "674500.00", 90, "5.0", "337250.00", "0"],
          ["bad-candle"]
        ]
        """;

    public const string Frankfurter =
        """
        {
          "amount": 1.0,
          "base": "EUR",
          "date": "2026-10-20",
          "rates": { "USD": 1.1642 }
        }
        """;

    public const string Cboe =
        """
        {
          "data": {
            "symbol": "^SPX",
            "last_trade_price": 6754.12,
            "last_trade_time": "2026-10-20T20:00:00Z",
            "price_change": 12.5
          }
        }
        """;

    public const string Twse =
        """
        [
          { "Code": "0050", "Name": "元大台灣50", "ClosingPrice": "135.60", "MonthlyAveragePrice": "134.84", "Date": "1151020" },
          { "Code": "2330", "Name": "台積電", "ClosingPrice": "1,100.00", "MonthlyAveragePrice": "1,080.00", "Date": "115/10/20" },
          { "Code": "9999", "Name": "No price", "ClosingPrice": "--", "Date": "115/10/20" }
        ]
        """;

    public const string Moex =
        """
        {
          "marketdata": {
            "columns": ["SECID", "BOARDID", "CURRENTVALUE", "LASTVALUE", "UPDATETIME", "UPDATEDATE", "SYSTIME"],
            "data": [["IMOEX", "SNDX", 3120.45, 3119.0, "18:39:59", "2026-10-20", "2026-10-20 18:40:01"]]
          }
        }
        """;
}

internal static class DisasterFixtures
{
    public const string GdacsRss =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <rss version="2.0"
             xmlns:gdacs="http://www.gdacs.org"
             xmlns:geo="http://www.w3.org/2003/01/geo/wgs84_pos#">
          <channel>
            <title>GDACS</title>
            <item>
              <title>Earthquake in Japan</title>
              <link>https://www.gdacs.org/report.aspx?eventid=123</link>
              <guid>gdacs-123</guid>
              <pubDate>Tue, 20 Oct 2026 10:00:00 GMT</pubDate>
              <gdacs:eventtype>EQ</gdacs:eventtype>
              <gdacs:eventid>123</gdacs:eventid>
              <gdacs:eventname></gdacs:eventname>
              <gdacs:alertlevel>Orange</gdacs:alertlevel>
              <gdacs:alertscore>1.5</gdacs:alertscore>
              <gdacs:fromdate>2026-10-20T09:00:00</gdacs:fromdate>
              <gdacs:datemodified>2026-10-20T11:00:00</gdacs:datemodified>
              <gdacs:country>Japan</gdacs:country>
              <gdacs:iso3>JPN</gdacs:iso3>
              <gdacs:severity unit="M" value="5.4" />
              <geo:Point>
                <geo:lat>35.1</geo:lat>
                <geo:long>139.2</geo:long>
              </geo:Point>
            </item>
            <item>
              <title>Item without a stable id</title>
            </item>
          </channel>
        </rss>
        """;

    public const string Eonet =
        """
        {
          "events": [
            {
              "id": "EONET_1",
              "title": "Wildfire A",
              "categories": [ { "id": "wildfires", "title": "Wildfires" } ],
              "sources": [ { "id": "InciWeb", "url": "https://example.test/fire" } ],
              "geometry": [
                { "magnitudeValue": 500, "magnitudeUnit": "acres", "date": "2026-10-19T00:00:00Z", "type": "Point", "coordinates": [-120.5, 38.2] },
                { "magnitudeValue": 750, "magnitudeUnit": "acres", "date": "2026-10-20T00:00:00Z", "type": "Point", "coordinates": [-120.4, 38.3] }
              ]
            },
            {
              "id": "EONET_2",
              "title": "Storm without geometry",
              "categories": [ { "id": "severeStorms", "title": "Severe Storms" } ],
              "geometry": []
            }
          ]
        }
        """;

    public const string Usgs =
        """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "type": "Feature",
              "id": "us7000abcd",
              "properties": {
                "mag": 4.5,
                "place": "10 km south of Test",
                "time": 1761046800000,
                "updated": 1761047400000,
                "tsunami": 1,
                "sig": 312,
                "type": "earthquake",
                "title": "M 4.5 - 10 km south of Test",
                "url": "https://earthquake.usgs.gov/earthquakes/eventpage/us7000abcd",
                "magType": "mb",
                "alert": "yellow"
              },
              "geometry": {
                "type": "Point",
                "coordinates": [139.2, 35.1, 10.0]
              }
            }
          ]
        }
        """;

    public const string Ifrc =
        """
        {
          "count": 1,
          "results": [
            {
              "id": 987,
              "name": "Flood in Test Region",
              "dtype": { "id": 2, "name": "Flood" },
              "start_date": "2026-10-18T00:00:00Z",
              "created_at": "2026-10-18T06:00:00Z",
              "modified_at": "2026-10-20T07:00:00Z",
              "countries": [ { "name": "Test Country", "iso3": "TST" } ],
              "ifrc_severity_level": "Orange",
              "latitude": 1.5,
              "longitude": 2.5
            }
          ]
        }
        """;
}
