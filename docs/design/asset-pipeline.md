# Asset Pipeline — modele 3D i integracja

> **Cel:** Specyfikacja dla produkcji/integracji modeli 3D w grze Railway Manager.
> Dla znajomych tworzących modele taboru oraz własnej produkcji w Blender/asset store.
> **Aktualna strategia assetowa:** `docs/design/asset-needs-by-system.md` sekcja
> "Realna shopping list dla EA" jest nadrzędna.

---

## Ogólne zasady techniczne

> **Render pipeline:** projekt jest na **URP (Universal Render Pipeline 17.4, aktywny od 2026-06-17 / M-URP)** — wcześniej Built-in RP. Render proceduralny i materiały importowanych modeli idą przez `RailwayManager.Core.Rendering.MaterialFactory` (PBR `URP/Lit` lub `URP/Unlit`). Wzmianki o `URP/Lit` poniżej to **aktualny** materiał, nie aspiracja. Funkcje URP-only (DecalProjector, post-process volume, bloom, lepsze światło) są teraz **dostępne na URP, implementacja TODO M12** — sama migracja objęła pipeline + materiały, nie te features.

### Skala
- **1 jednostka Unity = 1 metr** (domyślna Unity scale)
- Modele muszą być w **skali 1:1** do rzeczywistości
- Lokomotywa EU07: ~15.9m długości, ~3m szerokości, ~4.3m wysokości
- Wagon osobowy standard UIC-X: ~24.5m długości, ~2.8m szerokości, ~4.05m wysokości

### Układ współrzędnych
- **Unity:** Y up, Z forward, X right
- **Blender:** Z up, -Y forward — wymagana konwersja przy eksporcie FBX
- Eksport FBX z Blender: Forward `-Z`, Up `Y`, Apply Transform (Modifiers) ✓

### Pivot point
- **Środek pojazdu** na płaszczyźnie XZ (pozycja na torze)
- **Y = 0** na poziomie szyny (nie dna wagonu)
- Model wystaje ponad pivot (tylko pantograf lekko za pivot w górę)

### Format pliku
- **FBX 2018+** (ASCII lub binary)
- Embedded textures: **NIE** — tekstury osobno, referenced przez Unity
- Materiały: **NIE eksportować z Blender** — Unity ma własne materiały (`URP/Lit` PBR lub `URP/Unlit`, tworzone przez `MaterialFactory` — patrz nota wyżej)
- Animacje: w FBX dla tych co są (pantograf), reszta Unity runtime script

---

## Tabor kolejowy

### Wymagania per model

**Geometria:**
- Target triangle count: **5k-15k tris** dla modelu głównego (LOD 0)
- LOD 1: ~30% tris (uproszczenie siatki)
- LOD 2: ~10% tris (bardzo uproszczony, widoczny z daleka)
- LOD 3: billboard (plaska tekstura z 2D renderem)
- Rozstaw LOD: 0 (0-50m), 1 (50-200m), 2 (200-500m), 3 (500m+)

**UV mapping:**
- Lightmap UV: nie wymagany (gra używa dynamic lighting)
- Main UV: wymagany, atlas lub per-part
- Overlap: dozwolone dla symetrycznych części (pantograf, wózki)

**Tekstury:**
- **Albedo** (bazowy kolor) — 2048×2048 PNG lub 4096×4096 dla hero vehicles
- **Normal** — 2048×2048 tangent space
- **MetallicSmoothness** — R=metallic, A=smoothness
- **AO** — 1024×1024 (bake z high-poly)
- **Emissive** — 1024×1024 dla okien/świateł (opcjonalne)
- Format: PNG na dysk, Unity konwertuje do Crunch/DXT

**Bounding box:**
- `Bounds` component auto-generated przez Unity
- Ale **bake BoxCollider** jako dziecko modelu (szerokość 3m, wysokość 4.5m, długość per model)
- Używane do raycast selection w Depot 3D

### Części modelu (hierarchia w FBX)

```
Pojazd_EN57 (root)
├── Body (pudło główne)
├── Bogie_Front (wózek przedni — osobny GameObject do animacji)
│   ├── Wheels_Front_Left
│   ├── Wheels_Front_Right
│   └── Axles
├── Bogie_Rear (wózek tylny)
│   ├── Wheels_Rear_Left
│   ├── Wheels_Rear_Right
│   └── Axles
├── Pantograph_Front (opcjonalny, dla elektrycznych)
│   ├── Base
│   ├── Arm_Lower
│   ├── Arm_Upper
│   └── Shoe (styk z siecią)
└── Pantograph_Rear (opcjonalny)
```

**Nazewnictwo:** PascalCase, rozróżnij `Front/Rear` dla symetrycznych części

### Animacje

**Koła (wszystkie pojazdy):**
- **NIE animujemy w Blender** — Unity runtime script `transform.Rotate()`
- Wystarczy że koła są osobnymi GameObjects w hierarchii
- Pivot każdego koła w środku osi obrotu
- Script: `wheelsTransform.Rotate(Vector3.right, rpm * Time.deltaTime * speedMultiplier)`

**Pantografy (elektryczne pojazdy):**
- **Animacja w Blender** jako FBX animation clip
- 2 klipsy: `Pantograph_Up` (0.5s), `Pantograph_Down` (0.5s)
- Unity Animator controller z 2 stanami + transitions
- Sterowane z kodu:
  - Engine OFF → PantographDown
  - Engine ON + catenary above → PantographUp
  - Engine ON + no catenary → Engine can't start (nie ma ruchu)

**Drzwi:** NIE animujemy. Pomijamy zgodnie z decyzją.

### Wagony osobowe - model modulowy

> Decyzja produkcyjna: w EA i na premierze 1.0 nie planujemy wagonow towarowych.
> Wagony pasazerskie maja byc zbudowane modularnie, tak aby mala liczba modeli 3D
> obslugiwala wiele rekordow gameplayowych i customowe oklejenia gracza.

