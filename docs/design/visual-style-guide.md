# Visual Style Guide

> Status: baza dla Fazy 0, skorygowana po pierwszym passie Fazy 1-2
> Zakres: UI, UX tone, shape language, kamera, oswietlenie, materialy, kierunek kolorystyczny
> Ostatnia aktualizacja: 2026-05-17 (korekta sekcji materialy/tekstury pod CBM-target — patrz `depot-visual-direction.md`)
> Wczesniejsza wersja: 2026-04-28

---

## 1. Teza wizualna

Railway Manager to przyjazna, czytelna gra kolejowa low poly w stylu nowoczesnej makiety: miekkie, zaokraglone formy, spokojne materialy, dobrze zorganizowane ekrany i UI, ktore prowadzi gracza ukladem oraz ksztaltem zanim zacznie prowadzic go kolorem.

To zdanie jest filtrem dla kolejnych decyzji. Jezeli zmiana sprawia, ze gra staje sie chlodniejsza, ciezsza, bardziej chaotyczna, bardziej agresywna albo wizualnie "twardsza", to najpewniej idzie w zlym kierunku.

Wazna korekta: ten guide nie nakazuje maksymalnego rozjasniania UI. "Przyjazne" nie znaczy "wyplukane", a "czytelne" nie znaczy "prawie biale".

---

## 2. Filary produktu

### Najpierw orientacja

Gra ma byc goscinna, nie surowa. Mlody gracz powinien bez walki z interfejsem rozumiec, co jest klikalne, co jest zaznaczone, gdzie patrzec i co wydarzylo sie po wykonaniu akcji.

### Forma przed kolorem

Low poly w tej grze ma wynikac glownie z bryl, zaokraglen, prostych ksztaltow i czystych warstw, a nie z samego rozjasniania tla albo podkrecania pastelowych kolorow.

### Czytelnosc ponad realizm

Wolimy czyste sylwetki, czytelne kolory i oczywiste stany interakcji niz realistyczny brud, drobny szum detalu albo ciezki, filmowy nastroj.

### Nowoczesna makieta kolejowa

Celem nie jest ani zabawkowy chaos, ani twardy symulator operatorski. Gra ma przypominac starannie zaprojektowana makiete kolejowa: uporzadkowana, ciepla, stylizowana i przyjemna do ogladania z typowego dystansu kamery.

### Spokojna kontrola

Gracz ma czuc sie pewnie. Ruch kamery, stany przyciskow, highlighty, przejscia i overlaye maja byc plynne i uspokajajace, a nie nerwowe lub przesadnie dramatyczne.

---

## 3. Zasady swiata

### Oswietlenie i nastroj

- Swiat powinien wygladac jak pogodna scena dzienna, ale nie przepalona i nie wyblakla.
- Unikamy smoliscie czarnych cieni i bardzo ostrego kontrastu.
- Preferujemy miekkie krawedzie cieni i delikatny ambient fill.
- Depot moze byc cieplejszy i bogatszy niz mapa, ale obie sceny nadal musza wygladac jak jedna gra.

### Geometria i materialy

> **Korekta 2026-05-17:** target wizualny doprecyzowany jako *City Bus Manager*-style (stylized PBR z umiarkowanymi teksturami), nie pure low-poly flat shading. Sekcja ponizej zaktualizowana. Szczegoly per-sekcja Depot: `depot-visual-direction.md`.
>
> **Render pipeline:** stylized PBR opisany niżej realizujemy na **URP (Universal Render Pipeline 17.4, aktywny od 2026-06-17 / M-URP)** — wczesniej Built-in RP (shader `Standard`). Materialy proceduralne sa juz **URP/Lit** (PBR) lub **URP/Unlit**, tworzone przez `RailwayManager.Core.Rendering.MaterialFactory`. Funkcje post-processingowe (bloom, lepsze swiatlo) oraz decale (`DecalProjector`) sa teraz **dostepne na URP, ale implementacja TODO M12** — sama migracja objela pipeline + materialy, nie te features. Patrz nota w `depot-visual-direction.md`.

