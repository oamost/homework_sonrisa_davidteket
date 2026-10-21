# 2026-10-21 – Webes ChatGPT agentic kreditlimit utáni átadás-felkészítés

## Feladat és döntések

A tulajdonos kifejezetten kéri a teljes működő helyi alkalmazást: egy `run.sh` entrypointot build/start/stop műveletekre; SMTP és Slack tesztelhető integrációját; az összes ténylegesen elérhető fontossá vált eseménytípusra igazságos Important News nézetet; a prompt history megőrzését; és egy magyar próbalistát. A feladatkiírás szerint az AI-output kritikus ellenőrzése, a döntések és a tesztbizonyítékok **a leadandó termék részei**, nem puszta utólagos díszlet.

A meglévő architektúrát megtartottuk (ASP.NET Core 10 API + Angular + SQLite + aszinkron durable queue + SMTP/Slack/HTTPS webhook), nem épült párhuzamos küldőszolgáltatás. A böngészős Slack-/email-belépés nem alkalmas a rendszer integrációjára: SMTP app-jelszó és Slack Incoming Webhook szükséges. A megfelelő szerveroldali beállítások és send-test végpontok már léteztek; nem állítottuk át őket bizonyíték nélkül.

### Ténylegesen javított / kiterjesztett területek

- **Task 26 indíthatóság:** a korábbi Docker kizárólag backend API-t tartalmazott; a `Dockerfile` most Node 24 build stage-ben teszteli és buildeli az Angulart, és statikusan kiszolgált fájlokat visz az ASP.NET runtime-ba. A `run.sh` külön `init/build/start/stop/restart/status/logs/up` parancsot kapott. A meglévő futó példányt sikertelen build nem állítja le, adatkötetek maradnak; előre létező, idegen Docker név vagy kötet címke nélkül elutasított.
- **Local admin usability:** az éles környezetből átvett `__Host-`/Secure cookie mód tiszta HTTP localhost esetén böngészőfüggően elutasítható. A csak explicit helyi indítóhoz kötött `WorldAlerts:LocalHttp` mód alacsonyabb sütiküldési korlátozást enged ugyanazon 127.0.0.1 címen, de HttpOnly/SameSite Strict és antiforgery marad; normál HTTPS default nem változott. A `scripts/init_env.py` a production admin számára csupán jelszó-HASH-t generál, nyers titkot nem tárol.
- **Important News (selection):** a korábbi egyetlen 100 elemre korlátozott hírlista egy zajos RSS-kategóriával kiszoríthatott katasztrófa- és piacmozgásokat. Az új rangsor először a **ténylegesen elérhető** jóváhagyott eseménytípusok, utána a családok lefedését biztosítja, majd pontszámmal tölti fel a keretet. Nincs mesterséges tétel vagy valótlan lefedettségi ígéret. Regressziós .NET teszt hozzáadva.
- **Admin operációs felület:** korábban csak egyszer olvasott státuszt. Most 30 másodpercenként újra lekérdez, látható stale állapottal és utolsó sikeres frissítési idővel. Kijavított prefetch előtt kapcsolható eseménytípus-tag hiba.
- **Használati és tesztbizonyítékok:** `scripts/smoke.py` read-only health/API/UI smoke, `docs/startup.md`, `docs/TESTELES_HU.md`, valamint a verbatim promptnaplóhoz új tulajdonosi promptok.

## Ellenőrzések és megkérdőjelezett feltételezések

- **Sikeres a jelen környezetben:** `node ./node_modules/@angular/cli/bin/ng.js test --watch=false`: **40/40 pass**; `ng build`: sikeres production build; `python3 -m unittest discover -s src/tests -p 'Startup*.py' -v`: **11/11 pass** a szimulált Dockerrel; `bash -n run.sh`; `git diff --check`; Python szintaktikai ellenőrzések. Node a sandboxban 22.16 (a projekt Angular CLI követelménye ennél frissebb), így a helyi figyelmeztetést **csak az ignored node_modules könyvtárban** kerültük meg a tesztekhez; a szállított Dockerfile Node 24-et használ. A node_modules nincs a leadott archívumban.
- **Nem ellenőrizhető itt:** Docker Engine, .NET SDK, élő SMTP és Slack saját fiókok / webhookok hiányoznak. Emiatt a .NET tesztet és a Docker image buildet **itt nem futtattuk**; az új C# teszteket és a valódi end-to-end kézbesítést a tulajdonos `./run.sh build` és a rövid tesztleírás szerint ellenőrzi. A csatornaszolgáltatók és a forráshírek teljes világméretű lefedettsége nem bizonyított.
- **Biztonság:** a forrásban található helyi SQLite DB, `*.sqlite.keys/`, titkosított settings, `.env*`, generált bin/obj/dist és node_modules szándékosan **nincs** a csomagolt átadásban. Git történet megőrizve; új commitot nem írtunk előzetes felhasználói ellenőrzés nélkül (AGENTS.md szabály).