**Zakres bazowy dla EA:**

| Element | Liczba | Warianty |
|---------|--------|----------|
| Pudlo wagonu | 3 | stare 24.5 m, nowe 24.5 m, nowe 26.4 m |
| Wozek | 2 | stary typ, nowy typ |
| Hamulec szynowy | 1 opcjonalny add-on | detal wizualny do nowego wozka, nie osobny pelny wozek |
| Wnetrze | 0 modeli | klasa/typ wagonu tylko w danych |

**Pudla:**
- `CoachBody_245_Old` - 24.5 m, stare okna polowkowe, stare drzwi z klamka.
- `CoachBody_245_Modern` - 24.5 m, nowe okna z lufcikami, drzwi odskokowo-przesuwne.
- `CoachBody_264_Modern` - 26.4 m, nowe okna z lufcikami, drzwi odskokowo-przesuwne.

**Wozki:**
- `CoachBogie_Old_BlockBrake` - starszy wozek, kola obreczowane, klasyczne wstawki hamulcowe.
- `CoachBogie_Modern_DiscBrake` - nowszy wozek, kola monoblokowe, hamulce tarczowe.
- `MagneticRailBrake_AddOn` - opcjonalny mesh/detal wlaczany na nowym wozku, gdy wagon ma hamulec szynowy elektromagnetyczny.

**Wnetrza i funkcja wagonu:**
- Nie modelujemy wnetrz wagonow, bo gra nie ma podgladu do srodka.
- Okna wagonow sa ciemne/przyciemnione.
- Typ wagonu wynika z danych w `FleetCatalog`: 1 klasa, 2 klasa, 1/2 klasa, kuszetka,
  sypialny, barowy/restauracyjny, rowerowy, rodzinny, dostosowany dla osob z niepelnosprawnoscia.
- Roznice funkcjonalne powinny byc pokazane z zewnatrz przez oznaczenia, piktogramy,
  numer klasy i ewentualnie drobne dekale, nie przez osobne wnetrze.

**Efekt docelowy:**
- 3 pudla + 2 wozki daja baze dla kilkunastu typow wagonow w danych.
- Roznorodnosc wizualna ma pochodzic z dlugosci pudla, typu drzwi/okien, typu wozka,
  customowego oklejenia, oznaczen klasy/typu i poziomu zabrudzenia.
- Osobne pudlo dla wagonu sypialnego/restauracyjnego mozna dodac post-EA tylko wtedy,
  gdy bedzie to widocznie podnosilo czytelnosc i klimat skladow.

### Livery i customowe oklejenia

> Decyzja: docelowo oklejenia taboru maja byc customowe w grze. Nie mnozymy
> osobnych modeli ani osobnych pelnych tekstur albedo dla kazdego przewoznika.

**Podejscie techniczne:**
- Pojazd ma stala geometrie: pudlo, dach, drzwi, okna, wozki, detale.
- Wyglad przewoznika pochodzi z danych livery/presetu: kolory, pasy, oznaczenia, logo,
  nazwa przewoznika, piktogramy, numer pojazdu, poziom brudu.
- Modele powinny miec sensowne material sloty: `Body`, `Doors`, `Roof`, `Underframe`,
  `Windows`, `Bogie`, `Decals`.
- Pasy, logotypy, oznaczenia klasy i piktogramy powinny byc osobnymi decal meshami,
  decal projectorami albo shader maskami.
- Dane oklejenia zapisujemy jako preset/JSON, nie jako wypalona teksture per wagon.

**Minimalny edytor na EA:**
- Kolor bazowy pudla.
- Kolor pasa bocznego i wybor kilku prostych wzorow pasa.
- Kolor drzwi, dachu i podwozia.
- Nazwa przewoznika jako tekst.
- Automatyczne oznaczenia klasy i piktogramy funkcji wagonu.
- Poziom zabrudzenia/zuzycia jako parametr wizualny.

**Post-EA / 1.0+:**
- Bardziej rozbudowane wzory oklejen.
- Import wlasnego logo jako obrazka, jezeli zostanie ogarniety zapis, walidacja i kwestie prawne.
- Presety dla roznych segmentow oferty: regionalny, dalekobiezny, nocny, techniczny.
- Modding/Steam Workshop dla oklejen, jezeli technicznie bedzie to bezpieczne.

**Uwagi prawne i produkcyjne:**
- W buildach publicznych unikac realnych malowan/operatorow bez licencji.
- Domyslne oklejenia powinny byc fikcyjne lub inspirowane ogolnym stylem kolei, bez kopiowania znakow towarowych.
- Customowe oklejenia maja dac graczowi poczucie wlasnego przewoznika bez koniecznosci produkcji dziesiatek modeli.

### Lokomotywy spalinowe

> Decyzja: dla EA zakres lokomotyw spalinowych jest waski. Gra nie ma towarowek
> w EA ani na premierze 1.0, wiec diesle maja pokryc glownie linie niezelektryfikowane,
> manewry, jazdy techniczne i sytuacje awaryjne.

**Zakres EA:**

| Model | Rola | Uzasadnienie |
|-------|------|--------------|
| 754 | liniowa lokomotywa spalinowa do pociagow pasazerskich | model prawie gotowy, realnie uzywana w Polsce, daje obsluge tras bez sieci trakcyjnej |
| SM42 | manewrowa / techniczna / tania stara lokomotywa | klasyka polskiej kolei, "krolowa manewrow", wazna dla zajezdni i klimatu |

**Rola gameplayowa:**
- 754 ma byc pelnoprawna lokomotywa liniowa dla ruchu pasazerskiego na odcinkach
  niezelektryfikowanych.