- Low poly ma znaczyc proste, pewne formy, a nie niedokonczone placeholdery.
- Bryly powinny byc lekko zmiekczone: bevel, zaokraglenie, lagodniejsze przejscia bryl sa mile widziane tam, gdzie nie szkodza czytelnosci.
- Materialy to **stylized PBR**: albedo + light normal + minimalna roughness variation. Nie flat low-poly z vertex colors, ale tez nie hiper-real PBR z heavy detail.
- **Umiarkowany szum tekstur jest dozwolony i pozadany** dla naturalistycznych powierzchni (asfalt z wear, beton z krakelura, cegla z wzorem brick, trawa z gradient). To NIE jest "low noise" — CBM target ma widoczne tekstury na powierzchniach na poziomie wzroku przy zoom in.
- Czytelnosc ma wynikac **z kompozycji + koloru + tekstury**, nie wylacznie z ksztaltu. Tekstura nie powinna jednak konkurowac z silhouette obiektu — szum sluzy materialowi, nie zaglusza go.
- Anti-targety: gritty realism (heavy grime, mud, decay), filmowy noise, hiper-real PBR z normal map detail mocno powyzej silhouette resolution.
- Preferujemy cieple tony gruntu, malowany metal, spokojny beton, drewno i czyste oznakowanie.

### Uzycie koloru w swiecie

- Bazowe kolory swiata powinny byc cieplo-neutralne i lekkie dla oka.
- Tory, perony, sciezki, budynki i warstwy mapy musza sie odrozniac juz przy pobieznym spojrzeniu.
- Nasycenie powinno byc umiarkowane: przyjazne i zywe, ale nie cukierkowe.

---

## 4. Zasady UI

### Ogolny ton

- UI powinno byc miekkie, spokojne i przystepne.
- Preferujemy srednie albo lekko jasne powierzchnie zamiast skrajnie ciemnych paneli i zamiast prawie bialych plaskich kart.
- Rogi powinny byc zaokraglone, a glowne panele powinny miec wyrazny, ale spokojny ksztalt.
- Kontrolki powinny wygladac na na tyle duze, zeby mozna im bylo zaufac.
- Kolor jest wsparciem. Glowna czytelnosc ma wynikac z ukladu, grupowania i shape language.

### Organizacja ekranu

- Kazdy ekran powinien miec wyrazne strefy: kontekst, glowna zawartosc, akcje, status.
- Gracz powinien od razu wiedziec, co jest "naglowkiem", co jest "danymi", a co jest "czy moge cos teraz zrobic".
- Sekcje powinny byc budowane kartami, insetami i spacingiem, a nie samymi separatorami linii.
- Najwazniejsze panele maja wygladac jak dobrze ulozone elementy makiety, nie jak lista technicznych kontrolek wrzuconych jeden pod drugim.

### Hierarchia

- Najwazniejsze akcje musza byc od razu oczywiste.
- Stan zaznaczenia musi byc widoczny natychmiast.
- Koszt, ostrzezenie i sukces maja byc czytelne bez potrzeby otwierania tooltipa.
- Najpierw budujemy hierarchie spacingiem, skala i ksztaltem, dopiero potem dodatkowymi dekoracjami i kolorem.

### Charakter interakcji

- Hover, press, selected, disabled i warning musza sie wyraznie roznic.
- Ruch ma byc krotki i miekki, zwykle okolo 150-250 ms.
- Unikamy flashy popupow, gumowego bounce i glosnych efektow w podstawowych flow UI.

### Dostepnosc i mlodsi gracze

- Unikamy bardzo malego tekstu.
- Unikamy szarego tekstu na lekko innym szarym tle.
- Nie opieramy waznych akcji tylko na ikonach, jezeli znaczenie nie jest juz dobrze utrwalone.
- Gdzie to mozliwe, wazne przyciski powinny laczyc ikone i etykiete.

---

## 5. Typografia

### Kierunek

- TMP jest domyslnym systemem tekstu dla player-facing UI.
- Standaryzujemy jedna glowna rodzine fontow dla wiekszosci UI. Obecne zasoby Noto sa domyslnym punktem startowym.
- Nowo dotkniety kod UI odchodzi od LegacyRuntime.ttf.

### Zalecana skala

- H1: 40-48
- H2: 28-32
- H3: 20-24
- Body: 15-18
- Secondary / caption: 12-13
- Button label: 15-18

### Zasady typografii

- Hierarchie budujemy glownie rozmiarem i weight, nie samym kolorem.
- Tekst glowny musi pozostac dostatecznie ciemny na jasnych powierzchniach.
- Unikamy dlugich etykiet all-caps poza drobnymi utility tagami.

---

## 6. Paleta startowa

