# PhotoLibrarian — audit skrytých, nevyužívaných a vývojových funkcí

Stav zdrojového kódu: listopad 2026. Tento dokument **není seznam věcí k okamžitému smazání**. Označení „skryté“ znamená, že odpovídající prvek je ve XAML explicitně `Visibility="Collapsed"` nebo událost není běžně dostupná. Není to důkaz, že je nepoužívaná ve všech režimech.

| Funkce / oblast | Kde je vidět v kódu | Aktuální dostupnost | Doporučení k rozhodnutí |
|---|---|---|---|
| „Make a copy“ v horní liště | `Views/TopRibbon.xaml` → `MakeCopyButton`; `Services/PhotoOperationsService.cs` → `MakeCopyForEditingAsync` | Tlačítko je výslovně skryté; metoda je připojená | **Ponechat logiku**: užitečná pro bezpečné úpravy; rozhodnout o vrácení tlačítka do UI |
| Narovnání snímku | `Views/TopRibbon.xaml` → `StraightenButton`; `Views/ImageViewerOverlay.xaml.cs` | Tlačítko skryté; existuje režim s vodicími čarami | **Ověřit prakticky**, pak buď zpřístupnit, nebo odložit |
| Otevření editoru úprav | `Views/TopRibbon.xaml` → `AdjustButton`; `Views/ImageEditorView.xaml(.cs)`; `Services/EditEffectGraph.cs` | Tlačítko skryté; editor a efekty existují | **Ponechat**, jde o rozsáhlý modul; nerozhodovat jen podle skrytého tlačítka |
| Korekce červených očí | `Views/TopRibbon.xaml` → `RedEyeButton`; `Views/ImageViewerOverlay.xaml.cs` | Tlačítko skryté; kód režimu a zpracování přítomný | **Kandidát k deaktivaci/odstranění**, pokud ho nechceme zpřístupnit |
| Označení/flagování obrázků | `Views/ImageGridView.xaml.cs` (komentář u kontextového menu), `Views/ImageViewerOverlay.xaml.cs` → `ToggleFlagAsync` | Uživatelská akce i zkratka F jsou záměrně skryté; databáze a filtrování fungují | **Rozhodnout**, zda znovu zobrazit, nebo ponechat pouze kvůli kompatibilitě |
| Seskupování podle roku/měsíce pořízení | `Views/ImageGridView.xaml` → `GroupByCombo` | Možnosti mají `Visibility="Collapsed"` | **Kandidát k odstranění z UI**, logiku zatím zachovat pro datumový strom |
| Seskupování/řazení podle hodnocení | `Views/ImageGridView.xaml` → `GroupByCombo`, `SortByCombo` | Možnosti skryté | **Zvážit aktivaci** po kontrole přesnosti indexu hodnocení |
| Filtr hodnocení a porovnání hvězd | `Views/ImageGridView.xaml` → `RatingCombo`, `RatingModeCombo` | Ovládací prvky skryté, ve ViewModelu `ImageRefinementFilter` stále existuje | **Kandidát k odstranění z aktivního UI**, ne nutně z datového modelu |
| Pokročilý filtr období | `Views/ImageGridView.xaml` → `DatePresetCombo`, `CustomDatePanel` | Výchozí pole jsou skrytá | **Zvážit**, zda stačí strom dat; pak odstranit duplicitní UI |
| Filtr konkrétní osoby | `Views/ImageGridView.xaml` → `PersonCombo` | Skrytý, přesto se plní daty v `RefreshPeopleAsync` | **Optimalizace výkonu:** po ověření odstranění zbytečného načítání skrytého filtru |
| Filtr flagů | `Views/ImageGridView.xaml` → `FlagCombo` | Skrytý | **Propojit s rozhodnutím** o flagování |
| Filtry chybějícího ratingu, data a GPS | `Views/ImageGridView.xaml` → `MissingMetadataCombo` | „Unrated“, „No capture date“, „No geotag“ skryté; „Untagged“ dostupné | **Zvážit odstranění skrytých položek**, ponechat odtagované |
| Staré handlery posouvání myší | `Views/ImageViewerOverlay.xaml.cs` → `OnPointerPressed/Moved/Released` | Prázdné metody označené jako kompatibilita se starým zoomem | **Silný kandidát k vyčištění**, po ověření žádných XAML handler referencí |
| Další distribuce přes Microsoft Store | `.github/workflows`, `docs/STORE-RELEASE.md` | CI sestavuje MSIX; stabilní distribuční cestou je Inno Setup + GitHub Release | **Zachovat jen pokud je Store reálný plán**; jinak odstranit z nadstandardních CI běhů |
| Nástroje ML benchmarku | `tools/PhotoLibrarian.ModelBench`, `src/PhotoLibrarian.Inference` | Vývojové a evaluační nástroje, nikoli funkce běžného uživatelského okna | **Zachovat odděleně**; neodstraňovat bez kontroly vazeb automatického tagování a testů |

## Doporučené pořadí úklidu

1. **Bezpečné s minimálním rizikem:** vyřadit zbytky neaktuálních dokumentů, prázdné kompatibilní handlery až po prohledání všech odkazů, aktualizovat informace o buildu.
2. **Po rozhodnutí o funkčním rozsahu:** skrytá pole filtrů (hodnocení, datum, osoby, flagy) a skryté možnosti seskupování; odstranit UI a příslušné event handlery pouze tehdy, když je opravdu neplánujeme používat.
3. **Nedotýkat se bez regresních testů:** editor a narovnání, korekce červených očí, metadata, databázové schéma, ONNX pipeline a MSIX toolchain.
4. **Praktický test:** otestovat na skutečných albech vliv aktivního filtru/řazení na rychlost a pádovost při přesunu fotografií.

Poznámka: „nepoužívané“ výše vychází z aktuálního zapojení uživatelského rozhraní, nikoliv z runtime telemetrie. Skutečnou potřebu a možnost odstranění je nutné společně rozhodnout.
