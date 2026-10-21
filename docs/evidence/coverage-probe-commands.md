# Coverage probe commands (tasks 07 and 09)

API: `http://127.0.0.1:5000`, started with a clean database. `limit` is clamped to 200, so per-source calls are used instead of one global list. GDACS currently holds 248 rows, so its count through the API stops at 200; the SQLite query at the end gives exact totals.

Field names below are the actual response fields (verified against live output).

## 07 — disaster source coverage

### 1. Which sources respond, and how many rows each returns

```bash
for s in gdacs eonet usgs who-don ifrc-go; do
  printf '%-10s ' "$s"
  curl -s "http://127.0.0.1:5000/api/disasters?source=$s&limit=200" \
    | python3 -c 'import json,sys; print(len(json.load(sys.stdin)))'
done
```

### 2. Type × region distribution for one source

```bash
curl -s 'http://127.0.0.1:5000/api/disasters?source=gdacs&limit=200' | python3 -c '
import json,sys,collections
d=json.load(sys.stdin)
print("types:", dict(collections.Counter(x["eventType"] for x in d)))
print("regions:", collections.Counter(x.get("iso3") or x.get("country") or "unknown" for x in d).most_common(10))
times=[x["eventTimeUtc"] for x in d if x["eventTimeUtc"]]
print("event time range:", min(times), "->", max(times) if times else "-")
'
```

### 3. Missing severity / location / time per source

```bash
for s in gdacs eonet usgs who-don ifrc-go; do
  printf '%-10s ' "$s"
  curl -s "http://127.0.0.1:5000/api/disasters?source=$s&limit=200" | python3 -c '
import json,sys
d=json.load(sys.stdin)
def nulls(k): return sum(1 for x in d if x.get(k) in (None,""))
t,c,s,a=nulls("eventTimeUtc"),nulls("country"),nulls("severity"),nulls("alertLevel")
print("rows="+str(len(d)),"nullEventTime="+str(t),"nullCountry="+str(c),"nullSeverity="+str(s),"nullAlert="+str(a))
'
done
```

### 4. Duplicate native ids inside a source

```bash
for s in gdacs eonet usgs who-don ifrc-go; do
  printf '%-10s ' "$s"
  curl -s "http://127.0.0.1:5000/api/disasters?source=$s&limit=200" | python3 -c '
import json,sys
d=json.load(sys.stdin); ids=[x["nativeId"] for x in d]
print(f"rows={len(ids)} unique={len(set(ids))}")
'
done
```

### 5. Sample one full event

```bash
curl -s 'http://127.0.0.1:5000/api/disasters?source=ifrc-go&limit=1' | python3 -m json.tool
```

### 6. Exact database totals (bypasses the 200 cap)

```bash
sqlite3 src/api/data/worldalerts.sqlite \
  'SELECT SourceId, COUNT(*) FROM DisasterEvents GROUP BY SourceId ORDER BY 2 DESC;'
```

## 09 — market source coverage

### 1. One latest observation per source

```bash
for s in binance-btcusdt-ticker binance-btcusdt-1h cboe-spx-delayed frankfurter-eurusd moex-imoex twse-eod; do
  curl -s "http://127.0.0.1:5000/api/markets?source=$s&limit=1" | python3 -c '
import json,sys
d=json.load(sys.stdin)
if not d: print("EMPTY"); raise SystemExit
x=d[0]
print(x["sourceId"], x["instrument"], x["value"], x["quoteUnit"], "obs="+x["observationUtc"], "delayed="+str(x["delayed"]))
'
done
```

### 2. History depth and freshness

```bash
curl -s 'http://127.0.0.1:5000/api/markets?source=binance-btcusdt-1h&limit=200' | python3 -c '
import json,sys
d=json.load(sys.stdin); t=[x["observationUtc"] for x in d]
print("candles:", len(d), "oldest:", min(t), "newest:", max(t))
print("intervals:", {x["interval"] for x in d}, "delayed:", {x["delayed"] for x in d})
'
curl -s 'http://127.0.0.1:5000/api/markets?source=twse-eod&limit=5' | python3 -c '
import json,sys
d=json.load(sys.stdin)
for x in d: print(x["sourceId"], x["instrument"], x["value"], x["quoteUnit"], x["observationUtc"])
print("rows:", len(d))
'
```

### 3. Delay/venue semantics

```bash
for s in cboe-spx-delayed moex-imoex frankfurter-eurusd twse-eod; do
  curl -s "http://127.0.0.1:5000/api/markets?source=$s&limit=1" | python3 -c '
import json,sys
x=json.load(sys.stdin)[0]
print(x["sourceId"], "|", x["delaySemantics"], "|", x["provenance"])
'
done
```

### 4. Cross-source agreement (same asset, two sources)

```bash
curl -s 'http://127.0.0.1:5000/api/markets?limit=200' | python3 -c '
import json,sys
d=json.load(sys.stdin)
btc=[x for x in d if x["instrument"]=="BTCUSDT"]
for x in btc: print(x["sourceId"], x["interval"], x["value"], x["observationUtc"])
print("sources returned:", sorted({x["sourceId"] for x in d}))
'
```

### 5. Exact database totals

```bash
sqlite3 src/api/data/worldalerts.sqlite \
  'SELECT SourceId, COUNT(*) FROM MarketObservations GROUP BY SourceId ORDER BY 1;'
```

## Subscriptions (task 03) — smoke test

```bash
curl -s -X POST http://127.0.0.1:5000/api/subscriptions \
  -H 'Content-Type: application/json' \
  -d '{"eventType":"disaster.earthquake","channel":"email","destination":"me@example.com"}'
curl -s 'http://127.0.0.1:5000/api/subscriptions'
curl -s -o /dev/null -w 'delete: %{http_code}\n' -X DELETE http://127.0.0.1:5000/api/subscriptions/1
```

`eventType` here is only a free-form probe string; the approved taxonomy is still task 01 and is not implied by this example.

## Known findings from these probes

- WHO DON and IFRC GO rows have `eventTimeUtc: null`; ordering keeps them last. Their usable dates must come from other fields before importance/catch-up logic can use them.
- IFRC `eventType` values arrive with source-native casing and names (`Flood`, `Pluvial/Flash Flood`), unlike normalized GDACS/EONET values (`flood`). Normalization is still to be fixed.
- USGS and EONET rows carry no `country`/`iso3`, so region-coverage analysis for those sources needs coordinates, not country codes.
- GDACS currently exposes no active tsunami or volcano event; volcano coverage was verified separately through EONET's category feed (5 live events).
