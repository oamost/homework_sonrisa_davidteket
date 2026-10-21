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

    /// <summary>
    /// GDACS recency mix for a fetch at 2026-10-21T12:00:00Z: current, recently
    /// modified, old, missing all recency fields, event-date fallback, and an
    /// event exactly on the 7-day boundary.
    /// </summary>
    public const string GdacsRecency =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <rss version="2.0" xmlns:gdacs="http://www.gdacs.org">
          <channel>
            <title>GDACS recency</title>
            <item>
              <title>Current old event</title>
              <gdacs:eventid>201</gdacs:eventid>
              <gdacs:eventtype>EQ</gdacs:eventtype>
              <gdacs:iscurrent>true</gdacs:iscurrent>
              <gdacs:fromdate>2026-01-01T00:00:00</gdacs:fromdate>
              <gdacs:datemodified>2026-01-01T00:00:00</gdacs:datemodified>
            </item>
            <item>
              <title>Recently modified</title>
              <gdacs:eventid>202</gdacs:eventid>
              <gdacs:eventtype>FL</gdacs:eventtype>
              <gdacs:iscurrent>false</gdacs:iscurrent>
              <gdacs:fromdate>2026-01-01T00:00:00</gdacs:fromdate>
              <gdacs:datemodified>2026-10-20T00:00:00</gdacs:datemodified>
            </item>
            <item>
              <title>Old archive row</title>
              <gdacs:eventid>203</gdacs:eventid>
              <gdacs:eventtype>TC</gdacs:eventtype>
              <gdacs:iscurrent>false</gdacs:iscurrent>
              <gdacs:fromdate>2026-01-01T00:00:00</gdacs:fromdate>
              <gdacs:datemodified>2026-01-01T00:00:00</gdacs:datemodified>
            </item>
            <item>
              <title>No recency fields</title>
              <gdacs:eventid>204</gdacs:eventid>
              <gdacs:eventtype>VO</gdacs:eventtype>
            </item>
            <item>
              <title>Event date fallback</title>
              <gdacs:eventid>205</gdacs:eventid>
              <gdacs:eventtype>WF</gdacs:eventtype>
              <gdacs:fromdate>2026-10-20T00:00:00</gdacs:fromdate>
            </item>
            <item>
              <title>Exactly on the window boundary</title>
              <gdacs:eventid>206</gdacs:eventid>
              <gdacs:eventtype>EQ</gdacs:eventtype>
              <gdacs:iscurrent>false</gdacs:iscurrent>
              <gdacs:fromdate>2026-01-01T00:00:00</gdacs:fromdate>
              <gdacs:datemodified>2026-10-14T12:00:00</gdacs:datemodified>
            </item>
          </channel>
        </rss>
        """;

    /// <summary>
    /// EONET recency mix for a fetch at 2026-10-21T12:00:00Z: recent wildfire,
    /// old wildfire, 81-day-old volcano (inside the 90-day volcano window),
    /// equally old wildfire (outside the 30-day default), missing geometry, and
    /// an event exactly on the 30-day boundary.
    /// </summary>
    public const string EonetRecency =
        """
        {
          "events": [
            {
              "id": "EONET_RECENT",
              "title": "Recent wildfire",
              "categories": [ { "id": "wildfires", "title": "Wildfires" } ],
              "geometry": [ { "date": "2026-10-20T00:00:00Z", "type": "Point", "coordinates": [1.0, 2.0] } ]
            },
            {
              "id": "EONET_OLD",
              "title": "Old wildfire",
              "categories": [ { "id": "wildfires", "title": "Wildfires" } ],
              "geometry": [ { "date": "2026-01-01T00:00:00Z", "type": "Point", "coordinates": [3.0, 4.0] } ]
            },
            {
              "id": "EONET_VOLCANO",
              "title": "Old volcano",
              "categories": [ { "id": "volcanoes", "title": "Volcanoes" } ],
              "geometry": [ { "date": "2026-08-01T00:00:00Z", "type": "Point", "coordinates": [5.0, 6.0] } ]
            },
            {
              "id": "EONET_OLD_WILDFIRE",
              "title": "Same age wildfire",
              "categories": [ { "id": "wildfires", "title": "Wildfires" } ],
              "geometry": [ { "date": "2026-08-01T00:00:00Z", "type": "Point", "coordinates": [7.0, 8.0] } ]
            },
            {
              "id": "EONET_MISSING",
              "title": "No geometry",
              "categories": [ { "id": "wildfires", "title": "Wildfires" } ],
              "geometry": []
            },
            {
              "id": "EONET_BOUNDARY",
              "title": "Exactly on the window boundary",
              "categories": [ { "id": "wildfires", "title": "Wildfires" } ],
              "geometry": [ { "date": "2026-09-21T12:00:00Z", "type": "Point", "coordinates": [9.0, 10.0] } ]
            }
          ]
        }
        """;

    /// <summary>
    /// IFRC recency mix for a fetch at 2026-10-21T12:00:00Z: recent creation,
    /// recent update, old audit dates, a start-date-only fallback, no timestamps,
    /// and an event exactly on the 7-day boundary.
    /// </summary>
    public const string IfrcRecency =
        """
        {
          "count": 6,
          "results": [
            {
              "id": 401,
              "name": "Recent creation",
              "dtype": { "name": "Flood" },
              "disaster_start_date": "2026-10-18T00:00:00Z",
              "created_at": "2026-10-20T00:00:00Z"
            },
            {
              "id": 402,
              "name": "Old archive event",
              "dtype": { "name": "Flood" },
              "disaster_start_date": "2026-01-01T00:00:00Z",
              "created_at": "2026-01-01T00:00:00Z",
              "updated_at": "2026-01-02T00:00:00Z"
            },
            {
              "id": 403,
              "name": "Start date fallback",
              "dtype": { "name": "Flood" },
              "disaster_start_date": "2026-10-19T00:00:00Z"
            },
            {
              "id": 404,
              "name": "No timestamps",
              "dtype": { "name": "Flood" }
            },
            {
              "id": 405,
              "name": "Exactly on the window boundary",
              "dtype": { "name": "Flood" },
              "created_at": "2026-10-14T12:00:00Z"
            },
            {
              "id": 406,
              "name": "Recent update",
              "dtype": { "name": "Flood" },
              "created_at": "2026-01-01T00:00:00Z",
              "updated_at": "2026-10-20T00:00:00Z"
            }
          ]
        }
        """;

    /// <summary>
    /// Copernicus EMS recency mix for a fetch at 2026-10-21T12:00:00Z: closed
    /// and old, open and old, closed but recently updated, missing closed,
    /// closed without lastUpdate, and a closed activation exactly on the
    /// 14-day boundary.
    /// </summary>
    public const string EmsRecency =
        """
        [
          {
            "id": 501,
            "name": "Closed old activation",
            "category": "Flood",
            "closed": true,
            "activationTime": "2026-01-01T00:00:00Z",
            "lastUpdate": "2026-01-01T00:00:00Z"
          },
          {
            "id": 502,
            "name": "Open old activation",
            "category": "Flood",
            "closed": false,
            "activationTime": "2026-01-01T00:00:00Z",
            "lastUpdate": "2026-01-01T00:00:00Z"
          },
          {
            "id": 503,
            "name": "Closed recently updated activation",
            "category": "Wildfire",
            "closed": true,
            "activationTime": "2026-01-01T00:00:00Z",
            "lastUpdate": "2026-10-20T00:00:00Z"
          },
          {
            "id": 504,
            "name": "Missing closed field",
            "category": "Flood",
            "activationTime": "2026-01-01T00:00:00Z"
          },
          {
            "id": 505,
            "name": "Closed without lastUpdate",
            "category": "Flood",
            "closed": true,
            "activationTime": "2026-01-01T00:00:00Z"
          },
          {
            "id": 506,
            "name": "Closed exactly on the window boundary",
            "category": "Flood",
            "closed": true,
            "activationTime": "2026-01-01T00:00:00Z",
            "lastUpdate": "2026-10-07T12:00:00Z"
          }
        ]
        """;
}
