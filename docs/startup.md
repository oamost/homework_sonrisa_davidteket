# World Alerts – helyi indítás (Linux / Windows)

A build a .NET 10 backendet és az Angular web UI-t **egy** Docker-képbe csomagolja. Az alkalmazás kizárólag a helyi `http://127.0.0.1:8080/` címen érhető el, **nem** a régi `https://127.0.0.1:4200/` címen.

## Indítás

Linux/WSL2 (a repo gyökerében):

```bash
chmod +x run.sh
./run.sh up
python3 scripts/smoke.py
```

Windows PowerShell + Docker Desktop (Linux containers):

```powershell
.\run.ps1 up
# Smoke ellenőrzéshez Python 3 szükséges:
python scripts/smoke.py
```

**Admin login: `admin` (jelszó: `admin`).** Az indítószkriptek ezt a sózott PBKDF2 hash-t **automatikusan** átadják Docker környezeti változóként; nincs `init` vagy jelszóbekérés, nem szükséges `.env`-ben adminjelszót megadni. A `.env` hiányában helyben automatikusan létrejön egy üres fájl, a létező `.env` változatlan marad, és a mentett SQLite adatok/kulcsok is megmaradnak. Az esetlegesen régi `.env`-ben szereplő admin hash-t a *helyi indítószkript* felülbírálja az `admin`-hoz tartozó hash-sel; a régi konfigurációs fájlt nem írja felül.

**Biztonság:** az `admin/admin` közismert, gyenge fejlesztői hitelesítő adat. KIZÁRÓLAG helyi demóhoz! Az indítószkriptek `-p 127.0.0.1:8080:8080` bindot használnak. Ne tedd internetre, ne forwardold a portot, és ne használj nyilvánosan elérhető reverse proxyt vagy megosztott gépen ugyanezzel a jelszóval. Publikus telepítéshez saját erős jelszóhash, HTTPS és biztonsági felülvizsgálat szükséges.

## Életciklus

| Linux/WSL2 | Windows PowerShell | Leírás |
| --- | --- | --- |
| `./run.sh up` | `.\run.ps1 up` | új build és tesztek, siker esetén régi *saját* konténer leállítása, friss indítás |
| `./run.sh build` | `.\run.ps1 build` | build+teszt, futó konténert nem állít le |
| `./run.sh start` | `.\run.ps1 start` | az ellenőrzött képpel új konténer |
| `./run.sh stop` | `.\run.ps1 stop` | leállítja a saját konténert és az azonosított régi Angular :4200 szervert |
| `./run.sh restart` | `.\run.ps1 restart` | csak konténercsere; nem buildel újra |
| `./run.sh clean-dev` | `.\run.ps1 clean-dev` | csak a repóhoz tartozó régi :4200 Angular dev szervert állítja le |
| `./run.sh logs` | `.\run.ps1 logs` | konténernapló |
| `./run.sh status` | `.\run.ps1 status` | konténer állapota |

A `stop`, `up` és `restart` **nem törli a Docker-köteteket**. Sikertelen build után az előző futó konténer érintetlen marad. A fejlesztői :4200 porton futó, repóhoz köthető Angular példányokat `stop/start/up/restart` automatikusan leállítja. Ha a 4200-as porton nem azonosítható vagy másik projektből származó folyamat fut, azt **nem** lövi ki; ilyen esetben ellenőrizd az alábbi Linux diagnosztikát:

```bash
sudo ss -ltnp '( sport = :4200 )'
# ha biztosan a saját régi Angular példányod és nem áll le magától:
sudo fuser -k 4200/tcp
```

A `sudo fuser -k` **minden** helyi 4200-as TCP folyamatot érint, ezért ezt csak manuális ellenőrzés után használd. Fontos: leállítás után a régi nyitott böngészőfület zárd be, és az új demót a `http://127.0.0.1:8080/` címen nyisd meg.

## Értesítések és ellenőrzés

Az `http://127.0.0.1:8080/subscriptions` oldalon mentsd el az SMTP 587/STARTTLS szervert, feladó- és címzett címet, az SMTP fiók app-jelszavát, valamint a Slack Incoming Webhook URL-t, és engedélyezd mindkét csatornát. A böngészőben megnyitott Gmail/Slack-fiók önmagában nem elegendő. Mindkét tesztüzenetet **külön** küldd el, majd a főoldali rejtett Debug kapcsolóból a *Send TEST event*-et, és ellenőrizd a tényleges inboxot/Slack-csatornát és az admin queue-t is.

Important News a forrásokból beérkezett, jogosult, fontosságküszöböt elérő valódi tételeket adja vissza. Több RSS és eseményforrás alapján dolgozik; **nincs garancia**, hogy minden kategóriában minden nap ténylegesen lesz fontos hír. A feed legfeljebb 100 tételt ad, fontos kategóriákat lehetőség szerint reprezentálva. Diagnosztika: `http://127.0.0.1:8080/api/news/important` és `./run.sh logs`.

Teljes kézi elfogadási ellenőrzés: `docs/TESTELES_HU.md`. A lokális Docker build és tesztek nem helyettesítik az éles Slack/SMTP kézbesítés ellenőrzését.