## Nyitott átadás előtti kapuk

A helyi Docker `.NET 10` és frontend teljes build + teszt sikere; admin HTTP-cookie belépés; SMTP app-jelszavas **valós inbox** és Slack Incoming Webhook **valós csatorna** megérkezése; TEST esemény mindkét kézbesítési útja; források adat- és limiter-állapota. Ezeket sem a sikeres frontend tesztek, sem a generált dokumentáció nem helyettesítik.

## Utólagos javítások – hibajelentés 2026-10-21, 13 óra körül

A felhasználó valós Docker-buildje **23/23 sikeres stage-et** mutatott (Node 24 + .NET SDK 10 + Angular tesztek). Ezt követően az admin `init` nyílt hibaüzenettel elutasította a megadott jelszópárt (nem történt konfigurációs fájl létrehozás), majd a `start` leállt hiányzó `.env` miatt. A build sikere **nem** jelenti, hogy a bejelentkezés vagy a küldés működik. Új `admin-password` parancs biztonságosan megőrzi a meglévő `.env` más kulcsait és újraszámolja a sózott admin hash-t (visszavonható jelszóvesztés nélkül). Python-regresszió: 3/3 pass.

A `src/api/Queue/AlertPipeline.cs` és `src/api/Queue/TestEventService.cs` korábbi ága minden automatikus privát email/Slack csatornát elnyomhatott, ha létezett akár egy más kategóriájú legacy feliratkozás, és a felhasználó még nem mentette el a típusválasztót. Ez hibás volt: a privát csatorna explicit engedélyezése már opt-in. Javítva, a konkrét azonos csatornás/típusú legacy tiltás továbbra is elsőbbséget élvez. Két célzott C#-regressziós eset hozzáadva. **A C# tesztek e környezetben .NET hiányában nem futtathatók.**

A feed kezdőoldala korábban egyszerre csak 5 valódi hírcikket jelenített meg, az összes fontos kategóriának ez rossz láthatóságot adott. Ez a kliensoldali lapozás eltávolítva; a szerveroldali fontosnak minősített összes (max. 100) cikk látszik. Mai (helyi dátumú) hír-darabszám megjelenik; soha nem állítjuk, hogy minden típusból *szükségszerűen* keletkezik fontos hír. A jelenlegi 24 órás provider-idő és fontosság-értékelés nem hamisítható adatot szolgáltat. Friss BBC RSS feedekkel bővült a témalefedettség. Angular teszt: 40/40 pass.

Az `./run.sh start` és a `.\\run.ps1 start` már futó, címkéjével sajátként igazolt konténert **előbb leállítja és eltávolítja**, majd új, az aktuális `.env`-et és image-et használó konténert hoz létre a megőrzött köteteken. A `build` továbbra sem bánt működő rendszert sikertelen build esetén. `run.ps1` külön `build/start/stop/restart/status/logs/init/admin-password/up` parancsokat kapott, natív Windows-próba nem volt. Idegen konténer nem állítható le automatikusan.

**Nyitott ellenőrzési kapuk:** a tulajdonos futtassa `./run.sh admin-password && ./run.sh up`; login külön privát ablakban; Important News mai fontos tételek és gyűjtőlog; mentett SMTP és Slack külön tesztküldés; majd TEST esemény queue→valós beérkezés mindkét csatornán. Provider-fiók és saját futtató Docker hozzáférés híján az élő kézbesítés **nem igazolt**. A fejlesztőnek őszintén kell jeleznie az esetleges `./run.sh build` regressziót, amely C# test fordításból adódhat.