- SM42 ma byc przede wszystkim pojazdem manewrowym, technicznym i awaryjnym.
- SM42 moze prowadzic lekkie sklady pasazerskie, ale powinna miec ograniczenia:
  niska predkosc, slabsze przyspieszenie, gorsza efektywnosc i potencjalne kary
  komfortu/eksploatacji przy uzyciu jako regularna lokomotywa liniowa.
- Dzieki temu 754 i SM42 nie dubluja sie funkcja: jedna obsluguje normalny ruch
  na liniach bez drutu, druga robi "brudna robote" kolei.

**Post-EA / 1.0+:**
- `SU160` - nowoczesna polska lokomotywa spalinowa od PESY, dobra do sklepu nowego
  taboru i jako drogi, profesjonalny upgrade dla tras niezelektryfikowanych.
- `SU4210` / `SU4220` - modernizacja klasycznej SM42. Powinna byc dostepna jako
  sciezka modernizacji posiadanej SM42, nie tylko jako osobny zakup, bo daje graczowi
  poczucie rozwoju istniejacego majatku.

**Poza zakresem na razie:**
- Ciezkie diesle towarowe typu ST44/M62 nie sa potrzebne dopoki gra nie obsluguje
  ruchu towarowego.
- Dodatkowe historyczne lub niszowe lokomotywy spalinowe traktowac jako fan-service
  po domknieciu podstawowego gameplayu.

### Spalinowe zespoly trakcyjne i szynobusy

> Decyzja: dla SZT/szynobusow liczymy rodziny modelowe, nie kazda serie osobno.
> Ta kategoria ma duzo realnych wariantow, ale na EA i 1.0 trzeba ograniczyc zakres
> do kilku rodzin, ktore daja czytelna drabinke gameplayowa.

**Zakres EA:**

| Rodzina modelowa | Serie gameplayowe | Rola |
|------------------|-------------------|------|
| SA137 / SA138 | SA137 = 2 czlony, SA138 = 3 czlony | nowszy, pojemniejszy SZT regionalny |
| SA134 / SA135 / SA136 | SA135 = 1 czlon, SA134 = 2 czlony, SA136 = 3 czlony | podstawowy, elastyczny tabor lokalny/regionalny |

**Rola gameplayowa w EA:**
- `SA135` - najtanszy prog wejscia na lokalne linie niezelektryfikowane, mala pojemnosc.
- `SA134` - podstawowy 2-czlonowy SZT dla lokalnych/regionalnych potokow.
- `SA136` - wiekszy 3-czlonowy wariant na mocniejsze potoki.
- `SA137` - nowszy 2-czlonowy SZT, lepszy komfort i niezawodnosc.
- `SA138` - nowszy 3-czlonowy SZT, drozszy i bardziej pojemny.

**Zasada modelowa:**
- SA137 i SA138 powinny wspoldzielic praktycznie ten sam model, z roznica dlugosci
  i liczby czlonow.
- SA134, SA135 i SA136 powinny wspoldzielic praktycznie ten sam model, z roznica
  liczby czlonow.
- Jezeli to technicznie mozliwe, model robic modularnie: czlon A, czlon B/srodkowy,
  czlon C. To ogranicza koszt assetow i ulatwia warianty.

**Zakres 1.0:**
- `SA139` - nowy Pesa Link, do wykorzystania w konfiguratorze nowego taboru.
- `SD85` / `SN84` - spalinowy tabor mniej lokalny, do dluzszych relacji bez sieci
  trakcyjnej i jako alternatywa dla 754 + wagony.

**Balans roli SD85/SN84:**
- SD85/SN84 nie powinien calkowicie zastapic skladow lokomotywa + wagony.
- Ma byc wygodny i samowystarczalny na srednich potokach, ale mniej elastyczny
  pojemnosciowo niz sklad wagonowy.
- `754 + wagony` zachowuje role dla wiekszej pojemnosci i bardziej elastycznego
  konfigurowania skladu.

### Elektryczne zespoly trakcyjne

> Decyzja: EZT to najwieksza i najbardziej ryzykowna kategoria taboru, wiec zakres
> musi byc trzymany rodzinami modelowymi. EA ma dac kontrast: tani klasyk z rynku
> uzywanego oraz nowoczesna rodzina z konfiguratora nowego taboru.

**Zakres EA:**

| Rodzina modelowa | Serie gameplayowe | Rola |
|------------------|-------------------|------|
| EN57 / EN71 | EN57 = 3 czlony, EN71 = 4 czlony | klasyka, tani start, rynek wtorny, duzy potencjal modernizacji |
| Stadler FLIRT | ED160 = 8 czlonow, ER160 = 5 czlonow, L-4268 = 2 czlony, LM-4268 = 3 czlony | nowy tabor z konfiguratora, nowoczesna drabinka dlugosci i komfortu |

**Rola gameplayowa w EA:**
- `EN57` - tani, stary, pojemny klasyk do startu i obslugi regionalnej.
- `EN71` - dluzszy wariant klasyka, wieksza pojemnosc kosztem wieku i utrzymania.
- `L-4268` - krotki FLIRT 2-czlonowy do slabszych potokow i aglomeracji.
- `LM-4268` - FLIRT 3-czlonowy jako sredni nowy zakup.
- `ER160` - FLIRT 5-czlonowy dla mocniejszych relacji regionalnych.
- `ED160` - FLIRT 8-czlonowy dla dalekobieznego/premium segmentu bez wchodzenia
  jeszcze w Pendolino.

**Zasada modelowa dla FLIRT:**
- Stadler FLIRT traktujemy jako jedna rodzine modelowa, nie cztery osobne modele.
- Model powinien byc modularny: czlon kabinowy A, czlony srodkowe, czlon kabinowy B,
  wozki koncowe i wozki Jacobsa.
