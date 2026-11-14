# PhotoLibrarian

**PhotoLibrarian by Trimenor** is a local Windows photo-album manager built with WinUI 3, .NET and a custom virtualized gallery. It is developed as an independent continuation of the [original PhotoLibrarian project](https://github.com/dunclaw/PhotoLibrarian). The current app focuses on albums, hierarchical tags, file operations and photo metadata rather than cloning Windows Live Photo Gallery.

**Česky:** PhotoLibrarian je lokální správce fotografických alb pro Windows. Umožňuje rychlé procházení velkých alb, třídění a kombinované filtrování podle tagů, úpravy metadat a hromadné operace s fotografiemi. Nevyžaduje cloudové úložiště ani webový server.

![PhotoLibrarian screenshot](docs/screenshot.jpg)

## Installation / Instalace

- **Windows x64:** download the current installer from [GitHub Releases](https://github.com/zafodtom/PhotoLibrarian/releases/latest).
- **Aktualizace:** v aplikaci otevři **Settings → CHECK FOR UPDATES**. Kontrola se provádí ručně; běžné používání alba je offline.
- **Podporovaná platforma:** Windows 10/11, Windows App SDK; build v .NET 8. Instalační soubor není digitálně podepsaný, takže Windows může zobrazit upozornění SmartScreen.
- **Release workflow:** [Build and Release](.github/workflows/windows-installer-release.yml), ručně se zapnutým `publish_release` až po úspěšném CI. Viz [pokyny pro aktualizace](docs/UPDATES.md).

## Main features / Hlavní funkce

| Oblast | Funkce |
|---|---|
| Alba | Lokální alba, podsložky, strom složek, rychlé virtuální náhledy, jednotlivé soubory i více vybraných fotografií |
| Prohlížení | Fotografie a podporovaná videa, režim prohlížeče, přiblížení/posun, návrat na naposledy zobrazenou fotografii |
| Tagy | Hierarchický strom, zahrnutí `+` a vyloučení `−`, kombinace `ALL (AND)` / `ANY (OR)`, vyhledávání a aktuální počty |
| Metadata | Tagy, hodnocení, popis a datum pořízení; zápis do podporovaných obrázků nebo XMP sidecarů a index SQLite |
| Organizace | Výběr a filtrování fotografií, řazení podle názvu a dalších vlastností, kopírování/přesouvání, přejmenování složek, mazání do koše |
| Úpravy | Prohlížeč s ořezem a dostupnými úpravami obrazu; oddělený Win2D editor (některé ovládací prvky jsou aktuálně skryté) |
| Rozpoznávání | Volitelné lokální rozpoznávání osob a automatické tagování přes ONNX; modelové soubory musí uživatel dodat a schválit |
| Hromadné nástroje | Převod podporovaných obrázků alba na JPEG; vložení standardního XMP do JPEG; vyčištění tagů/označení alba se zálohami |

### Data and privacy / Data a soukromí

- Soubory zůstávají ve vybraném lokálním albu. Katalog a náhledová data používají složku `.album` a lokální databázi SQLite.
- Metadata se zapisují přímo do podporovaných souborů prostřednictvím Windows Imaging Component; u některých formátů se používají samostatné XMP sidecary. **Ne každá vlastnost je přenositelná pro každý formát.**
- Automatické tagování je dobrovolné a probíhá lokálně. Podrobnosti a modelové experimenty jsou ve zdrojových souborech `tools/PhotoLibrarian.ModelBench` a v samostatných dokumentech.
- Aktualizátor kontaktuje GitHub pouze při ruční kontrole aktualizací.

### Batch JPEG conversion / Hromadný převod na JPG

V **Settings → ALBUM // JPEG CONVERSION** nejprve spusť **SCAN ALBUM**, zkontroluj počty a pak potvrď **CONVERT TO JPG**.

- Převod zahrnuje podporované obrázky v podsložkách aktuálního alba, ale ne v jiných vnořených albech. RAW, video a nepodporované formáty se nepřevádějí.
- Standardní XMP sidecary se vloží do JPEG, pokud se jejich obsah vejde do standardního segmentu; neúspěšné soubory zůstanou zachovány.
- Originály a sidecary se pro obnovení ukládají do `.album/jpeg-conversion-backups/`.
- **Pozor:** převod ne-JPEG obrázků je ztrátový a některá původní vložená metadata nemusí být zachována. Před převodem velkého alba použij kopii a ověř namátkově výsledek.

### Clear album tags / Vyčištění alba

V **Settings → ALBUM // RESET TAGS** můžeš po potvrzení vyčistit tagy, uživatelská označení a katalog tagů. Fotografie a ostatní běžná metadata (například hodnocení či popisky) mají zůstat zachována. Soubory se před změnou zálohují do `.album/metadata-reset-backups/`; problematické/nepodporované soubory se hlásí. Automatické tagování se vypne, aby nevracelo odstraněné tagy.

Tyto hromadné operace zatím používej až po záloze a ideálně nejprve na testovacím albu.

## Development / Vývoj

```powershell
git clone https://github.com/zafodtom/PhotoLibrarian.git
cd PhotoLibrarian
dotnet build src/PhotoLibrarian/PhotoLibrarian.csproj -c Release
dotnet test src/PhotoLibrarian.Tests/PhotoLibrarian.Tests.csproj -c Release
```

Pro vývoj je vhodné Visual Studio 2022 s Windows App SDK a .NET 8. Řešení obsahuje:

- `src/PhotoLibrarian`: WinUI 3 pohledy, navigace, editor, aplikace.
- `src/PhotoLibrarian.Core`: indexace, metadata, datové služby a SQLite.
- `src/PhotoLibrarian.ML`: lokální ONNX rozpoznávání a tagování.
- `src/PhotoLibrarian.Tests`: automatické testy.
- `tools/PhotoLibrarian.ModelBench`: samostatný experimentální nástroj pro srovnávání modelů (není to hlavní uživatelské rozhraní).

Vývojové buildy pro Windows a kontroly MSIX/instalátoru běží v GitHub Actions. Stable release je samostatná vědomá publikace, nikoli automatický výsledek každého commitu.

## Existing but hidden functionality / Skryté a nepoužívané části

Aplikace stále obsahuje některé historické nebo skryté funkce. **Nejsou automaticky určené k odstranění.** Konkrétní seznam s odkazy na zdrojové soubory, riziky a doporučeným rozhodnutím je v [auditu funkcí](docs/FEATURE-AUDIT.md).

## License, origin and acknowledgements / Licence a původ

PhotoLibrarian is a fork and ongoing independent development of the [original project by Duncan Lawler](https://github.com/dunclaw/PhotoLibrarian). The original **MIT copyright and license notice** is preserved in [LICENSE](LICENSE); Trimenor branding does not replace or invalidate that attribution.

PhotoLibrarian uses third-party libraries such as Windows App SDK/WinUI 3, Win2D, MetadataExtractor, XmpCore, CommunityToolkit.Mvvm and ONNX Runtime. See project/package metadata and upstream licenses for their respective terms.
