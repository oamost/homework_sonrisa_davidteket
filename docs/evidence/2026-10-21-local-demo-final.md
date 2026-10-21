# Helyi demó: admin és régi Angular szerver kezelése (2026-10-21)

Felhasználói visszajelzés: a `https://127.0.0.1:4200/` dev szerver tovább futott Dockerön kívül; a jelszóval kapcsolatos interaktív inicializálás blokkolta a tesztelést. Kérés: ismert `admin` lokális jelszó, `run.sh` és `run.ps1` jelszókérdés nélkül, régi repó-példányok leállítása.

## Döntések

- Az `admin` jelszó *nem plaintext konfigurációként* kerül továbbításra, hanem előre elkészített, random sóval készült PBKDF2-SHA256 hash formájában. A két indítószkript azonos hash-t ad át Docker `-e` környezeti változóként, felülírva az `.env` esetleges korábbi helyi admin hash-ét. Az alkalmazás általános Production admin logikáját nem gyengítettük; Docker lokális `127.0.0.1` bindot használ.
- Nincs több `init` / `admin-password` kérés a run parancsokban. Hiányzó alapértelmezett `.env` fájl 0600 joggal létrejön Linuxon, Windows alatt üres fájlként. Meglévő privát adatok változatlanul megmaradnak.
- A Dockerön kívüli Angular szerver PID-jének ellenőrzése *projektútvonalhoz* és Angular/Vite parancssorhoz kötött; más :4200 portot foglaló szolgáltatást nem ölünk meg. Linuxon `/proc` socket inode+PID+cwd+cmdline, Windowson TCP-listener + WMI parancssor. `clean-dev`, `stop`, `start`, `up`, `restart` végrehajtja a takarítást.
- Build továbbra is a régi konténer leállítása előtt történik, így sikertelen build nem rombolja a korábbi működő példányt. Docker-kötetek nem törlődnek.

## Valódi ellenőrzés korlátai

A kapott korábbi Docker-napló szerint a frontend és backend build sikeresen végigfutott, de a jelen javításhoz a futtatási környezet nem biztosít Docker/.NET/PowerShell binárist, sem privát Slack vagy SMTP hitelesítő adatot. Ezért a **friss** end-to-end build, a Windows natív indulás és a valós email/Slack kézbesítés **csak a felhasználó gépén igazolható**. A `scripts/smoke.py` nem küld valódi üzenetet. A mai hírek nem generálhatók mesterségesen, a források és importance küszöbök döntenek.

## Vállalt kockázat

`admin/admin` nyilvános hitelesítő adat; kizárólag izolált helyi próbára megengedhető. Port forward / reverse proxy / publikált deploy tilos ezzel a konfigurációval. Éles telepítés előtt erős adminjelszó kell.