- Warianty 2/3/5/8-czlonowe skladamy z tej samej rodziny assetow.
- Roznice miedzy ED160, ER160, L-4268 i LM-4268 powinny wynikac glownie z dlugosci,
  danych gameplayowych, wyposazenia i oklejenia.

**Zakres 1.0:**
- `Newag Impuls I` - polski nowoczesny EZT z wieloma wersjami dlugosciowymi.
- `Pesa Elf I` - drugi polski filar regionalno-aglomeracyjny, rowniez z wieloma
  wersjami dlugosciowymi.
- `Modernizacje EN57/EN71` - sciezka rozwoju starego majatku: nowe czola/drzwi/okna,
  lepszy komfort, klimatyzacja/SIP/monitoring, wyzsza niezawodnosc i wyzsza wartosc.

**Post-1.0 / aktualizacje:**
- `Dart ED161` - mocny kandydat na pierwszy wiekszy update dalekobiezny.
- `Pendolino ED250` - duzy update premium/high-speed, gdy gra ma juz sensowny segment
  premium, wyzsze koszty i wymagania infrastrukturalne.
- `Elf II` - pozniejszy update regionalno-aglomeracyjny.
- `Impuls II` - pozniejszy update regionalno-aglomeracyjny.

**Uwagi balansowe:**
- Pendolino nie powinno wejsc za wczesnie tylko jako "szybki drogi pociag".
  Potrzebuje roli premium: wysokie ceny, prestiz, wysokie koszty, ograniczone postoje,
  sensowne wymagania infrastrukturalne i dobry balans popytu.
- Modernizacje EN57/EN71 sa wazne managersko, bo pozwalaja graczowi rozwijac juz
  posiadany tabor zamiast zawsze kupowac nowy.

### Lokomotywy elektryczne

> Decyzja: w EA lokomotywy elektryczne maja dac pelna obsluge skladow wagonowych:
> tani klasyk z rynku wtornego oraz nowoczesny zakup z konfiguratora. Nie dodajemy
> ciezkich lokomotyw towarowych, dopoki gra nie obsluguje ruchu towarowego.

**Zakres EA:**

| Rodzina modelowa | Serie gameplayowe | Rola |
|------------------|-------------------|------|
| EU07 / EP07 / EP08 | praktycznie jeden model 3D, roznice glownie w danych | klasyka, rynek wtorny, podstawowa lokomotywa do skladow wagonowych |
| Newag EU160 | nowy zakup z konfiguratora | nowoczesna lokomotywa dalekobiezna, naturalny upgrade z klasykow |

**Rola gameplayowa w EA:**
- `EU07` - podstawowy, tani klasyk do skladow wagonowych.
- `EP07` - bardzo bliski wariant EU07, roznice glownie w parametrach, stanie i cenie.
- `EP08` - szybszy klasyczny wariant do bardziej ambitnych pociagow pasazerskich.
- `EU160` - nowy, drogi, niezawodny standard 160 km/h do dalekobieznych skladow
  wagonowych i konfiguratora nowego taboru.

**Zasada modelowa:**
- EU07, EP07 i EP08 korzystaja z jednej rodziny modelowej. Z zewnatrz nie robimy
  osobnych modeli, jezeli roznice sa dla gracza slabo widoczne.
- Rozroznienie powinno byc w danych: predkosc maksymalna, moc, koszty utrzymania,
  awaryjnosc, rocznik, stan techniczny, cena i dostepnosc.
- EU160 powinna miec osobny model i pelnic role nowoczesnego "konca roboczego"
  skladow wagonowych, zeby nowe zakupy nie byly zdominowane tylko przez EZT.

**Zakres 1.0:**
- `EP09` - stara szybka lokomotywa do pociagow dalekobieznych; tansza niz nowoczesne
  lokomotywy, ale drozsza w utrzymaniu i bardziej kaprysna.
- `Pesa Gama` - nowa lokomotywa z konfiguratora, alternatywa producenta wobec EU160.
- `Newag EU200` - nowsza i szybsza lokomotywa niz EU160, premium 200 km/h.

**Post-1.0 / DLC / aktualizacje:**
- `EU07A` - odswiezona wersja EP07/EU07, najlepiej jako sciezka modernizacji
  posiadanej lokomotywy, nie tylko osobny zakup.
- `Siemens Vectron` - razem z DLC Niemcy; nowy zakup z konfiguratora i dobry kandydat
  na lokomotywe wielosystemowa/miedzynarodowa z odbiorem za granica.
- `EP05` - potencjalna lokomotywa historyczna/fan-service; nie blokuje core gameplayu,
  bo model moze byc trudniejszy do wykonania.

**Uwagi balansowe:**
- EU200 nie powinna byc po prostu "EU160, tylko lepsza". Musi byc droga i oplacalna
  tylko tam, gdzie infrastruktura, rozklad i popyt wykorzystuja 200 km/h.
- Vectron ma najwiekszy sens razem z krajem/DLC, ktore uzasadnia ruch miedzynarodowy,
  inne systemy zasilania/zabezpieczen i odbior taboru poza Polska.
- Ciezkie elektryczne lokomotywy towarowe (np. ET22/ET41/Dragon jako typowo freight)
  zostaja poza zakresem, dopoki nie ma towarow.

---

## Budynki w zajezdni (Depot 3D)

### Wymagania
- **1-5k tris** dla budynków zewnętrznych
- **Modular pieces** — ściany 1m, 2m, 5m; okna, drzwi jako osobne meshe
- **Mogą być sourcem z asset store** — Synty Studios, Polyperfect, POLYGON packs
- Licencja: tylko te które wolno używać commercially

### Wnętrza i propy pomieszczeń

