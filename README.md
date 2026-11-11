# Seed Apocalypse — GitHub build repository

Deze repository bevat de C#-broncode en de referentie-assemblies voor de Seed Apocalypse-mod voor PvZ Fusion 4.0.5.2 met MelonLoader 0.7.x.

## Automatisch een DLL bouwen met GitHub

1. Pak `SeedApocalypse_GitHub.zip` uit op je computer.
2. Maak op GitHub een nieuwe repository aan. Een **private repository** is verstandig zolang je niet zeker weet of je de meegeleverde game-/loaderbestanden openbaar mag verspreiden.
3. Upload de **inhoud van deze map** naar de hoofdmap van je GitHub-repository. Upload dus `.github`, `libs`, `Mod.cs`, `SeedApocalypse.csproj`, `build.bat`, en `README.md` — niet alleen de ZIP zelf.
4. Commit de bestanden naar de branch `main` (of `master`).
5. Open op GitHub het tabblad **Actions** en open de workflow **Build Seed Apocalypse DLL**.
6. Wacht tot de run klaar is. Bij een geslaagde build verschijnt onderaan de run bij **Artifacts** een download met de naam `SeedApocalypse`.
7. Download en pak dat artifact uit. Daarin staat `SeedApocalypse.dll`. Zet die DLL in de `Mods`-map van PvZ Fusion.

De workflow start automatisch bij iedere push naar `main` of `master`. Je kunt hem ook handmatig starten via **Actions → Build Seed Apocalypse DLL → Run workflow**.

## Lokaal bouwen op Windows

Installeer de **.NET 6 SDK** (niet alleen de .NET Runtime), pak de repository volledig uit en voer `build.bat` uit. De DLL verschijnt daarna in `bin/Release/net6.0/SeedApocalypse.dll`.

## Mapstructuur

```text
.github/
  workflows/
    build.yml
libs/
  Il2CppAssemblies/  # PvZ Fusion game reference assemblies
  net6/              # MelonLoader / Il2CppInterop reference assemblies
Mod.cs
SeedApocalypse.csproj
build.bat
README.md
```

## Belangrijk

- Deze bestanden zijn afgestemd op de aangeleverde PvZ Fusion 4.0.5.2- en MelonLoader 0.7.x-bestanden. Andere versies kunnen andere referenties of code-aanpassingen vereisen.
- De eerste GitHub-build kan compilerfouten laten zien als de broncode niet overeenkomt met de exacte game-API. Bekijk dan de foutmelding in **Actions → Compile mod**.
- Dit ZIP-bestand bevat de broncode en buildreferenties, **niet** een vooraf gebouwde `SeedApocalypse.dll`. GitHub Actions maakt die DLL aan wanneer de workflow slaagt.
- Controleer de licentie- en distributieregels van de game en MelonLoader voordat je de repository openbaar maakt. Gebruik bij twijfel een private repository.
