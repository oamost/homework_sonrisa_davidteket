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

## 2026-10-21: aktuális helyi SMTP + Slack és friss hírek (kiegészítés)

**Az admin 0 queued / 0 failed értéke nem hiba.** Azt jelenti, hogy akkor nincs sorban álló vagy végleg hibás/„uncertain” küldés. Az új **Sent** mező a fogadó által elfogadott küldések számát mutatja (nem kézbesítési vagy olvasási visszaigazolás).

**Gmail példa:** `/subscriptions` → Private email delivery setup:
- SMTP host: `smtp.gmail.com`; port: `587` (STARTTLS)
- Username és From address: `januspannonius1011@gmail.com`
- Password: a Google-fiókhoz létrehozott **alkalmazásjelszó**, nem a normál bejelentkezési jelszó. Google-fiók > Biztonság > Kétlépcsős azonosítás (ha szükséges), majd https://myaccount.google.com/apppasswords. A lehetőség fióktípustól / biztonsági beállításoktól függhet.
- Send alerts to: `januspannonius1011@gmail.com`, Enable automatic email delivery: BE → **Save SMTP settings**.
- Test destination: `januspannonius1011@gmail.com` → **Send test email**. Nézd meg az Inbox, Spam és Összes levél mappát is. Ha 502 jön vissza, először a hostot, 587-es portot, app-jelszót, a kiszolgáló internetkapcsolatát és a STARTTLS-kapcsolatot ellenőrizd.
- Soha ne másold a titkos jelszót chatbe, commitba, konzolnaplóba vagy képernyőképre.

**Slack:** https://api.slack.com/apps → Create New App → From scratch → válassz saját workspace-t → Incoming Webhooks → Activate → Add New Webhook to Workspace → célcsatorna kiválasztása → Allow. A kapott `https://hooks.slack.com/services/...` **titok**, csak az alkalmazás `/subscriptions` → Private Slack delivery setup mezőjébe másold. A Channel label pl. `#alerts` csak megjelenítési felirat; a tényleges célcsatornát a Slack-webhook létrehozásakor választottad ki. Kapcsold be az automatikus értesítést → Save Slack settings → jelöld be a kifejezett tesztküldési hozzájárulást → Send one test to Slack. Keresd az üzenetet **abban a Slack csatornában** (nem szükségszerűen privát DM-ben).

**A teljes lánc tesztje:** a fenti két önálló teszt sikeres fogadása után `http://127.0.0.1:8080/subscriptions` → World news tag legyen kiválasztva és mentve. A főoldalon Debug → **Send TEST event**. Az új patch azonnal megkísérli a kizárólag tesztüzenetek kézbesítését; a `/admin` Sent számlálójának növekednie kell (30 mp-enként frissül), majd meg kell érkeznie egy TEST üzenetnek a Gmailbe és a Slack csatornába is. Ha Sent nem nő és Queued nő, küldési hiba történt; szükség szerint egy perc múlva háttérben újrapróbálja. A védett gombot legfeljebb percenként egyszer használd.

**Hírek:** `/api/news/important` csak a 24 órán belül jogosult, explicit fontossági szabályokon átment eseményeket jelöli Importantnak. A fontos hírek között nem jelenhet meg duplikált cím. Az új **Latest published reports** rész 20 valódi, maximum 24 órás, forrásidőbélyeggel rendelkező hírt mutat, amelyek lehetnek *nem Important* cikkek; ezek automatikus értesítést nem indítanak. A hiányzó piaci vagy katasztrófaesemény nem helyettesíthető kitalált adattal. Az API JSON-ban a `familyDiagnostics` mezőből ellenőrizhető, hogy melyik családot miért nem jelölték Importantnak. A háttérforrások elérhetősége nem garantált, a friss hírszekció sem garantál minden kategóriában mai fontos eseményt.

**Futtatás:** patch másolása után `./run.sh up`, majd `python3 scripts/smoke.py`. 4200-as régi dev instance esetén `./run.sh clean-dev` vagy ellenőrzött `sudo fuser -k 4200/tcp`. A 8080-as helyi portot használd. A patch meglévő lokális `.env` és védett beállításait meg kell tartani. Éles Gmail/Slack kézbesítést csak a fiók tulajdonosa tudja végponttól végpontig igazolni.
