# Átadás előtti tesztelés (HU, kb. 10–20 perc + build)

**Előfeltétel:** működő Docker, Python 3, élő internet, saját SMTP-hozzáférés (587/STARTTLS, app-jelszó), Slack Incoming Webhook. A valós értesítést kizárólag a **saját címre és csatornára** küldd; a jelszót/webhookot ne oszd meg.

1. **Indítás:** a kicsomagolt repó gyökeréből `chmod +x run.sh && ./run.sh up` (az `.env` automatikusan létrejön, nincs jelszóbekérés). **Elvárt:** sikeres `npm test`, `ng build`, `dotnet build`, `dotnet test`, Docker image és futó konténer; ha hibás teszt vagy build, **STOP: nem kész a szállításra**.
2. **Gyors ellenőrzés:** `python3 scripts/smoke.py` (más port esetén `python3 scripts/smoke.py http://127.0.0.1:8090`). **Elvárt:** minden sor `OK`; HTTP 200 az egészségnél, a 3 SPA útvonalon és az Important Newsnál; admin API bejelentkezés nélkül 401. `./run.sh logs` nem mutat kritikus hibát.
3. **Important News:** nyisd meg `http://127.0.0.1:8080/`. **Elvárt:** automatikus frissítés; nem kell kézzel refresh. Ellenőrizd `/api/news/important` JSON-ban az `items`, `familyDiagnostics`, `limitations` mezőket. A News, Disaster és Market családokból, illetve több eseménytípusból csak akkor jelenhet meg tétel, **ha a forrásokban van valóban fontos és jogosult esemény**. A 0 elem önmagában nem bizonyít hibát; nézd az `evaluatedCount` és `importantEligibleCount` számlálókat. Tömegnyi hírtétel sem szoríthatja ki a létező fontos katasztrófa/piaci eseményeket.
4. **Eseménytípusok:** `/subscriptions` → kezdetben minden jóváhagyott (24) típus aktív. Kapcsold ki pl. a `world-news` taget → **Save event types**, töltsd újra az oldalt. **Elvárt:** a kikapcsolás megmarad. Kapcsold vissza és ments, mert a későbbi TEST esemény `world-news` típusú.
5. **Admin:** `/admin` → jelentkezz be `admin` / `admin` adatokkal (kizárólag helyi demó). **Elvárt:** források, queue/failed és 30 másodperces automatikus státuszfrissítés. Másik privát ablakban `/api/admin/status` → 401. Téves jelszó nem enged be. Admin szervernézet: `/admin/server`.
6. **Email:** bejelentkezve, `/subscriptions` → SMTP: szolgáltatói host, **587 STARTTLS**, felhasználó/app-jelszó, feladó, saját címzett. Engedélyezés + **Save**; külön **Send test email**. **Elvárt:** a válasz SMTP-elfogadást jelez, **és** a levél ténylegesen megérkezik a saját Inbox/Spam mappába. Rossz jelszó esetén hiba és nincs hamis „siker”.
7. **Slack:** Slack workspace → Incoming Webhooks bekapcsolása + webhook telepítése a kívánt csatornába. `/subscriptions` → admin Slack: webhook URL, csatornacímke, engedélyezés + **Save**, majd külön **Send test Slack notification**. **Elvárt:** megérkezik a tényleges Slack-csatornába. Hibás webhooknál jól látható hiba.
8. **Automatikus végponttól végpontig:** a főoldali apró **Debug** kapcsoló → **Send TEST event** (mindkét mentett csatorna aktív). **Elvárt:** teszthír látszik az Important Newsban, és a háttérben queue-ba kerül, majd kézbesül a saját email és Slack célra. Kövesd a `/admin` delivery státuszt és `./run.sh logs` kimenetét. A tesztgomb védett korláttal működik: rövid időn belüli ismétlés 429-et adhat; várj, ne nyomkodd folyamatosan. A kikapcsolt `world-news` tag szándékosan megakadályozhatja az üzenetet.
9. **Kiegészítő csatorna:** opcionálisan adj hozzá saját HTTPS POST/PUT webhookot és teszteld az egyedi csatornán. A célplatform elvárt payload-formátuma külön beállítást igényelhet.
10. **Leállítás és adatmegőrzés:** `./run.sh stop` → `./run.sh status` nem futó; `./run.sh start` → ismét működik. **Elvárt:** megmaradnak a tag- és értesítési beállítások. `.env` módosítása után `./run.sh restart`, kód módosítása után `./run.sh up` (újrabuild). **NE** töröld a Docker-köteteket.

**Szállítási döntés:** csak akkor állítsd, hogy az email/Slack „élőben tesztelve”, ha a 6–8. lépésben **valóban** megérkeztek az üzenetek. Külső provider-eredetű hiány, korlát vagy kapcsolati probléma nem egyenlő a választóalgoritmus hibájával. Hibakor: `./run.sh logs`, majd a hibasorok alapján javítás és `./run.sh up`.

## 2026-10-21 végső lokális indítás

1. `./run.sh clean-dev` (csak a repó saját, Dockerön kívüli Angular :4200 példányát állítja le), majd `./run.sh up`. Windows: `.\run.ps1 clean-dev; .\run.ps1 up`. A `stop` és `up` is elvégzi a régi dev-példány keresését.
2. **Kizárólag** `http://127.0.0.1:8080/admin` és az **`admin` / `admin`** jelszó használatos a Docker demóban. `https://127.0.0.1:4200/` régi fejlesztői példány; ne ott tesztelj!
3. Hírek: `http://127.0.0.1:8080/api/news/important`. Ellenőrizd a `publishedUtc` időpontokat és a `limitations`/`familyDiagnostics` mezőket. A 2026-10-21-i hírek jelenléte az élő RSS-forrásoktól függ, nem szabad hamis találatokat megjeleníteni.
4. Slack+SMTP: `/subscriptions` → mentés+engedélyezés → külön test send mindkettőre → inbox/Slack ellenőrzés → főoldal Debug → TEST event → queue ellenőrzése.
5. A `scripts/smoke.py` **nem küld** valódi emailt/Slack üzenetet, ezért önmagában nem igazolja a kézbesítést.

**Biztonsági figyelmeztetés:** ez az előre ismert `admin` jelszó kizárólag 127.0.0.1-re bindolt, lokális demóra szolgál. Semmilyen formában ne tedd publikusan elérhetővé!
