# Buildhiba-javítás: két dátumfüggő katasztrófateszt

## Megfigyelt hiba (felhasználói futás)

`./run.sh up` alatt a .NET fordítás 0 hibával / 0 figyelmeztetéssel sikerült, de az 571 xUnit-tesztből kettő elbukott:

- `DisasterIngestionTests.Gdacs_filtered_rows_are_skipped_and_never_stored`: 4 helyett 3 esemény.
- `DisasterIngestionTests.Ifrc_requests_newest_first_and_skips_archive_rows_before_storage`: 4 helyett 3 esemény.

A Dockerfile helyesen leállítja a buildet teszthibánál, így az ezt követően, már nem futó szerverre indított `scripts/smoke.py` minden HTTP-ellenőrzése sikertelen lett. **Nem** bizonyít SMTP/Slack-szolgáltatói hibát.

## Ok és változtatás

Mindkét teszt 2026-10-21 12:00:00 UTC pillanatra felépített fixture-t használ, és 2026-10-14 12:00:00 UTC időpontú, *pontosan* 7 napos határeseményt vár befogadni. A `DisasterIngestionService` korábban a gép `DateTimeOffset.UtcNow` idejével szűrt. Már néhány perccel a fixture ideje után a határesemény kiesett.

- A service most a már konstruktorból elérhető `TimeProvider.GetUtcNow()` órát használja (élesben továbbra is `TimeProvider.System`).
- A tesztsegéd immár választható órát fogad; a két recency integrációs teszt a fixture eredeti pillanatára rögzített órát használja.
- Az elvárt eseményszám, szűrőszabály, tesztfuttatás és Docker-build biztonsági kapuja **változatlan**.

## Az új build ellenőrzése

```bash
cd /prod/sonrisa_homework
./run.sh up
python3 scripts/smoke.py
./run.sh status
```

A buildnek mind az **571 .NET-tesztet** át kell engednie (ha közben nem változott a tesztkészlet), majd el kell indulnia a `http://127.0.0.1:8080/` címen. Ezután külön ellenőrizd az adminbelépést (`admin`/`admin`), a mai hírfeedet és a két valós kézbesítési tesztet. A smoke-script nem küld emailt vagy Slack-üzenetet.

**Validáció státusza:** a feltöltött hibás futás alapján okdiagnózis és forráskód-javítás elkészült; a hotfixet a chat végrehajtó környezetében .NET SDK/Docker hiányában nem lehetett újrafordítani. A sikeres teljes buildet és az éles szolgáltatói kézbesítést a felhasználói gépen kell igazolni. Git commit/push nem készült.
