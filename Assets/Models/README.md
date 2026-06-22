# Models - Struktura folderów

## Depot/ - Modele dla systemu zajezdni

### Vehicles/
**Przeznaczenie:** Modele pojazdów szynowych
- Lokomotywy (.fbx, .blend)
- Wagony
- Tabor spalinowy/elektryczny

**Konwencja nazewnictwa:**
- `Loco_[Typ]_[Nazwa].fbx` - np. `Loco_Electric_EP09.fbx`
- `Wagon_[Typ]_[Nazwa].fbx` - np. `Wagon_Passenger_Bhp.fbx`

### Track/
**Przeznaczenie:** Elementy infrastruktury torowej
- Szyny (Rails) - modele 1m szyny
- Podkłady (Sleepers) - model pojedynczego podkładu
- Rozjazdy (Switches)
- Krzyżownice (Crossings)

**Konwencje nazewnictwa:**
- `Rail_1m.fbx` - model 1 metra szyny
- `Sleeper_Standard.fbx` - standardowy podkład
- `Switch_[Typ].fbx` - rozjazd

### Buildings/
**Przeznaczenie:** Budynki i konstrukcje zajezdni
- Hale warsztatowe
- Wiaty
- Budynki administracyjne
- Infrastruktura (rampy, perony)

**Konwencje nazewnictwa:**
- `Building_[Typ]_[Nazwa].fbx`

---

## Zalecenia techniczne

### Format plików:
- **Preferowany:** `.fbx` (uniwersalny, dobra kompatybilność z Unity)
- **Alternatywny:** `.blend` (bezpośredni import z Blendera)

### Skala:
- **1 jednostka Unity = 1 metr** w świecie rzeczywistym
- Model szyny 1m powinien mieć długość dokładnie 1.0 w Unity

### Pivot Point:
- **Pojazdy:** pivot na środku osi pojazdu, na poziomie szyn
- **Szyny:** pivot na początku odcinka (jeden koniec)
- **Podkłady:** pivot na środku podkładu
- **Budynki:** pivot na środku podstawy budynku (poziom gruntu)

### Materiały:
- Przypisuj materiały w Blenderze/programie 3D
- Unity automatycznie zaimportuje je do folderu Materials/

### LOD (Level of Detail):
- Jeśli model ma LOD, nazwij zgodnie z konwencją: `Model_LOD0`, `Model_LOD1`, `Model_LOD2`