> Decyzja: nie robimy gotowych wnętrz jako osobnych modeli. Pomieszczenia są budowane
> proceduralnie przez gracza: wybiera typ pomieszczenia i zaznacza obszar. Wyposażenie
> wnętrza to osobne propy, które gracz sam stawia tam, gdzie chce.

**Zakres EA - katalog propsów:**

| Grupa | Modele |
|-------|--------|
| Drzwi i okna | drzwi zwykłe, drzwi techniczne, okno |
| Biuro | biurko, krzesło, szafka, kosz na śmieci, kwiat/roślina, obraz/plakat/tablica |
| Socjal | blat/stół, krzesło, lodówka, szafka, kosz na śmieci |
| Toaleta | toaleta, umywalka, lustro opcjonalnie |
| Warsztat | blat roboczy, szafka narzędziowa, regał, pojemnik/kosz |
| Ogólne | lampa sufitowa, apteczka/BHP, tabliczka pomieszczenia |

**Minimalna lista EA:**
1. Drzwi zwykłe
2. Drzwi techniczne
3. Okno
4. Krzesło
5. Biurko
6. Szafka
7. Regał
8. Blat/stół
9. Lodówka
10. Kosz na śmieci
11. Toaleta
12. Umywalka
13. Lustro
14. Kwiat/roślina
15. Obraz/plakat/tablica
16. Lampa sufitowa
17. Szafka narzędziowa
18. Apteczka/BHP

**Zasady systemowe:**
- Propy nie tworzą gotowego układu wnętrza; gracz stawia je ręcznie.
- Każdy prop powinien mieć footprint/rozmiar na siatce, punkt zaczepienia i zakres obrotu.
- Propy powinny mieć tagi dla UI filtrowania: `Office`, `BreakRoom`, `Toilet`,
  `Workshop`, `Decoration`, `Utility`.
- Elementy ścienne (okno, drzwi, obraz, apteczka, tabliczka) wymagają trybu montażu
  na ścianie.
- Elementy podłogowe (biurko, krzesło, lodówka, toaleta, szafka) stoją na siatce
  pomieszczenia.
- Propy mogą w przyszłości mieć gameplay effect, ale na EA wystarczy warstwa wizualna
  i podstawowa walidacja kolizji/footprintu.

**Poza zakresem EA:**
- Gotowe, prefabrykowane wnętrza.
- Automatyczne umeblowanie pomieszczeń.
- Dziesiątki wariantów tego samego obiektu.
- Bardzo szczegółowe wyposażenie kuchni/toalet.
- Animowane drzwi i interakcje pracowników z propsami.

**Zakres 1.0 / post-launch:**
- Warianty wizualne podstawowych propsów: drugie krzesło, drugie biurko, większa szafa.
- Komputer/monitor, czajnik/ekspres, szafki ubraniowe, ławka pracownicza.
- Prysznic i bardziej rozbudowany zestaw socjalny.
- Więcej propsów warsztatowych i oznakowania BHP.
- Style/epoki wyposażenia i dekoracje zależne od poziomu budynku.

### Funkcjonalna infrastruktura obsługowa zajezdni

> Decyzja: to nie są dekoracje. To modele infrastruktury stawiane przy torze albo
> jako część toru, które mają wspierać obsługę taboru w zajezdni.

**Zakres EA:**

| Element | Warianty | Rola |
|---------|----------|------|
| Obrotnica | 1 | obracanie pojazdu kolejowego albo krótkiego składu |
| Myjnia | mała, średnia, długa | czyszczenie taboru, później koszt/komfort/maintenance |
| Podnośnik | mały, średni, długi | przeglądy, naprawy, obsługa techniczna |
| Stanowisko opróżniania fekaliów | 1 | obsługa pojazdów/wagonów z WC |
| Stanowisko nawadniania | 1 | uzupełnianie wody w pojazdach/wagonach |
| Pomost roboczy / stanowisko wejścia wyżej | 1 | prace przy dachu, pantografach, klimatyzacji; tor przechodzi środkiem |

**Zasady modelowe:**
- Myjnia i podnośnik powinny być traktowane jako rodziny modelowe z wariantami długości,
  nie jako trzy niezależne systemy.
- Warianty długości:
  - `Small` - lokomotywa, krótki szynobus/SZT, pojedynczy wagon,
  - `Medium` - krótszy EZT/SZT albo kilka wagonów,
  - `Long` - dłuższy EZT/SZT albo dłuższy skład.
- Stanowiska fekaliów i nawadniania to przytorowe moduły serwisowe z footprintem
  po jednej stronie toru.
- Pomost roboczy ma zostawiać środek wolny na tor i pojazd; może być jednostronny
  albo dwustronny, ale na EA wystarczy jeden uniwersalny wariant.
- Obrotnica powinna być traktowana jako element torowy specjalny, nie dekoracja.

**Zakres 1.0:**
- Polish tych samych stanowisk zamiast dokładania dużej liczby nowych typów.
- Lepsze animacje/feedback: światła, ruch szczotek w myjni, efekt wody, podnoszenie,
  proste efekty pracy stanowiska.
- UI pokazujące, jaki typ/długość taboru dane stanowisko obsługuje.

**Post-launch:**
- Symboliczne semafory/manewrowe i wskaźniki zajezdniowe jako elementy wizualnego
  feedbacku, nie jako pełna symulacja sygnalizacji.
- Dodatkowe stanowiska diagnostyczne.
- Tankowanie diesla, jeżeli nie zostanie objęte wcześniejszym systemem.
- Stanowisko piaskowania, odladzania albo inne sezonowe/usługowe moduły.

### Hierarchia
```
Budynek_Warsztat (root)
├── Walls
├── Roof (opcjonalne — gracz widzi z góry)
├── Doors
├── Windows
└── Details (komin, rynna, logo)
```

---

## Infrastruktura zajezdni (proceduralne → modele)

