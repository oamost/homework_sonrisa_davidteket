# World Alerts — átadási összefoglaló (handoff)

**Átadás dátuma:** 2026. október 21.  
**Projekt:** Sonrisa homework / World Alerts  
**Állapot:** működő, lokális Docker-demó; a külső értesítési csatornák éles kézbesítésének végponttól végpontig történő igazolása és a legutolsó regressziós javítás teljes újrafuttatása még hátravan.

## 1. Kiinduló feladat és megközelítés

Az eredeti, szándékosan hiányos brief fontos világeseményekről (hírek, piaci mozgások, természeti katasztrófák) küldött **email- és Slack-értesítéseket**, később bővíthető csatornákat és **adminfelületet** kért. A feladat az AI-eszközökkel végzett **tervezést, döntéseket, hibafelismerést, ellenőrzést és korrekciót** hangsúlyozza, nem kizárólag a végső kódot.

A megvalósításhoz választott, dokumentált értelmezés:

- Helyben futó webalkalmazás, külön felhőhoszting nélkül; Dockeres indítás Linuxon és Windowson.
- **ASP.NET Core / .NET 10** backend, **Angular** frontend, **SQLite** perzisztencia.
- Több nyilvános hír-, katasztrófa- és piaci adatforrás; forrásonkénti állapotkövetés, eseménytípusok, fontossági szabályok és duplikátumszűrés.
- Felhasználói eseménytípus-választás; email (SMTP/STARTTLS), Slack Incoming Webhook és bővíthető HTTPS-webhook kézbesítés.
- Adminfelület a források lekérdezési/egészségi állapotának és a kézbesítési sor számlálóinak áttekintésére.
- 24 órás eseményablakra épülő feldolgozás és induláskori felzárkóztatási logika; az adatforrások tényleges lefedettsége nem garantált.

## 2. Fő funkciók és kezelőfelületek

| Funkció | Elérési út / feladat |
| --- | --- |
| Important World Events | `http://127.0.0.1:8080/` — fontossági szabályon átment események, a legutóbbi frissítésekben külön friss, még nem feltétlenül fontos forráshírek |
| Értesítések és eseménytípusok | `/subscriptions` — tagválasztás, SMTP-, Slack- és webhook-beállítások, önálló tesztküldések |
| Admin | `/admin` — belépés, forrás- és kézbesítési állapotok, számlálók |
| API állapot | `/api/health` — sikeres futáskor `{"status":"ok"}` |
| Important News API | `/api/news/important` — tételek és forrás-/kategóriadiagnosztika |
| Esemény tesztelése | Főoldal → **Debug → Send TEST event** — az értesítési lánc próbája |

**Fontos értelmezési korlát:** a „fontos” besorolást szabályok adják, nem pusztán az, hogy egy cikk ma jelent meg. Ha az adott forrás nem szolgáltat megfelelő friss/kiemelt eseményt, egy kategória üres is lehet. Nem generálunk kitalált híreket a kategóriák feltöltésére. Piaci adatoknál egyes források késleltetettek vagy szűk földrajzi/instrumentum-lefedettségűek; a természeti veszélyek globális és típusonkénti lefedettsége sem teljes körűen igazolt.

## 3. Telepítés és futtatás

**Előfeltételek:** Docker Engine/Docker Desktop, internetelérés; Linux alatt Bash és Python 3 a smoke-teszthez. A teljes alkalmazás buildjét Docker végzi; nem szükséges előre feltelepített helyi `node_modules` vagy .NET buildkimenet.

Linux / WSL a repó gyökeréből:

```bash
chmod +x run.sh
./run.sh up                # build + tesztek + korábbi saját példány leállítása/cseréje + indítás
python3 scripts/smoke.py  # API és webes útvonalak gyors ellenőrzése
./run.sh status
./run.sh logs
./run.sh stop
./run.sh start             # meglévő image indítása új build nélkül
```

Windows PowerShell (Docker Desktop mellett):

