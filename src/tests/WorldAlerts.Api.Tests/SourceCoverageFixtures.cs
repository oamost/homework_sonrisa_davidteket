namespace WorldAlerts.Api.Tests;

/// <summary>Offline payloads for the task-11 source coverage adapters.</summary>
internal static class SourceCoverageFixtures
{
    public const string EonetVolcanoes =
        """
        {
          "events": [
            {
              "id": "EONET_1",
              "title": "Volcano A",
              "categories": [ { "id": "volcanoes", "title": "Volcanoes" } ],
              "geometry": [
                { "magnitudeValue": 2, "magnitudeUnit": "VEI", "date": "2026-10-20T00:00:00Z", "type": "Point", "coordinates": [10.0, 20.0] }
              ]
            },
            {
              "id": "EONET_2",
              "title": "Volcano B",
              "categories": [ { "id": "volcanoes", "title": "Volcanoes" } ],
              "geometry": []
            }
          ]
        }
        """;

    public const string EonetDuplicateAndLandslide =
        """
        {
          "events": [
            {
              "id": "EONET_1",
              "title": "Volcano A duplicate",
              "categories": [ { "id": "volcanoes", "title": "Volcanoes" } ],
              "geometry": []
            },
            {
              "id": "EONET_3",
              "title": "Landslide C",
              "categories": [ { "id": "landslides", "title": "Landslides" } ],
              "geometry": []
            }
          ]
        }
        """;

    public const string EonetEmpty = """{ "events": [] }""";

    public const string DwdWarnings =
        """
        warnWetter.loadWarnings({
          "time": 1792548705000,
          "warnings": {
            "901056002": [
              {
                "state": "Schleswig-Holstein",
                "type": 1,
                "level": 3,
                "start": 1792512000000,
                "regionName": "Insel Helgoland",
                "end": 1792566000000,
                "description": "Heat warning for the test region.",
                "event": "HITZE",
                "headline": "Amtliche WARNUNG vor HITZE",
                "instruction": "Drink water.",
                "stateShort": "SH"
              }
            ],
            "901056099": [
              {
                "state": "Schleswig-Holstein",
                "type": 1,
                "level": 3,
                "start": 1792512000000,
                "regionName": "Insel Helgoland",
                "end": 1792566000000,
                "description": "Heat warning for the test region.",
                "event": "HITZE",
                "headline": "Amtliche WARNUNG vor HITZE",
                "instruction": "Drink water.",
                "stateShort": "SH"
              }
            ],
            "501000001": [
              {
                "state": "Niedersachsen",
                "type": 1,
                "level": 2,
                "start": 1792529130000,
                "regionName": "Ostfriesische Küste",
                "end": null,
                "description": "Second heat warning for the test region.",
                "event": "HITZE",
                "headline": "Amtliche WARNUNG vor HITZE",
                "instruction": "Drink water.",
                "stateShort": "NI"
              }
            ],
            "803000000": [
              {
                "state": "Bayern",
                "type": 1,
                "level": 1,
                "start": 1792600000000,
                "regionName": "Test Frost Region",
                "end": 1792650000000,
                "description": "Frost warning for the test region.",
                "event": "FROST",
                "headline": "Amtliche WARNUNG vor FROST",
                "instruction": "Protect plants.",
                "stateShort": "BY"
              }
            ],
            "901056003": [
              {
                "state": "Schleswig-Holstein",
                "type": 1,
                "level": 2,
                "start": 1792512000000,
                "regionName": "Wind test region",
                "end": 1792566000000,
                "description": "Wind warning that must be filtered out.",
                "event": "WINDBÖEN",
                "headline": "Amtliche WARNUNG vor WINDBÖEN",
                "instruction": "Secure loose objects.",
                "stateShort": "SH"
              }
            ]
          }
        });
        """;