> **Status:** Obecnie wszystkie elementy infrastruktury w Depot są **proceduralne**
> (generowane z kodu w runtime przez toole). Docelowo mają być zastąpione modelami 3D
> tak samo jak tabor i budynki. To **dług artystyczny**, nie blokuje gameplay'a —
> pierwotna produkcja przed EA, reszta post-launch.

### Co obecnie jest proceduralne

| Element | Gdzie generowane | Target model |
|---------|------------------|--------------|
| **Szyny** (rails) | `Depot/Track/*` — mesh z Beziera 1435mm | Modułowy mesh profil UIC60, tileable |
| **Podkłady** (sleepers) | Proceduralne prostopadłościany co ~60cm | Drewniany wariant bazowy + podkład halowy |
| **Podsypka** (ballast) | Procedural mesh pod szynami | Tileable mesh + tekstura tłucznia |
| **Stała nawierzchnia** | Brak / do dodania | Bezpodsypkowa nawierzchnia halowa dla torów w halach i myjniach |
| **Rozjazdy** (switches) | `Depot/TurnoutData.cs` + generator | R190 1:9, R300 1:9, Krzyżowy R190 1:9 (patrz niżej) |
| **Słupy trakcji** | `Depot/Catenary/*` — pipeline 3-etapowy | Jeden uniwersalny słup pojedynczy na EA |
| **Wysięgniki trakcji** | `Depot/Catenary/*` | Jeden uniwersalny wysięgnik na EA |
| **Belka bramkowa** | `Depot/Catenary/*` | Jedna uniwersalna belka bramkowa na EA |
| **Przewody trakcji** | Procedural LineRenderer/mesh | Drut jako mesh/tube z prowiskiem |
| **Ściany / mury** | Room tool — prostopadłościany | Modularne: cegła, beton, panel, 1m/2m/5m |
| **Płoty** | Placeholder procedural | Siatka metalowa, bariery drogowe, słupki |
| **Drogi i ścieżki** | Path tool / kratka | Proceduralne po kratkach, z wizualnym krawężnikiem/obrzeżem |
| **Parkingi** | Tile na terenie / kratka | Proceduralne po kratkach, pola parkingowe + wykończenie krawędzi |
| **Perony pasażerskie** | Brak | Nie planujemy peronów pasażerskich w zajezdni |
| **Lampy / oświetlenie** | Point lights bez mesha | Latarnie, lampy halowe, reflektory |
| **Znaki/oznakowanie** | Brak | Tablice km, wskaźniki, hektometry |

### Zasady podejścia

**Dlaczego teraz proceduralne:** M2-M2.6 (Depot builder) musiał działać od zera bez zewnętrznych assetów — tak żeby gracz mógł budować zajezdnię natychmiast. Procedural generators z runtime mesh to rozwiązanie "wystarczająco dobre" na etapie prototype'u.

**Kiedy podmieniać:** W dwóch falach:
- **Faza 1 — przed EA (M12a / M12c Polish):** hero assety widoczne z bliska — szyny, podkłady, słupy trakcji, przewody, rozjazdy, ściany halowe. Te muszą wyglądać dobrze bo gracz się im przygląda.
- **Faza 2 — post-launch:** reszta (płoty, lampy, znaki, warianty regionalne).

**Reguła architektoniczna:** Każdy tool w Depot (`TrackTool`, `CatenaryTool`, `RoomTool`, `PathTool`) ma już warstwę rozdzieloną "co zbudować" vs "jak to wyrenderować". Podmiana = tylko zmiana renderera, logika budowania zostaje. Szczegóły: `docs/design/depot-3d.md`.

### Wymagania techniczne per typ

**Tory (szyny + podkłady + podsypka):**
- Muszą być **tileable** na dowolnej długości toru (od kilku metrów do setek metrów)
- Podkłady jako **instancjowane** meshy co ~60 cm (GPU instancing), żeby nie robić tysięcy draw calls
- LOD: z daleka upraszczany do paska tekstury, z bliska pełna geometria
- Warianty stanu: **standardowy / zużyty**. Nie planujemy zarośniętych torów.
- W halach i myjniach renderer toru automatycznie przełącza się na wariant halowy:
  podkłady halowe + stała nawierzchnia bezpodsypkowa zamiast klasycznej podsypki.
- Zakres EA:
  - szyna / przekrój szyny jako bazowy profil ciągnięty proceduralnie po geometrii toru,
  - podkład drewniany,
  - podkład halowy,
  - podsypka / tłuczeń,
  - stała nawierzchnia bezpodsypkowa dla hal i myjni,
  - kozioł oporowy,
  - trzy typy rozjazdów z dokumentacji, proceduralne albo hybrydowe.
- Zakres 1.0: bez dokładania nowych typów infrastruktury torowej, przede wszystkim polish i integracja.
- Post-launch: ewentualnie inne kozły oporowe, inne podsypki i inne podkłady.

**Słupy trakcji:**
- Zakres EA:
  - jeden uniwersalny słup pojedynczy,
  - jeden uniwersalny wysięgnik,
  - jedna uniwersalna belka bramkowa,
  - drut jako mesh/tube.
- Podział modelu: podstawa + trzon + wysięgnik — montażowy (modular), żeby pipeline generator mógł dobierać wysokości.
- Belka bramkowa powinna działać jako prosty wariant nad kilkoma torami, bez mnożenia typów konstrukcji.
- Pasujące do istniejącego systemu w `Depot/Catenary/` (3-etapowy: klasyfikacja → generacja → optymalizacja podpór).
- Zakres post-1.0:
  - betonowy słup,
  - inne wysięgniki,
  - różne stopnie zużycia i zabrudzenia.

