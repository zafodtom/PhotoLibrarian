# PhotoLibrarian — stabilní a vývojová vydání

PhotoLibrarian používá jeden GitHub repozitář a dva oddělené aktualizační kanály. Instalátor i ověřovací helper zůstávají společné, data alb jsou mimo instalační adresář.

## Stable (výchozí)

- Stabilní vydání vzniká **výhradně z větve `master`**.
- GitHub Actions → **PhotoLibrarian Windows Build and Release** → **Run workflow** → větev `master` → zaškrtnout `publish_release`.
- Workflow sestaví a otestuje Windows x64 aplikaci, vytvoří Inno Setup instalátor, SHA-256 kontrolní součet a zveřejní standardní GitHub Release `vX.Y.Z`.
- Stabilní Release vyžaduje `docs/releases/X.Y.Z.md`. Staré publikované tagy se nikdy nepřepisují.
- Po úspěšné instalaci nabízí aplikace standardně pouze stabilní aktualizace.

## Development (volitelný)

- Vývojové vydání může vzniknout **z libovolné větve kromě `master`**.
- Po zavedení workflow do `master`: GitHub Actions → **PhotoLibrarian Windows Build and Release** → vybrat vývojovou větev → `publish_development=true` a `publish_release=false`.
- Workflow po úspěšném sestavení a testech zveřejní GitHub **prerelease** s tagem `vX.Y.Z-dev.RUN_NUMBER`. Ten není označen jako nejnovější stabilní vydání.
- Pokud má větev stále verzi posledního stabilního vydání, workflow pro vývojový instalátor automaticky použije následující patch verzi bez přepsání `.csproj`.
- `InformationalVersion` aplikace obsahuje číslo vývojového buildu, proto umí aktualizátor nabídnout další `-dev.N` vydání stejné číselné verze.
- V aplikaci lze ve **Settings → SYSTEM // UPDATE CONTROL** zapnout **INCLUDE DEVELOPMENT UPDATES**. Nastavení se ukládá lokálně. Přepínač je výchozím stavem vypnutý.
- Zapnutý vývojový kanál nabízí nejnovější vhodné stabilní nebo vývojové vydání podle SemVer. Při shodném X.Y.Z má stabilní release vyšší prioritu než prerelease.
- Kontrola aktualizací je stále **výhradně ruční** a instalace vyžaduje dvojí potvrzení.

### Bezpečnost aktualizací

Aktualizátor používá oficiální GitHub Releases API, vyžaduje HTTPS adresu a přesný název instalátoru, kontroluje velikost souboru a jeho SHA-256 digest. Po potvrzení se instalátor stáhne do dočasné složky a update helper ho znovu ověří před spuštěním Inno Setup. Vypnutí vývojového kanálu samo o sobě neodinstaluje ani nedowngraduje vývojovou verzi.

**Doporučení:** vývojové instalátory testovat na kopii alba. Neměnit najednou data všech alb, zejména při testování konverze JPEG, metadat nebo hromadného přesouvání souborů.

## CI a publikace

- `.github/workflows/windows-ci.yml`: standardní sestavení a testy.
- `.github/workflows/windows-installer-release.yml`: sestavení instalátoru, kontroly a ruční publikování stabilního či vývojového vydání.
- `tools/Build-Installer.ps1`: vytvoření instalačního balíčku v Inno Setup.
- `PhotoLibrarian.UpdateHelper`: bezpečná instalace aktualizace po ukončení aplikace.

Stabilní release vždy vychází z ověřené větve `master`; vývojový release může obsahovat nehotové změny a není určený všem uživatelům. Ani jeden kanál automaticky nepublikuje release při běžném pushi.

## Testovací scénáře

1. Nainstalovat poslední stabilní verzi a při vypnutém přepínači zkontrolovat, že se prerelease nenabídne.
2. Zapnout vývojový kanál a ověřit nabídnutí vyššího `-dev.N` vydání.
3. Nainstalovat dev build, ověřit zobrazení jeho tagu v Settings a další aktualizaci na vyšší `-dev.N`.
4. Přepnout zpět na Stable a ověřit, že nedojde k automatickému downgrade.
5. Po vydání finálního `vX.Y.Z` ověřit, že se nabídne i instalované `vX.Y.Z-dev.N`.
6. Ověřit integritu a odmítnutí instalace při změně SHA-256.