To jest system startowy, a nie finalny art lock. Nowe UI powinno trzymac sie tej rodziny kolorystycznej, chyba ze pojawi sie mocny powod do odejscia.

### Powierzchnie UI

- App background: `#DDD4C7`
- Primary surface: `#EDE3D4`
- Secondary surface: `#D7CAB8`
- Raised surface: `#F3EADD`
- Border / divider: `#B6A791`

### Tekst

- Primary text: `#23313B`
- Secondary text: `#5D676E`
- Disabled text: `#8A918E`

### Akcje i stany

- Primary accent: `#5A9ACB`
- Primary accent hover: `#4A89B8`
- Success: `#76B06F`
- Warning: `#D7A34B`
- Danger: `#CE6D60`
- Selection / focus: `#3D82BA`

### Kotwice kolorystyczne swiata

- Sky: `#C9E2F4`
- Grass: `#8DBD73`
- Warm soil: `#C8A47D`
- Concrete: `#BFB5A8`
- Painted rail infrastructure: `#6A747D`
- Wood / sleepers accent: `#9C7448`

### Reguly palety

- Nie uzywamy czystej czerni jako standardowego tla paneli.
- Nie uzywamy prawie bialych, wyplukanych paneli jako domyslnego shellu gry.
- Nie uzywamy neonowych akcentow.
- Nie polegamy tylko na czerwonym i zielonym, zeby tlumaczyc znaczenie.
- Akcent kolorystyczny ma byc oszczedny: priorytetem jest porzadek i forma.

---

## 7. Kierunek kamery

### Kamera w Depot

Depot jest glowna scena prezentacyjna.

- Ruch ma byc plynny i celowy.
- Zmiana focusu ma pomagac podziwiac uklad makiety.
- Kamera ma wspierac zarowno budowanie, jak i inspekcje.
- Depot ma wygladac jak dopracowany miniature world, a nie scena debugowa.

Praktyczny kierunek:

- Utrzymujemy gladkie i przewidywalne damping.
- Stawiamy na stabilne kadrowanie zamiast skrajnych katow.
- Ulepszamy focus-on-object zanim zaczniemy dodawac dalsza zlozonosc kamery.
- Highlight zaznaczenia ma wspolpracowac z kamera, a nie z nia rywalizowac.

### Kamera mapy

Mapa jest widokiem operacyjnym, ale nie moze byc zimna ani wroga.

- Powinna byc czystsza i bardziej plaska niz Depot.
- Zastepujemy sterylna czern i ostry kontrast jasniejsza, spokojniejsza prezentacja.
- Czytelnosc torow, stacji, tras i zaznaczenia ma najwyzszy priorytet.

Praktyczny kierunek:

- Zachowujemy orthographic clarity.
- Uzywamy wyraznych pasm zoomu: overview, planning, detail.
- Unikamy naglych skokow stanu przy zoomie i przy zmianie kontekstu.

---

## 8. Kierunek oswietlenia

### Depot

- Cieple swiatlo dzienne
- Miekki ambient fill
- Miekkie, ale czytelne cienie
- Bez zgniecionych czerni
- Materialy maja rozdzielac sie glownie value i hue, nie tylko roughness

### Mapa

- Jasne i neutralne swiatlo dzienne
- Na tyle plaskie, zeby zachowac czytelnosc
- Nie calkiem martwe i nie sterylne
- Tlo ma wspierac dane, a nie je zagluszac

### Anty-cele

- Bez mrocznego, brudnego industrialu
- Bez prawie nocnego kontrastu w normalnej rozgrywce
- Bez przypadkowo pomieszanych temperatur barwowych
- Bez czarnej pustki za warstwami gameplayowymi, jezeli da sie uzyc miekszego tla

---

## 9. Zasady komponentow UI

### Rogi: stale panele ostre, plywajace zaokraglone (decyzja 2026-06-19)

Mieszany system, kryterium "staly vs przemieszczalny" — doprecyzowuje regule "domyslnie zaokraglone rogi":

- **Stale panele** otwierane z nav railu, dociagniete do krawedzi ekranu (Tabor, Finanse, Personel, Warsztat, Magazyn, Obiegi, Rozklady, Settings) -> **OSTRE rogi (square)**. Zaokraglony naroznik przy krawedzi przeswietuje scene = zle. Decyzja A: duze panele zarzadzania ujednolicone do full-bleed.
- **Kontekstowe popupy / modale** wywolane klknieciem encji (klik pojazd -> detal, klik tor/budynek/sklad; modale, koszyk, context menu) -> **ZAOKRAGLONE** (plywaja na dim, naroznik nie przeswietuje).