**Przewody trakcji:**
- Drut jako mesh/tube z prowiskiem (catenary curve) między słupami.
- Na EA wystarczy uproszczony zestaw przewodów czytelny wizualnie i zgodny z logiką pantografu.
- Pełne detale typu dodatkowe wieszaki, izolatory wariantowe i przęsła naprężania nie blokują EA.

**Ściany / mury zajezdni:**
- Modularne 1m/2m/5m żeby `RoomTool` mógł składać dowolne rozmiary hal
- Warianty powłoki: cegła, beton tynk, panel blacha, ceglany prefabrykat
- Osobne komponenty: ściana / drzwi bramy / drzwi serwisowe / okno

**Teren, drogi, ścieżki i parkingi:**
- W zajezdni nie planujemy peronów ani elementów pasażerskich.
- Zakres EA:
  - teren,
  - proceduralna droga po kratkach,
  - proceduralna ścieżka po kratkach,
  - wizualny krawężnik/obrzeże dla dróg i ścieżek,
  - drzewa/krzaki jako proste elementy otoczenia,
  - ogrodzenie,
  - szlaban dla wjazdu samochodów,
  - proceduralny parking po kratkach z oznaczeniem miejsc.
- Drogi, ścieżki i parkingi powinny być przede wszystkim systemami proceduralnymi,
  a modele/meshe mają służyć do wizualnego wykończenia krawędzi i powtarzalnych detali.
- Parking na EA może być prosty: kratkowe pole, linie miejsc parkingowych, opcjonalnie
  krawężnik i kilka wariantów nawierzchni.
- Post-EA:
  - więcej ozdób i wariantów zieleni,
  - panele słoneczne jako moduł dekoracyjno-funkcjonalny do wytwarzania energii,
  - więcej wariantów nawierzchni, krawężników i elementów drogowych.

**Ludzie, pojazdy drogowe i życie wokół zajezdni:**
- Zakres EA:
  - jeden prosty model człowieka/pracownika,
  - podstawowa animacja chodzenia,
  - poruszanie się ludka tam, gdzie ma funkcje w zajezdni.
- Zakres 1.0:
  - samochody jadące wzdłuż ulicy poza zajezdnią,
  - opcjonalnie autobusy i ciężarówki jako ambient drogowy,
  - ludzie chodzący po chodniku poza zajezdnią,
  - więcej modeli/wariantów ludzi,
  - samochody pracowników przyjeżdżających do pracy i/lub stojących na parkingu.
- Ruch uliczny i piesi poza zajezdnią to ambient; nie powinien blokować gameplayu
  ani wymagać pełnej symulacji miasta.

**Tło poza ogrodzeniem:**
- Cel: stworzyć wrażenie, że poza ogrodzoną zajezdnią istnieje żyjący świat.
- Zakres EA / polish:
  - proste budynki tła,
  - przystanek autobusowy,
  - fragment ulicy/chodnika poza ogrodzeniem,
  - drzewa/krzaki i małe elementy miejskie.
- Modele tła powinny być LOD-friendly, tanie renderowo i bez wpływu na gameplay.

**Sygnały, wskaźniki i oznakowanie kolejowe w zajezdni:**
- Nie planujemy pełnej funkcjonalnej sygnalizacji zajezdniowej.
- Ruch taboru w zajezdni odbywa się przez klikanie/zlecanie, gdzie dany pojazd ma jechać.
- Ewentualne semafory, wskaźniki i oznakowanie mają być przede wszystkim wizualne
  albo symboliczne, jako feedback stanu toru/manewru.
- Zakres post-launch: proste semafory/manewrowe, wskaźniki i tabliczki torowe, bez
  budowania pełnego systemu zależności sygnałowych.

**Rozjazdy:**
Trzy typy zaimplementowane w `Depot/TurnoutData.cs`:
- **R190 1:9** — zajezdniowy, promień 190m, 27.12m długości (łuk 21.02m + prosta 6.09m)
- **R300 1:9** — kolejowy standardowy, promień 300m, 33.23m długości (sam łuk 33.20m)
- **Krzyżowy R190 1:9** — symetryczny, 33.21m (prosta 6.09m + łuk R190 21.02m + prosta 6.09m)

Wymagania modeli:
- Na EA rozjazdy mogą być **proceduralne albo hybrydowe**, zgodne z powyższymi trzema typami.
- Preferowane podejście: prowadzenie szyn i podkłady generowane proceduralnie, a detale
  takie jak iglice, krzyżownica i kierownice jako modułowe meshe/prefaby.
- Docelowo rozjazdy mogą dostać hero polish, ale nie blokuje to EA.
- **Ruchome elementy:** iglice animowane w Unity Animator albo uproszczone stanem wizualnym (dla M9 przełączania)
- **Pivot zgodny z TurnoutDefinition:** początek toru głównego (x=0, z=0), tor odgałęziający idzie zgodnie z `FrogAngle`
- **LOD:** z bliska pełna geometria (szyny + podkłady + iglice), z daleka uproszczona
- Warianty zużycia (M7): nowy / używany / zużyty

**Źródła modeli:**
- **Kolejowe specyficzne** (szyny, słup/wysięgnik/bramka trakcyjna, rozjazdy) — zlecenie znajomym / własna produkcja w Blender
- **Ściany / podłogi / dachy** — asset store OK (Synty, POLYGON)
- **Płoty / bariery** — mix (część asset store, część na zamówienie)

### Pre-production checklist (przed zleceniem assetów)

- [ ] Zebrać reference photos real PKP — szyny UIC60, uniwersalny słup/wysięgnik trakcyjny, belka bramkowa, hale warsztatowe
- [ ] Zdefiniować style guide: **realistyczny ale lekko stylizowany** (nie photorealism, nie low-poly cartoon)
- [ ] Ustalić budżet tris per typ: szyna 200/m, podkład 50, słup 500, rozjazd 2-3k
- [ ] Dla EA trzymać jeden wariant trakcji; warianty materiałów/zużycia dopiero post-1.0
- [ ] Poprzedzić spec `docs/design/depot-3d.md` sekcja "Catenary pipeline" — model musi być zgodny