    public const string NwsAlerts =
        """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "type": "Feature",
              "id": "urn:test:1",
              "geometry": {
                "type": "Polygon",
                "coordinates": [ [ [ -100, 30 ], [ -90, 30 ], [ -90, 40 ], [ -100, 40 ], [ -100, 30 ] ] ]
              },
              "properties": {
                "id": "urn:test:1",
                "event": "Heat Advisory",
                "headline": "Heat Advisory for Test County",
                "areaDesc": "Test County",
                "severity": "Moderate",
                "certainty": "Likely",
                "onset": "2026-10-21T12:00:00Z",
                "effective": "2026-10-21T11:00:00Z",
                "expires": "2026-10-22T00:00:00Z",
                "sent": "2026-10-21T10:00:00Z"
              }
            },
            {
              "type": "Feature",
              "id": "urn:test:2",
              "geometry": null,
              "properties": {
                "id": "urn:test:2",
                "event": "Tsunami Warning",
                "headline": "Tsunami Warning for Test Coast",
                "areaDesc": "Test Coast",
                "severity": "Severe",
                "certainty": "Observed",
                "onset": "2026-10-21T13:00:00Z",
                "effective": "2026-10-21T13:00:00Z",
                "expires": "2026-10-21T18:00:00Z",
                "sent": "2026-10-21T12:55:00Z"
              }
            }
          ]
        }
        """;

    public const string NwsEmpty =
        """{ "type": "FeatureCollection", "features": [] }""";

    public const string Ems =
        """
        [
          {
            "id": 1001,
            "name": "Flood in Testland",
            "category": "Flood",
            "countries": [ { "name": "Greece", "iso3": "GRC" } ],
            "eventTime": "2026-10-18T00:00:00Z",
            "activationTime": "2026-10-18T06:00:00Z",
            "lastUpdate": "2026-10-20T07:00:00Z",
            "centroid": "POINT (23.7 37.9)"
          },
          {
            "id": 1002,
            "name": "Space weather activation",
            "category": "Space Weather",
            "countries": [ "Testland" ],
            "eventTime": "2026-10-19T00:00:00Z",
            "activationTime": "2026-10-19T01:00:00Z",
            "lastUpdate": "2026-10-20T02:00:00Z"
          }
        ]
        """;

    public const string EmsPageTwo =
        """
        [
          {
            "id": 1003,
            "name": "Wildfire in Testland",
            "category": "Wildfire",
            "countries": [ { "name": "Testland", "iso3": "TST" } ],
            "eventTime": "2026-10-20T00:00:00Z",
            "activationTime": "2026-10-20T01:00:00Z",
            "lastUpdate": "2026-10-20T02:00:00Z",
            "centroid": "POINT (1.5 2.5)"
          }
        ]
        """;

    public const string Emsc =
        """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "type": "Feature",
              "id": "emsc-1",
              "properties": {
                "time": "2026-10-20T18:00:00Z",
                "mag": 4.2,
                "magtype": "ML",
                "depth": 10.5,
                "flynn_region": "Greece"
              },
              "geometry": {
                "type": "Point",
                "coordinates": [ 23.7, 37.9, 10.5 ]
              }
            }
          ]
        }
        """;

    public const string CisaKev =
        """
        {
          "title": "CISA Known Exploited Vulnerabilities",
          "vulnerabilities": [
            {
              "cveID": "CVE-2026-0001",
              "vendorProject": "Acme",
              "product": "Widget",
              "vulnerabilityName": "Acme Widget remote code execution",
              "dateAdded": "2026-10-01",
              "dueDate": "2026-10-22",
              "knownRansomwareCampaignUse": "Known"
            },
            {
              "cveID": "CVE-2026-0002",
              "vendorProject": "Example",
              "product": "Gateway",
              "vulnerabilityName": "Example Gateway authentication bypass",
              "dateAdded": "2026-10-05",
              "dueDate": "2026-10-26",
              "knownRansomwareCampaignUse": "Unknown"
            }
          ]
        }
        """;
}