Regula "square" jest **waska** — tylko full-bleed dotykajace krawedzi. Zaokraglenie pozostaje domyslne dla popupow, kart, przyciskow i wewnetrznych powierzchni (zgodne z teza "miekkie, zaokraglone formy"). Technicznie: full-bleed = zwykly `Image` (square); plywajace = `UITheme.ApplySurface(..., UIShapePreset.PanelLarge/Panel)`. Audyt zgodnosci + lista zaleglych konwersji: `docs/TECH_DEBT.md` TD-043.

Kazdy wspolny komponent UI wprowadzany od Fazy 1 powinien spelniac te warunki:

- Domyslnie zaokraglone rogi
- Miekki kontrast miedzy panelem a tlem
- Jasny stan zaznaczenia
- Duzy click target
- Jedna oczywista primary action w kontekscie
- Spojne spacing tokens
- Co najmniej dwa poziomy powierzchni: outer card i inset / control layer
- Czytelny podzial na "informacja" oraz "akcja"

Zalecane wartosci startowe:

- Outer panel padding: 16-24
- Section gap: 16-24
- Row gap: 8-12
- Button height: 36-48
- Modal corner radius: 12-16
- Small control corner radius: 10-12
- Pill / tab radius: 18-22

---

## 10. Co pasuje, a co nie

### Pasuje

- Pogodne i czytelne sceny
- Cieple materialy
- Miekkie cienie
- Spokojny ruch UI
- Przyjazne etykiety
- Duze, czytelne kontrolki
- Czyste low poly silhouette
- Zaokraglone karty i panele
- Dobrze wydzielone sekcje ekranu
- Umiarkowany kontrast tla z czytelnym tekstem

### Nie pasuje

- Chlodne korporacyjne control-room UI
- Ciemny charcoal dashboard jako domyslny look
- Wyplukane, prawie biale panele jako domyslny look
- Gritty realism i bardzo brudne tekstury (heavy grime, mud, decay, rust) — light wear i krakelura sa OK i pozadane (zgodnie z CBM-target)
- Bardzo maly tekst i geste tabele bez hierarchii
- Agresywne VFX i glosne przejscia
- Czarna prezentacja mapy jako domyslny stan, chyba ze konkretne narzedzie uzasadnia wyjatek
- Flat low-poly bez tekstur (vertex color only) — to ponizej CBM-target jakosciowo

---

## 11. Kolejnosc wdrozenia po korekcie kierunku

Faza 0 jeszcze nie zmienia gry. Ona definiuje cel i porzadek prac.

Pierwsze kroki implementacyjne powinny byc takie:

1. Zdefiniowac shape language:
   rounded sprites / 9-slice, karty, insets, pill tabs, button variants.
2. Uporzadkowac layout:
   kazdy ekran ma miec jasny podzial na kontekst, tresc, akcje i status.
3. Dopiero potem korygowac tonalnosc:
   przyciemnic powierzchnie o jeden stopien i ograniczyc kolor jako glowny nosnik roznic.
4. Rozlewac ten system na wspolne prymitywy oraz kolejne ekrany gameplayowe.
5. Po ustabilizowaniu UI wrocic do kamery, oswietlenia i szerszego polishu Depot / Mapy.

Wniosek praktyczny: wczesne passy Fazy 1-2 nie sa finalnym "low poly UI pass". To dopiero fundament. Kolejne iteracje maja poprawiac przede wszystkim forme, uklad i hierarchie, a nie tylko jasnosc oraz barwy.

---

## 12. Pliki najbardziej dotkniete przez ten guide

- `Assets/Scripts/SharedUI/TopBarUI.cs`
- `Assets/Scripts/MainMenu/MainMenuUI.cs`
- `Assets/Scripts/Personnel/UI/UiHelper.cs`
- `Assets/Scripts/Depot/DepotOrbitCamera.cs`
- `Assets/Scripts/Map/Rendering/CameraController.cs`
- `Assets/Scripts/Map/Rendering/MapSetup.cs`
- `Assets/Materials/*`

To nie sa wszystkie pliki, ktore zmienia sie pozniej, ale sa najlepszymi punktami wejscia do wdrazania tego kierunku.
