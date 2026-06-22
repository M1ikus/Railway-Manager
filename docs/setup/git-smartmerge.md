# Konfiguracja Unity Smart Merge dla Git

`.gitattributes` w tym repo używa `merge=unityyamlmerge` dla scen, prefabów,
materiałów i animacji. Żeby to działało, każda maszyna (każdy współpracownik,
każdy nowy klon repo) musi raz skonfigurować lokalny `.git/config`.

## Dlaczego

Domyślny merge gita nie rozumie YAML Unity i często psuje sceny przy konflikcie.
Unity dostarcza `UnityYAMLMerge.exe` (Smart Merge) który łączy zmiany
strukturalnie, na poziomie obiektów Unity, nie linii.

## Jak

### 1. Znajdź ścieżkę do UnityYAMLMerge

Lokalizacja zależy od wersji Unity i miejsca instalacji. Typowe ścieżki:

- **Windows (Unity Hub default):**
  `C:/Program Files/Unity/Hub/Editor/<wersja>/Editor/Data/Tools/UnityYAMLMerge.exe`
- **Windows (custom install, jak w tym projekcie):**
  `D:/Unity/<wersja>/Editor/Data/Tools/UnityYAMLMerge.exe`
- **macOS:**
  `/Applications/Unity/Hub/Editor/<wersja>/Unity.app/Contents/Tools/UnityYAMLMerge`
- **Linux:**
  `~/Unity/Hub/Editor/<wersja>/Editor/Data/Tools/UnityYAMLMerge`

Aktualna wersja projektu: zobacz `ProjectSettings/ProjectVersion.txt`.

### 2. Skonfiguruj git (raz, lokalnie)

W katalogu projektu:

```bash
git config --local merge.unityyamlmerge.name "Unity SmartMerge"
git config --local merge.unityyamlmerge.driver 'SCIEZKA_DO_UnityYAMLMerge merge -p "%O" "%B" "%A" "%A"'
git config --local merge.unityyamlmerge.recursive binary
```

Przykład dla tego komputera (Unity 6000.4.0f1 w D:/Unity):

```bash
git config --local merge.unityyamlmerge.driver 'D:/Unity/6000.4.0f1/Editor/Data/Tools/UnityYAMLMerge.exe merge -p "%O" "%B" "%A" "%A"'
```

### 3. Sprawdź

```bash
git config --local --get-regexp 'merge.unityyamlmerge'
```

Powinno pokazać trzy linie: `name`, `driver`, `recursive`.

## Testowanie

Smart Merge zadziała automatycznie przy następnym konflikcie na pliku
oznaczonym `merge=unityyamlmerge` w `.gitattributes` (sceny, prefaby, etc.).
Nie ma sensu testować na pustym repo — działa tylko kiedy git znajdzie konflikt.

## Uwagi

- Konfiguracja jest **lokalna** (`.git/config`), nie wersjonowana.
- Każda nowa maszyna / każdy współpracownik musi powtórzyć kroki 1-2.
- Po update Unity (nowa wersja) — zaktualizuj ścieżkę w `merge.unityyamlmerge.driver`.
- Jeśli `UnityYAMLMerge` zwróci błąd przy konflikcie, fallback to ręczny merge YAML — bolesny, ale działa.