```powershell
.\run.ps1 up
.\run.ps1 status
.\run.ps1 logs
.\run.ps1 stop
```

A szkriptek megpróbálják leállítani a **saját projekthez bizonyíthatóan tartozó**, korábban Dockerön kívül indított, `:4200`-as Angular-fejlesztői szervert (`clean-dev`), és megállítják/cserélik a saját Docker-példányt. Az ismeretlen eredetű folyamatokat szándékosan nem lövik ki. A helyi adatokat a Docker-kötetek megőrzik.

**Helyi demó adminbelépés:** `admin` / `admin`. Ez **nem éles környezetbe való**; az alkalmazást a szkript csak `127.0.0.1:8080` címen teszi elérhetővé. Ne publikáld a portot és ne használd ezt a jelszót interneten elérhető szerveren.

## 4. Email és Slack beüzemelése

Az értesítési szolgáltatások **nincsenek a forráskódhoz mellékelt titkokkal előkonfigurálva**. A saját hitelesítő adatok megadása szükséges, ezért a működő UI/build nem bizonyítja önmagában a külső kézbesítést.

**Gmail/SMTP:** `/subscriptions` → Private email delivery setup; SMTP host `smtp.gmail.com`, port `587` (STARTTLS), username/feladó a saját Gmail-cím, jelszóként megfelelő Google **alkalmazásjelszó**, címzettként a kívánt cím; automatikus kézbesítés engedélyezése → **Save SMTP settings** → **Send test email**. A valódi beérkezést az Inbox és Spam mappában kell ellenőrizni.

**Slack:** Slack App Management → új app → Incoming Webhooks engedélyezése → a kívánt workspace/csatorna számára webhook létrehozása. Az így kapott **titkos** `https://hooks.slack.com/services/...` URL megadása a `/subscriptions` → Private Slack delivery setup részben → engedélyezés és mentés → tesztküldés jóváhagyása → **Send one test to Slack**. A kiválasztott Slack-csatornában kell ellenőrizni az üzenetet. Egy böngészőben bejelentkezett Slack-fiók **önmagában nem** helyettesíti a webhookot.

A két külön csatornateszt után a főoldalon **Debug → Send TEST event** teszteli a közös, automatikus utat. A World news eseménytípus legyen engedélyezve. Az adminoldal **Queued / Sent / Failed** számlálói és a `./run.sh logs` segítik a diagnosztikát. A **Sent** a küldési cél általi elfogadást jelzi, nem feltétlenül a címzett által történő olvasást.

**Biztonság:** `.env`, SQLite-adatbázis, védett kulcsok, alkalmazásjelszó és Slack-webhook **ne kerüljön** a beküldött archívumba vagy Git-commitba.

## 5. Validáció — igazolt és nyitott tételek

**A 2026. október 21-i felhasználói próbákból igazolt:**

- Docker alatt a .NET és az Angular fordítás/becsomagolás sikeresen lefutott legalább egy korábbi hotfix után; a szerver később elindult.
- `GET http://127.0.0.1:8080/api/health` HTTP **200 OK** választ és `{"status":"ok"}` törzset adott.
- A felület elérhető, az **admin `admin`/`admin` belépés és kijelentkezés működött**.
- A forrásstátusz-felület valós lekérdezéseket mutatott (több egészséges forrással és legalább egy stale állapottal).
- A korábbi news-feed képernyőképen kis számú, részben előző napi esemény és duplikáció jelent meg; ez motiválta a későbbi szűrési és frisshír-felületi korrekciókat.

**Legutóbbi javítások, amelyek teljes újraellenőrzése még szükséges:**