---

## Ambient (drogi, piesi, drzewa, tło) — M12 Polish

### Źródła (asset store)
- **Drzewa:** Unity Terrain demo, Polygon Nature, SpeedTree free
- **Budynki tła:** POLYGON City pack, Modern City
- **Piesi:** POLYGON People, Mixamo animations
- **Samochody:** POLYGON Vehicles

### Zasady
- **LOD mandatory** — ambient modele muszą mieć LOD
- **No visible gameplay impact** — czysto wizualne
- **Batching-friendly** — najlepiej static meshes, żeby Unity mogło batchować

---

## Walidacja przy integracji

Przy dodawaniu nowego modelu do gry (FBX → Unity):

1. **Import settings:**
   - Scale factor: 1 (model już w metrach)
   - Mesh Compression: Off dla hero vehicles, Low dla reszty
   - Read/Write: Off (chyba że potrzebne do collision)
   - Normals: Calculate (smoothing angle 60°)
   - Tangents: Calculate Legacy
2. **Material setup:**
   - Auto-generated materiał zastępujemy ręcznym (`URP/Lit` PBR, przez `MaterialFactory` — patrz nota na początku doc'a)
   - Przypisujemy albedo/normal/metallic/AO
3. **Prefab generation:**
   - Drag FBX → scene → dodanie komponentów (VehicleController, WheelRotator, BoxCollider)
   - Save jako prefab w `Assets/Prefabs/Depot/Vehicles/`
4. **FleetCatalog entry:**
   - Dodanie wpisu w `FleetCatalog.cs` z linkiem do prefaba
5. **Test:**
   - Spawn w zajezdni (FleetPanel → MyFleet → DetailPopup → "Pokaż w zajezdni")
   - Verify bounding box, pivot, LOD switching

---

## Checklist dla znajomych produkujących modele

Przekazać razem z zleceniem:

- [ ] Skala **1:1 metry**
- [ ] Pivot: **środek pojazdu XZ, Y=0 na szynie**
- [ ] Forward direction: **+Z** (Unity convention, po konwersji Blender-Unity)
- [ ] FBX 2018+, **bez embedded textures, bez materiałów Blender**
- [ ] Tekstury osobne: Albedo/Normal/Metallic/AO PNG
- [ ] Rozdzielczość tekstur: **2048×2048** dla standardowych, 4096 dla hero
- [ ] **LOD 0/1/2** w jednym FBX lub jako osobne pliki (`*_LOD0.fbx`, `*_LOD1.fbx`)
- [ ] Hierarchia:
  - [ ] `Body` jako root mesh
  - [ ] `Bogie_Front`, `Bogie_Rear` z kołami jako dziećmi
  - [ ] Koła jako **osobne GameObjects** (do Unity runtime rotation)
  - [ ] `Pantograph_Front`, `Pantograph_Rear` (dla elektrycznych) z animacją Up/Down
- [ ] Triangle count w budżecie (5k-15k LOD 0)
- [ ] Bez animacji drzwi
- [ ] UV mapping bez overlap (z wyjątkiem symetrycznych bogies)
- [ ] Dostarczyć: **FBX + PNG tekstury + source Blender plik**

---

## Priorytety produkcji (M-Models / EA, recalibracja 2026-05-23)

Najnowsze źródło prawdy: `docs/design/asset-needs-by-system.md` sekcja
"Realna shopping list dla EA". Starsza lista
"minimum dla M9" jest zastąpiona, bo M9 działa na placeholderach, a realne modele
wchodzą jako osobny milestone M-Models.

### EA blocker — real mesh wymagany
1. **EP07 body** — pokrywa rodzinę `EU07_family` (EP07/EU07/EP08). Status: prototyp freelance, opłacony.
2. **754 Brejlovec body** — liniowy diesel pasażerski. Status: in progress u matseb, risk low.
3. **EU160 Griffin body** — 4 warianty napięciowe reuse mesh + shader/decals. Status: in progress u newkamil, risk high; fallback = post-EA "Modern Electric Update".
4. **Wagon UIC-X 24.5m pudło** — bazowy mesh dla wszystkich klasycznych wagonów osobowych (`Coach_111A/112A/152A/156A` jako dane + paint).
5. **Wózki pasażerskie** — 3 typy modularne do UIC-X/UIC-Z.
6. **Pantografy + zderzaki + drzwi** — modular components dla EP07/EU160/754/wagonów.
7. **7 materiałów PBR + HDRi** — free/Asset Store, spójne z CBM-style.

### EA acceptable placeholder
- **EN57/EN71/EN76** (`EN57_family`) — funkcjonalnie w danych, wizualnie placeholder w EA.
- **SA13x / SZT family** — placeholder w EA.
- **SM42** — placeholder w EA mimo ważnej roli gameplayowej.
- **FLIRT family** — placeholder w EA.
- **Wagon UIC-Z 26.4m** — placeholder w EA; IC dalekobieżny jako content update.
- Cała reszta `initial_market`, która nie ma real mesh, zostaje pomarańczowym cuboidem z labelem.

### Post-EA content updates
1. **EZT** — EN57/EN71 cab + middle members jako pierwsza aktualizacja contentowa.
2. **SZT** — SA base/new families.
3. **FLIRT family** — cab + middle dla 2/3/5/8 członów.
4. **Wagon UIC-Z 26.4m** — dalekobieżny IC.
5. **SM42 body** — opcjonalnie, niski priorytet względem passenger-facing content.
6. Impuls/Elf/Dart/Pendolino, EU200/Gama/SU160, Vectron DLC, historyczne fan-service — późniejsze aktualizacje/DLC.
