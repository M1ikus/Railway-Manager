using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using UnityEngine.UI;
using TMPro;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace MainMenu
{
    /// <summary>
    /// Pełnoekranowy panel "Gra wieloosobowa".
    /// Lista serwerów z wyszukiwarką i przyciskiem tworzenia gry.
    /// </summary>
    public class MultiplayerScreenUI : MonoBehaviour, IMenuScreen
    {
        // ── mock server data ──────────────────────────
        // TODO (M10 Mirror integration): zastąpić MockServers prawdziwym discovery
        // (Mirror.NetworkDiscovery / Steam P2P lobby list). Mock zostaje jako fallback
        // dla offline preview UI. OnJoinServer / OnCreateGame też wymagają realnej impl.
        private struct ServerEntry
        {
            public string name;
            public string map;
            public int    players;
            public int    maxPlayers;
            public int    ping;       // ms
        }

        private static readonly ServerEntry[] MockServers = new[]
        {
            new ServerEntry { name = "Kolej Śląska #1",        map = "Depot Alpha",   players = 3,  maxPlayers = 8,  ping = 22  },
            new ServerEntry { name = "PKP Intercity Pub",       map = "Depot Beta",    players = 7,  maxPlayers = 8,  ping = 48  },
            new ServerEntry { name = "Tramwajarze z Krakowa",   map = "Central Hub",   players = 1,  maxPlayers = 4,  ping = 110 },
            new ServerEntry { name = "Railway Legends EU",      map = "Depot Alpha",   players = 5,  maxPlayers = 12, ping = 35  },
            new ServerEntry { name = "Friendly Beginners",      map = "Tutorial Map",  players = 2,  maxPlayers = 6,  ping = 67  },
            new ServerEntry { name = "Hardcore Railroaders",    map = "Mountain Pass", players = 8,  maxPlayers = 8,  ping = 19  },
            new ServerEntry { name = "Test Server (priv)",      map = "Depot Alpha",   players = 1,  maxPlayers = 2,  ping = 5   },
        };

        // ── colours ───────────────────────────────────
        private static readonly Color SearchBarBg  = UITheme.OverlayPanel;
        private static readonly Color RowBg        = UITheme.OverlayPanel;
        private static readonly Color RowHover     = UITheme.OverlayPanelStrong;
        private static readonly Color RowFull      = UITheme.WithAlpha(UITheme.Danger, 0.18f);
        private static readonly Color BottomBarBg  = UITheme.OverlayPanelStrong;
        private static readonly Color InputBg      = UITheme.TopBarInset;
        private static readonly Color BtnPrimary   = UITheme.PrimaryAccent;
        private static readonly Color BtnSecondary = UITheme.SecondarySurface;
        private static readonly Color TextPrimary  = UITheme.PrimaryText;
        private static readonly Color TextMuted    = UITheme.SecondaryText;
        private static readonly Color TextRed      = UITheme.Danger;
        private static readonly Color Accent       = UITheme.PrimaryAccent;
        private static readonly Color PingGood     = UITheme.Success;
        private static readonly Color PingMed      = UITheme.Warning;
        private static readonly Color PingBad      = UITheme.Danger;

        // ── references ────────────────────────────────
        private GameObject        _root;
        private Transform         _contentParent;
        private RectTransform     _contentRT;
        private TextMeshProUGUI   _titleLbl;
        private TextMeshProUGUI   _backLbl;
        private TextMeshProUGUI   _emptyLbl;
        private TMP_InputField    _searchField;
        private TextMeshProUGUI   _serverCountLbl;
        private TextMeshProUGUI   _createLbl;
        private TextMeshProUGUI   _refreshLbl;

        private string _searchText = "";

        public System.Action OnBack;

        // ─────────────────────────────────────────────
        //  PUBLIC API
        // ─────────────────────────────────────────────

        public void Build(Transform canvasTransform)
        {
            BuildRoot(canvasTransform);
            BuildSharedTopBar();
            BuildSearchBar();
            BuildScrollArea();
            BuildBottomBar();
            _root.SetActive(false);
        }

        public void Show()
        {
            _root.SetActive(true);
            _searchText = "";
            if (_searchField != null) _searchField.text = "";
            PopulateServers();
            RefreshLanguage();
        }

        public void Hide()    => _root.SetActive(false);
        public bool IsVisible => _root != null && _root.activeSelf;

        public void RefreshLanguage()
        {
            if (_titleLbl != null) _titleLbl.text = LocalizationService.Get("multiplayer.title");
            if (_backLbl != null)  _backLbl.text  = "\u2190";
            if (_createLbl != null) _createLbl.text = LocalizationService.Get("multiplayer.action.create_game");
            if (_refreshLbl != null) _refreshLbl.text = LocalizationService.Get("multiplayer.action.refresh");
            UpdateServerCount();

            // Refresh placeholder dla SearchField (zmiana j\u0119zyka aktualizuje placeholder text)
            if (_searchField != null && _searchField.placeholder is TextMeshProUGUI placeholderTmp)
                placeholderTmp.text = LocalizationService.Get("multiplayer.search.placeholder");
        }

        // === i18n hot-reload (M13-4d) ===

        void OnEnable()
        {
            LocalizationService.OnLanguageChanged += OnLocaleChanged;
        }

        void OnDisable()
        {
            LocalizationService.OnLanguageChanged -= OnLocaleChanged;
        }

        private void OnLocaleChanged()
        {
            if (IsVisible)
            {
                PopulateServers(); // server rows maj\u0105 localized "Join" / "Full" labels
                LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRT);
            }
            RefreshLanguage();
        }

        // ─────────────────────────────────────────────
        //  ROOT
        // ─────────────────────────────────────────────

        private void BuildRoot(Transform parent)
        {
            _root = MenuScreenPrimitives.CreateFullscreenRoot("MultiplayerScreen", parent);
        }

        // ─────────────────────────────────────────────
        //  TOP BAR
        // ─────────────────────────────────────────────

        private void BuildSharedTopBar()
        {
            var bar = MenuScreenPrimitives.CreateTopBar("TopBar", _root.transform, () => OnBack?.Invoke(), out _backLbl, out _titleLbl);

            var spacer = NewGO("Spacer", bar.transform);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _serverCountLbl = MakeTMP("Count", bar.transform);
            _serverCountLbl.fontSize  = 20;
            _serverCountLbl.color     = TextMuted;
            _serverCountLbl.alignment = TextAlignmentOptions.Right;
            _serverCountLbl.raycastTarget = false;
            _serverCountLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 200f;
        }

        // ─────────────────────────────────────────────
        //  SEARCH BAR
        // ─────────────────────────────────────────────

        private void BuildSearchBar()
        {
            var bar = NewGO("SearchBar", _root.transform);
            bar.AddComponent<Image>().color = SearchBarBg;
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -70f);
            rt.sizeDelta = new Vector2(0f, 60f);

            var hl = bar.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Xxl, UITheme.Spacing.Md);
            hl.spacing  = UITheme.Spacing.Md;
            hl.childAlignment      = TextAnchor.MiddleLeft;
            // 2026-05-17: childControlWidth/Height true (TD-029 pattern, by\u0142o false \u2192 LayoutElement
            // ignored, server name + map name nak\u0142ada\u0142y si\u0119 na siebie).
            hl.childControlWidth   = true;
            hl.childControlHeight  = true;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            // Search icon \u2014 procedural sprite (BUG-014: emoji \uD83D\uDD0D nie ma w fontach)
            var iconGO = NewGO("Icon", bar.transform);
            var iconImg = iconGO.AddComponent<Image>();
            iconImg.sprite = IconGenerator.GetSearchSprite();
            iconImg.color = TextMuted;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
            var iconLE = iconGO.AddComponent<LayoutElement>();
            iconLE.preferredWidth  = 28f;
            iconLE.preferredHeight = 28f;

            // Input field
            var inputGO = NewGO("Input", bar.transform);
            inputGO.GetComponent<RectTransform>().sizeDelta = new Vector2(500f, 36f);
            inputGO.AddComponent<Image>().color = InputBg;
            inputGO.AddComponent<LayoutElement>().preferredWidth = 500f;

            // Input — text area
            var textArea = NewGO("Text Area", inputGO.transform);
            FillRT(textArea);
            var textAreaRT = textArea.GetComponent<RectTransform>();
            textAreaRT.offsetMin = new Vector2(8f, 2f);
            textAreaRT.offsetMax = new Vector2(-8f, -2f);

            // placeholder
            var placeholder = MakeTMP("Placeholder", textArea.transform);
            placeholder.text      = LocalizationService.Get("multiplayer.search.placeholder");
            placeholder.fontSize  = 20;
            placeholder.color     = TextMuted;
            placeholder.fontStyle = FontStyles.Italic;
            placeholder.raycastTarget = false;
            FillRT(placeholder.gameObject);

            // text
            var inputText = MakeTMP("Text", textArea.transform);
            inputText.fontSize = 20;
            inputText.color    = TextPrimary;
            inputText.raycastTarget = false;
            FillRT(inputText.gameObject);

            _searchField = inputGO.AddComponent<TMP_InputField>();
            _searchField.textComponent   = inputText;
            _searchField.placeholder     = placeholder;
            _searchField.textViewport    = textArea.GetComponent<RectTransform>();
            _searchField.onValueChanged.AddListener(OnSearchChanged);

            // Refresh button
            var refreshGO = NewGO("RefreshBtn", bar.transform);
            refreshGO.GetComponent<RectTransform>().sizeDelta = new Vector2(130f, 36f);
            var refreshImg = refreshGO.AddComponent<Image>();
            refreshImg.color = BtnSecondary;
            var refreshBtn = refreshGO.AddComponent<Button>();
            refreshBtn.targetGraphic = refreshImg;
            refreshBtn.transition = Selectable.Transition.ColorTint;
            refreshBtn.colors = UITheme.CreateColorBlock(
                BtnSecondary,
                UITheme.RaisedSurface,
                UITheme.Border,
                BtnSecondary,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            refreshBtn.onClick.AddListener(OnRefresh);
            refreshGO.AddComponent<LayoutElement>().preferredWidth = 130f;

            _refreshLbl = MakeTMP("Lbl", refreshGO.transform);
            _refreshLbl.text      = LocalizationService.Get("multiplayer.action.refresh");
            _refreshLbl.fontSize  = 20;
            _refreshLbl.color     = TextPrimary;
            _refreshLbl.alignment = TextAlignmentOptions.Center;
            _refreshLbl.raycastTarget = false;
            FillRT(_refreshLbl.gameObject);
        }

        // ─────────────────────────────────────────────
        //  SCROLL AREA
        // ─────────────────────────────────────────────

        private void BuildScrollArea()
        {
            const float topOffset = 130f; // TopBar(70) + SearchBar(60)
            const float botOffset = 70f;

            var scroll = MenuScreenPrimitives.BuildVerticalScrollArea(
                _root.transform,
                offsetMin: new Vector2(0f, botOffset),
                offsetMax: new Vector2(0f, -topOffset),
                contentPadding: UITheme.Padding(UITheme.Spacing.Xxl, UITheme.Spacing.Lg),
                contentSpacing: UITheme.Spacing.Xs,
                contentAlignment: TextAnchor.UpperCenter,
                out _contentRT);
            _contentParent = _contentRT.transform;

            // Empty state label — sibling Content w Viewport (overlay gdy lista pusta).
            // Viewport jest child ScrollRect (helper hierarchia), pobieramy z `scroll.viewport`.
            var vp = scroll.viewport.gameObject;
            _emptyLbl = MakeTMP("EmptyLbl", vp.transform);
            _emptyLbl.text      = "";
            _emptyLbl.fontSize  = 24;
            _emptyLbl.color     = TextMuted;
            _emptyLbl.alignment = TextAlignmentOptions.Center;
            _emptyLbl.raycastTarget = false;
            _emptyLbl.textWrappingMode = TMPro.TextWrappingModes.Normal;
            var emptyRT = _emptyLbl.GetComponent<RectTransform>();
            emptyRT.anchorMin = new Vector2(0.1f, 0.3f);
            emptyRT.anchorMax = new Vector2(0.9f, 0.7f);
            emptyRT.offsetMin = Vector2.zero;
            emptyRT.offsetMax = Vector2.zero;
            _emptyLbl.gameObject.SetActive(false);
        }

        // ─────────────────────────────────────────────
        //  BOTTOM BAR
        // ─────────────────────────────────────────────

        private void BuildBottomBar()
        {
            var bar = NewGO("BottomBar", _root.transform);
            bar.AddComponent<Image>().color = BottomBarBg;
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 70f);

            var hl = bar.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Xxl, UITheme.Spacing.Lg);
            hl.spacing  = UITheme.Spacing.Lg;
            hl.childAlignment      = TextAnchor.MiddleRight;
            // 2026-05-17: childControlWidth/Height true (TD-029 pattern).
            hl.childControlWidth   = true;
            hl.childControlHeight  = true;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            // Flexible spacer to push button right
            var spacer = NewGO("Spacer", bar.transform);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // "Utwórz grę" button
            var createGO = NewGO("CreateBtn", bar.transform);
            createGO.GetComponent<RectTransform>().sizeDelta = new Vector2(220f, 44f);
            var createImg = createGO.AddComponent<Image>();
            createImg.color = BtnPrimary;
            var createBtn = createGO.AddComponent<Button>();
            createBtn.targetGraphic = createImg;
            createBtn.transition = Selectable.Transition.ColorTint;
            createBtn.colors = UITheme.CreateColorBlock(
                BtnPrimary,
                UITheme.PrimaryAccentHover,
                UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f),
                BtnPrimary,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            createBtn.onClick.AddListener(OnCreateGame);
            createGO.AddComponent<LayoutElement>().preferredWidth = 220f;

            _createLbl = MakeTMP("Lbl", createGO.transform);
            _createLbl.text      = LocalizationService.Get("multiplayer.action.create_game");
            _createLbl.fontSize  = 22;
            _createLbl.color     = UITheme.InverseText;
            _createLbl.fontStyle = FontStyles.Bold;
            _createLbl.alignment = TextAlignmentOptions.Center;
            _createLbl.raycastTarget = false;
            FillRT(_createLbl.gameObject);
        }

        // ─────────────────────────────────────────────
        //  POPULATE SERVER LIST
        // ─────────────────────────────────────────────

        private void PopulateServers()
        {
            foreach (Transform ch in _contentParent)
                Destroy(ch.gameObject);

            string filter = _searchText.Trim().ToLowerInvariant();
            int shown = 0;

            foreach (var s in MockServers)
            {
                if (filter.Length > 0 && !s.name.ToLowerInvariant().Contains(filter))
                    continue;

                BuildServerRow(s);
                shown++;
            }

            bool empty = shown == 0;
            _emptyLbl.gameObject.SetActive(empty);
            if (empty)
                _emptyLbl.text = LocalizationService.Get("multiplayer.empty");

            UpdateServerCount(shown);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRT);
        }

        private void BuildServerRow(ServerEntry s)
        {
            bool full = s.players >= s.maxPlayers;

            var row = NewGO(s.name, _contentParent);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 64f);
            var rowImg = row.AddComponent<Image>();
            rowImg.color = full ? RowFull : RowBg;

            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Lg, UITheme.Spacing.Sm, UITheme.Spacing.Sm);
            hl.spacing  = UITheme.Spacing.Md;
            hl.childAlignment      = TextAnchor.MiddleLeft;
            // 2026-05-17: childControlWidth/Height true (TD-029 pattern). Bez tego nameCol
            // preferredWidth=460 ignorowane → name/map/players/ping/button nakładały się.
            hl.childControlWidth   = true;
            hl.childControlHeight  = true;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            // ── Server name + map ─────────────────────
            var nameCol = NewGO("NameCol", row.transform);
            nameCol.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 48f);
            var nameVL = nameCol.AddComponent<VerticalLayoutGroup>();
            nameVL.childAlignment      = TextAnchor.MiddleLeft;
            // 2026-05-17: childControlWidth true — name + mapName layouts independent
            nameVL.childControlWidth   = true;
            nameVL.childControlHeight  = true;
            nameVL.childForceExpandWidth  = false;
            nameVL.childForceExpandHeight = false;
            nameVL.spacing = UITheme.Spacing.Xxs;
            nameCol.AddComponent<LayoutElement>().preferredWidth = 460f;

            var nameLbl = MakeTMP("Name", nameCol.transform);
            nameLbl.text      = s.name;
            nameLbl.fontSize  = 22;
            nameLbl.color     = full ? TextMuted : TextPrimary;
            nameLbl.fontStyle = FontStyles.Bold;
            nameLbl.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            nameLbl.overflowMode = TextOverflowModes.Ellipsis;
            nameLbl.raycastTarget = false;
            nameLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 460f;

            var mapLbl = MakeTMP("Map", nameCol.transform);
            mapLbl.text      = s.map;
            mapLbl.fontSize  = 17;
            mapLbl.color     = TextMuted;
            mapLbl.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            mapLbl.raycastTarget = false;
            mapLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 460f;

            // ── Players ───────────────────────────────
            var playersLbl = MakeTMP("Players", row.transform);
            playersLbl.text      = $"{s.players}/{s.maxPlayers}";
            playersLbl.fontSize  = 22;
            playersLbl.color     = full ? TextRed : TextPrimary;
            playersLbl.alignment = TextAlignmentOptions.Center;
            playersLbl.raycastTarget = false;
            playersLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 90f;

            // ── Ping ──────────────────────────────────
            var pingLbl = MakeTMP("Ping", row.transform);
            pingLbl.text     = $"{s.ping} ms";
            pingLbl.fontSize = 20;
            pingLbl.color    = PingColor(s.ping);
            pingLbl.alignment = TextAlignmentOptions.Center;
            pingLbl.raycastTarget = false;
            pingLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 70f;

            // ── Flexible spacer ───────────────────────
            var sp = NewGO("Sp", row.transform);
            sp.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // ── Join button ───────────────────────────
            var joinGO = NewGO("JoinBtn", row.transform);
            joinGO.GetComponent<RectTransform>().sizeDelta = new Vector2(110f, 40f);
            var joinImg = joinGO.AddComponent<Image>();
            joinImg.color = full ? UITheme.WithAlpha(UITheme.Border, 0.75f) : BtnPrimary;
            var joinBtn = joinGO.AddComponent<Button>();
            joinBtn.targetGraphic = joinImg;
            joinBtn.interactable  = !full;
            joinBtn.transition = Selectable.Transition.ColorTint;
            joinBtn.colors = UITheme.CreateColorBlock(
                joinImg.color,
                full ? joinImg.color : UITheme.PrimaryAccentHover,
                full ? joinImg.color : UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f),
                joinImg.color,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            joinGO.AddComponent<LayoutElement>().preferredWidth = 110f;

            string serverName = s.name;
            joinBtn.onClick.AddListener(() => OnJoinServer(serverName));

            var joinLbl = MakeTMP("Lbl", joinGO.transform);
            joinLbl.text      = LocalizationService.Get(full ? "multiplayer.action.full" : "multiplayer.action.join");
            joinLbl.fontSize  = 20;
            joinLbl.color     = full ? TextMuted : UITheme.InverseText;
            joinLbl.fontStyle = FontStyles.Bold;
            joinLbl.alignment = TextAlignmentOptions.Center;
            joinLbl.raycastTarget = false;
            FillRT(joinLbl.gameObject);

            // Hover effect (only for non-full rows)
            if (!full)
                row.AddComponent<HoverImageColor>().Init(rowImg, RowBg, RowHover);
        }

        // ─────────────────────────────────────────────
        //  HELPERS
        // ─────────────────────────────────────────────

        private void UpdateServerCount(int count = -1)
        {
            if (_serverCountLbl == null) return;
            if (count < 0) count = MockServers.Length;
            _serverCountLbl.text = LocalizationService.Get("multiplayer.server_count_format", count);
        }

        // Progi ping (ms) — wartości typowe dla European-server P2P. Powyżej 100ms ping
        // wpływa wyczuwalnie na sync w sim grze (Mirror tickrate ~50Hz = 20ms frame).
        private const int PingGoodThresholdMs = 50;
        private const int PingMediumThresholdMs = 100;

        private static Color PingColor(int ping)
        {
            if (ping <= PingGoodThresholdMs)   return PingGood;
            if (ping <= PingMediumThresholdMs) return PingMed;
            return PingBad;
        }

        private void OnSearchChanged(string value)
        {
            _searchText = value;
            PopulateServers();
        }

        private void OnRefresh()
        {
            _searchText = "";
            if (_searchField != null) _searchField.text = "";
            PopulateServers();
        }

        private static void OnJoinServer(string serverName)
        {
            // TODO (M10 Mirror): real join flow — handshake, password prompt jeśli serwer chroniony,
            // scene transition do Depot z context'em "join existing". Aktualnie tylko log dla pre-EA UI demo.
            Log.Info($"[Multiplayer] Dołączanie do serwera: {serverName} — stub do M10");
        }

        private static void OnCreateGame()
        {
            GameCreatorContext.ResetServerConfig();
            GameCreatorContext.Mode = GameCreatorContext.GameMode.Multiplayer;
            UnityEngine.SceneManagement.SceneManager.LoadScene("GameCreator");
        }

        // ─────────────────────────────────────────────
        //  PRIMITIVE HELPERS
        // ─────────────────────────────────────────────

        private static GameObject NewGO(string name, Transform parent) => MenuScreenPrimitives.NewGO(name, parent);
        private static TextMeshProUGUI MakeTMP(string name, Transform parent) => MenuScreenPrimitives.MakeTMP(name, parent);
        private static void FillRT(GameObject go) => MenuScreenPrimitives.Fill(go);
    }
}