- Fontos hírek duplikációszűrése és „Latest published reports” felület.
- A tesztesemény email/Slack-kézbesítésének azonnali próbája, régi feliratkozásokkal kapcsolatos regressziós javítás, admin **Sent** számláló.
- A legfrissebb buildhiba: `ImportantNewsEndpointTests.Response_reports_important_stories_omitted_after_display_limit`; a korábbi teszt 105 azonos címet adott a deduplikáló algoritmusnak, miközben 100 különböző megjelenített hírt várt. Az utolsó hotfix különböző címekkel ellenőrzi a 100-as limitet, és külön tesztet ad a deduplikációhoz. **Az utolsó hotfix utáni teljes Docker/.NET tesztfuttatás eredménye még nincs visszaigazolva.**
- A valódi SMTP- és Slack-kézbesítés, a főoldali TEST event mindkét csatornára történő megérkezése és a **natív Windows PowerShell-indítás** nem rendelkezik végső, visszaigazolt élő teszteredménnyel.

A `python3 scripts/smoke.py` **nem küld valódi emailt vagy Slack-üzenetet**, és önmagában nem helyettesíti az élő küldési teszteket. Az első induláskor az API késleltetve válhat elérhetővé; a friss smoke-szkript rövid ideig vár a készenlétre.

## 6. Dokumentáció, AI-irányítás és döntési nyomvonal

Az értékelés szempontjából fontos anyagok **a repó részei**:

- `prompts.md` — prompttörténet, benne a **webes ChatGPT folytatás az agentic kreditlimit elérése után** megjelöléssel.
- `ctx/goals/plan.md` és `ctx/goals/tasks.md` — célok, munkaterv, teendők/állapotok.
- `docs/product-concept.md`, `docs/requirement-coverage.md`, `docs/alert-requirement-assessment.md` — briefértelmezés, követelmények és lehatárolás.
- `docs/decisions/` — fontossági szabályok, forrásminőség, lefedettségi rések, deduplikáció, döntések indoklása.
- `docs/evidence/` — tesztnaplók, futási nyomok, helyesbítések és felismert korlátok.
- `docs/superpowers/plans/` és `docs/superpowers/specs/` — köztes tervek és tervezési anyagok.
- `docs/TESTELES_HU.md`, `docs/startup.md`, `docs/alert-setup.md`, `docs/email-setup.md` — részletesebb működtetési és tesztelési leírások.
- A Git-történet a fejlesztési mérföldkövekhez kapcsolódó commitokat tartalmazza; **a legutolsó fájlmódosítások csak akkor jelennek meg ebben a történetben, ha leadás előtt commitolásra kerülnek**.

A munka során külön dokumentált korrekciók történtek például dátumfüggő katasztrófa-ingesztiós tesztek, adminjelszó-bootstrapping, korábbi dev-szerverek kezelése, fontos hírek rangsorolása és deduplikálása, valamint értesítési tesztfolyamatok terén. A meg nem erősített szolgáltatói lefedettséget és külső kézbesítést nem állítjuk ellenőrzöttnek.

## 7. Átvevői gyors ellenőrzőlista

1. Forrásból `./run.sh up` (vagy PowerShellen `.\run.ps1 up`); a **build és az összes teszt** legyen sikeres.
2. `python3 scripts/smoke.py`; ellenőrizd az `/api/health` és az API/UI-válaszokat.
3. Bejelentkezés `/admin` címen helyi demóadatokkal; forrás- és kézbesítési státuszok ellenőrzése.
4. Főoldal: Important News, dátumok, duplikációk, új frisshír-szekció, hiányzó kategóriák diagnosztikája.
5. SMTP mentése és **valóban megérkezett** tesztemail ellenőrzése.
6. Slack webhook mentése és **valóban megérkezett** tesztüzenet ellenőrzése.
7. Debug → TEST event, majd **mindkét csatorna**, valamint Queued/Sent/Failed ellenőrzése.
8. `./run.sh stop`, majd `./run.sh start`: újraindítás és mentett konfigurációk megőrzése.

**Összegzés:** a projekt bemutat egy AI-val tervezett és iteratívan tesztelt, forrásalapú, lokális világesemény-figyelő/értesítő rendszert. A fenti nyitott pontok tudatosan szerepelnek az átadásban: a kész kód és az élesben bizonyított működés nem ugyanaz.
