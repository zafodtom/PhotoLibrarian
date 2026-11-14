# PhotoLibrarian ModelBench — bezpečné oddělení

Rozhodnutí: **ponechat volitelné automatické tagování v PhotoLibrarianu**, oddělit experimentální modelový benchmark a přípravné nástroje do samostatného vývojového repozitáře.

## Co patří kam

**V PhotoLibrarianu zůstane:**
- `src/PhotoLibrarian.ML` a navázané modely RAM++ / TinyCLIP;
- `src/PhotoLibrarian.Inference` (sdílené předzpracování, používané aplikací);
- správa, import a schvalování lokálních ONNX modelů;
- albumová databáze a hierarchické automatické tagy.

**Do nového repozitáře se má zkopírovat:**
- `tools/PhotoLibrarian.ModelBench` (CLI, HTML/CSV reporty, Python příprava modelů);
- kopie `src/PhotoLibrarian.Inference` — benchmark potřebuje stejné výpočty;
- `Directory.Build.props`, `Directory.Packages.props` a `LICENSE`.

Kopii knihovny Inference nelze předčasně odstranit z hlavní aplikace: potřebuje ji automatické tagování. Jakmile existuje samostatný repozitář, je možné později zvážit sdílený balíček, ale není to podmínka prvního oddělení.

## Export a založení samostatného repozitáře

Z kořene checkoutu PhotoLibrarianu:

```powershell
.\tools\Export-ModelBench.ps1 -Destination "D:\Projects\PhotoLibrarian-ModelBench"
cd "D:\Projects\PhotoLibrarian-ModelBench"
dotnet build tools\PhotoLibrarian.ModelBench\PhotoLibrarian.ModelBench.csproj -c Release
dotnet run --project tools\PhotoLibrarian.ModelBench\PhotoLibrarian.ModelBench.csproj -- --help
```

Po úspěšném lokálním ověření vytvoř nový **prázdný** repozitář `zafodtom/PhotoLibrarian-ModelBench` na GitHubu a nahraj do něj exportované zdrojové soubory:

```powershell
git init
git add .
git commit -m "Initial standalone ModelBench extraction"
git branch -M main
git remote add origin https://github.com/zafodtom/PhotoLibrarian-ModelBench.git
git push -u origin main
```

Příkazy pro push předpokládají, že prázdný repozitář už skutečně existuje a je nastavené Git ověření. Vhodné je použít soukromý repozitář, dokud nebude ověřena licence všech připravovaných testovacích podkladů.

## Kdy odstranit původní ModelBench

Teprve po ověření nového repozitáře, jeho sestavení a přenesení užitečných testů.
V hlavním `PhotoLibrarian.Tests.csproj` je dnes přímý `ProjectReference` na ModelBench a `ModelBenchTests.cs` používá jeho typy. Před odstraněním z původního repozitáře je nutné obě vazby odstranit nebo přesunout. Tím nebudou dotčené testy skutečné automatické tagovací pipeline.

**Aktuální stav:** exportní skript je připraven, ale nový repozitář zatím nebyl vytvořen. Zdrojový nástroj proto zůstává zachován a nehrozí jeho ztráta.
