using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModSettingsMenu
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Plugin Entry Point
    // ─────────────────────────────────────────────────────────────────────────

    [BepInPlugin("com.github.antigravity.modsettingsmenu", "Mod Settings Menu", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource? Log;
        private Harmony? _harmony;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("Mod Settings Menu initializing...");
            try
            {
                // Spawn controller before patches fire
                var go = new GameObject("ModSettingsController");
                DontDestroyOnLoad(go);
                go.AddComponent<ModSettingsController>();

                _harmony = new Harmony("com.github.antigravity.modsettingsmenu");
                _harmony.PatchAll();
                Log.LogInfo("Mod Settings Menu ready.");
            }
            catch (Exception ex)
            {
                Log.LogError("Failed to initialize Mod Settings Menu: " + ex);
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Data Model
    // ─────────────────────────────────────────────────────────────────────────

    public class CfgEntry
    {
        public string Key            { get; set; } = "";
        public string RawValue       { get; set; } = "";
        public string PendingValue   { get; set; } = "";
        public string Section        { get; set; } = "";
        public string Description    { get; set; } = "";
        public string SettingType    { get; set; } = "";
        public string DefaultValue   { get; set; } = "";
        public bool   IsDirty        => PendingValue != RawValue;
    }

    public class CfgSection
    {
        public string Name           { get; set; } = "";
        public List<CfgEntry> Entries{ get; set; } = new List<CfgEntry>();
    }

    public class ModConfig
    {
        public string FilePath       { get; set; } = "";
        public string ModName        { get; set; } = "";
        public string PluginGuid     { get; set; } = "";
        public List<CfgSection> Sections { get; set; } = new List<CfgSection>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Config File Parser
    // ─────────────────────────────────────────────────────────────────────────

    public static class CfgParser
    {
        public static List<ModConfig> LoadAll(string configDir)
        {
            var result = new List<ModConfig>();
            if (!Directory.Exists(configDir)) return result;
            foreach (var path in Directory.GetFiles(configDir, "*.cfg"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (name.Equals("BepInEx", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var c = Parse(path);
                    if (c != null)
                    {
                        if (IsModLoaded(c))
                        {
                            result.Add(c);
                        }
                        else
                        {
                            Plugin.Log?.LogInfo($"Skipping config file for unloaded mod: {name}");
                        }
                    }
                }
                catch (Exception ex) { Plugin.Log?.LogError($"Parse error {path}: {ex.Message}"); }
            }
            return result;
        }

        private static bool IsModLoaded(ModConfig cfg)
        {
            var loadedPlugins = BepInEx.Bootstrap.Chainloader.PluginInfos;
            
            // Match 1: Plugin GUID parsed from config comments
            if (!string.IsNullOrEmpty(cfg.PluginGuid) && loadedPlugins.ContainsKey(cfg.PluginGuid))
                return true;

            // Match 2: Config filename matches a loaded Plugin GUID
            string fileName = Path.GetFileNameWithoutExtension(cfg.FilePath);
            if (loadedPlugins.ContainsKey(fileName))
                return true;

            // Loop through loaded plugins for looser matching
            foreach (var kp in loadedPlugins)
            {
                var info = kp.Value;
                if (info == null || info.Metadata == null) continue;

                // Match 3: Case-insensitive GUID checks
                if (info.Metadata.GUID.Equals(cfg.PluginGuid, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (info.Metadata.GUID.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    return true;

                // Match 4: Mod display name check
                if (info.Metadata.Name.Equals(cfg.ModName, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (info.Metadata.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    return true;

                // Match 5: Loaded DLL name matches config name (e.g. "SineusAutoInvite.dll" matches "SineusAutoInvite.cfg")
                if (!string.IsNullOrEmpty(info.Location))
                {
                    string dllName = Path.GetFileNameWithoutExtension(info.Location);
                    if (dllName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        private static ModConfig? Parse(string path)
        {
            var lines   = File.ReadAllLines(path, Encoding.UTF8);
            var cfg     = new ModConfig { FilePath = path };
            CfgSection? curSection = null;
            string desc = "", type = "", def = "";

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                if (line.StartsWith("## Settings file was created by plugin "))
                { cfg.ModName = line.Substring("## Settings file was created by plugin ".Length).Trim(); continue; }
                if (line.StartsWith("## Plugin GUID:"))
                { cfg.PluginGuid = line.Substring("## Plugin GUID:".Length).Trim(); continue; }

                // Section
                if (line.StartsWith("[") && line.EndsWith("]") && line.Length > 2)
                {
                    curSection = new CfgSection { Name = line.Substring(1, line.Length - 2) };
                    cfg.Sections.Add(curSection);
                    desc = ""; type = ""; def = "";
                    continue;
                }

                // Metadata comments
                if (line.StartsWith("## ")) { desc += (desc.Length > 0 ? " " : "") + line.Substring(3).Trim(); continue; }
                if (line.StartsWith("# Setting type:")) { type = line.Substring("# Setting type:".Length).Trim(); continue; }
                if (line.StartsWith("# Default value:")) { def  = line.Substring("# Default value:".Length).Trim(); continue; }
                if (line.StartsWith("#")) continue;

                // Key = Value
                var eq = line.IndexOf('=');
                if (eq > 0 && curSection != null)
                {
                    var key = line.Substring(0, eq).Trim();
                    var val = line.Substring(eq + 1).Trim();
                    curSection.Entries.Add(new CfgEntry
                    {
                        Key = key, RawValue = val, PendingValue = val,
                        Section = curSection.Name, Description = desc,
                        SettingType = type, DefaultValue = def
                    });
                    desc = ""; type = ""; def = "";
                }
            }

            if (cfg.ModName.Length == 0)
                cfg.ModName = Path.GetFileNameWithoutExtension(path);
            return cfg.Sections.Count > 0 ? cfg : null;
        }

        public static void Save(ModConfig mod)
        {
            var lookup = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var sec in mod.Sections)
            {
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in sec.Entries) d[e.Key] = e.PendingValue;
                lookup[sec.Name] = d;
            }

            var lines   = File.ReadAllLines(mod.FilePath, Encoding.UTF8);
            var output  = new List<string>(lines.Length);
            string cur  = "";

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.StartsWith("[") && line.EndsWith("]") && line.Length > 2)
                { cur = line.Substring(1, line.Length - 2); output.Add(rawLine); continue; }

                var eq = line.IndexOf('=');
                if (eq > 0 && !line.StartsWith("#") &&
                    lookup.TryGetValue(cur, out var sd) &&
                    sd.TryGetValue(line.Substring(0, eq).Trim(), out var nv))
                {
                    output.Add($"{line.Substring(0, eq).Trim()} = {nv}");
                    foreach (var sec in mod.Sections)
                    foreach (var ent in sec.Entries)
                        if (sec.Name == cur && ent.Key == line.Substring(0, eq).Trim())
                            ent.RawValue = nv;
                }
                else output.Add(rawLine);
            }
            File.WriteAllLines(mod.FilePath, output.ToArray(), Encoding.UTF8);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  IMGUI Overlay Controller
    // ─────────────────────────────────────────────────────────────────────────

    public class ModSettingsController : MonoBehaviour
    {
        public static ModSettingsController? Instance { get; private set; }

        private bool _visible;
        private List<ModConfig> _configs = new List<ModConfig>();
        private int _selMod;
        private Vector2 _leftScroll, _rightScroll;
        private string _status = "";
        private float _statusUntil;
        private bool _stylesBuilt;
        private float _lastEscapeMenuCheckTime = 0f;
        private string _lastLoggedScene = "";
        private readonly List<GraphicRaycaster> _disabledRaycasters = new List<GraphicRaycaster>();

        // Styles
        private GUIStyle? _header, _subHeader, _label, _desc, _btn, _btnGreen, _btnRed, _tf, _cardStyle;

        // Palette
        private static readonly Color BgPanel    = new Color(0.10f, 0.10f, 0.15f, 0.99f);
        private static readonly Color Accent     = new Color(0.40f, 0.65f, 1.00f, 1.00f);
        private static readonly Color AccentDark = new Color(0.16f, 0.28f, 0.52f, 1.00f);
        private static readonly Color SectionBg  = new Color(0.13f, 0.13f, 0.20f, 1.00f);
        private static readonly Color TextMain   = new Color(0.92f, 0.92f, 0.96f, 1.00f);
        private static readonly Color TextSub    = new Color(0.85f, 0.88f, 0.94f, 1.00f);
        private static readonly Color Dirty      = new Color(1.00f, 0.78f, 0.20f, 1.00f);
        private static readonly Color Green      = new Color(0.22f, 0.72f, 0.38f, 1.00f);
        private static readonly Color Red        = new Color(0.75f, 0.20f, 0.20f, 1.00f);

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Reload();
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void Show()
        {
            _visible = true;
            Reload();
            
            // Block background click-through by disabling all active GraphicRaycasters in the scene.
            // This prevents mouse raycasts from hitting any background canvas buttons without breaking EventSystem state.
            _disabledRaycasters.Clear();
            foreach (var raycaster in Resources.FindObjectsOfTypeAll<GraphicRaycaster>())
            {
                if (raycaster != null && raycaster.gameObject.activeInHierarchy && raycaster.enabled)
                {
                    raycaster.enabled = false;
                    _disabledRaycasters.Add(raycaster);
                }
            }
            Plugin.Log?.LogInfo($"Disabled {_disabledRaycasters.Count} GraphicRaycaster(s) to block click-through.");
        }

        public void Hide()
        {
            _visible = false;
            
            // Restore all previously disabled GraphicRaycasters to enable clicks again
            int restoredCount = 0;
            foreach (var raycaster in _disabledRaycasters)
            {
                if (raycaster != null)
                {
                    raycaster.enabled = true;
                    restoredCount++;
                }
            }
            _disabledRaycasters.Clear();
            Plugin.Log?.LogInfo($"Restored {restoredCount} GraphicRaycaster(s).");
        }

        public bool IsVisible => _visible;

        private void Reload()
        {
            _configs = CfgParser.LoadAll(Path.Combine(Paths.BepInExRootPath, "config"));
            if (_selMod >= _configs.Count) _selMod = 0;
        }

        private void DoApply()
        {
            bool err = false;
            foreach (var m in _configs)
            {
                if (!HasDirty(m)) continue;
                try { CfgParser.Save(m); }
                catch (Exception ex) { Plugin.Log?.LogError("Save failed: " + ex); err = true; }
            }
            SetStatus(err ? "✗ Some saves failed — check BepInEx log."
                          : "✔ Saved! Restart may be needed for changes to apply.");
        }

        private void DoDiscard()
        {
            foreach (var m in _configs)
            foreach (var s in m.Sections)
            foreach (var e in s.Entries)
                e.PendingValue = e.RawValue;
            SetStatus("↩ Changes discarded.");
        }

        private void SetStatus(string msg) { _status = msg; _statusUntil = Time.unscaledTime + 5f; }
        private static bool HasDirty(ModConfig m)
        { foreach (var s in m.Sections) foreach (var e in s.Entries) if (e.IsDirty) return true; return false; }
        private static int EntryCount(ModConfig m)
        { int n = 0; foreach (var s in m.Sections) n += s.Entries.Count; return n; }

        // ─── Update Loop (Dynamic Injection & Diagnostics) ───────────────────

        private void Update()
        {
            var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (sceneName != _lastLoggedScene)
            {
                _lastLoggedScene = sceneName;
                Plugin.Log?.LogInfo($"[ModSettingsMenu] Active Scene changed to: '{sceneName}'");
            }

            // Close the mod settings panel if user presses Escape key
            if (_visible && Input.GetKeyDown(KeyCode.Escape))
            {
                Hide();
                Input.ResetInputAxes(); // Consume keypress to prevent closing background settings
            }

            // Continually verify button presence in menus when active
            if (Time.unscaledTime - _lastEscapeMenuCheckTime >= 0.25f)
            {
                _lastEscapeMenuCheckTime = Time.unscaledTime;
                try
                {
                    // 1. Scan for active UISettingsScreen (handles Lobby Settings / General Settings panels)
                    var settingsScreens = Resources.FindObjectsOfTypeAll<UISettingsScreen>();
                    foreach (var screen in settingsScreens)
                    {
                        if (screen != null && screen.gameObject.activeInHierarchy)
                        {
                            ButtonInjector.InjectSettingsScreen(screen);
                        }
                    }

                    // 2. Scan for Main Menu Managers (handles Lobby Scene's title screen)
                    var mainMenus = Resources.FindObjectsOfTypeAll<MainMenuManager>();
                    foreach (var mgr in mainMenus)
                    {
                        if (mgr != null && mgr.gameObject.activeInHierarchy)
                        {
                            ButtonInjector.InjectMainMenu(mgr);
                        }
                    }

                    // 3. Scan for Escape Menus
                    var escapeMenus = Resources.FindObjectsOfTypeAll<UIEscapeMenu>();
                    foreach (var menu in escapeMenus)
                    {
                        if (menu != null && menu.gameObject.activeInHierarchy)
                        {
                            var mainPanel = ButtonInjector.GetField<GameObject>(menu, "mainPanel");
                            if (mainPanel != null && mainPanel.activeInHierarchy)
                            {
                                ButtonInjector.InjectEscapeMenu(menu);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogError("Button injector scan error: " + ex.Message);
                }
            }
        }

        // ─── IMGUI ────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_visible) return;

            // Backup the active skin and reset it to default to prevent game skin overrides in-game
            var prevSkin = GUI.skin;
            GUI.skin = null;

            if (!_stylesBuilt) BuildStyles();

            // dim background
            DrawRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.65f));

            // Expanded Window Size for spacious layout
            float W = Mathf.Min(1200f, Screen.width  * 0.92f);
            float H = Mathf.Min(800f,  Screen.height * 0.90f);
            float X = (Screen.width  - W) * 0.5f;
            float Y = (Screen.height - H) * 0.5f;

            DrawPanel(new Rect(X, Y, W, H));

            // Restore the game's active skin
            GUI.skin = prevSkin;
        }

        private void DrawPanel(Rect r)
        {
            DrawRect(r, BgPanel);
            DrawBorder(r, AccentDark, 2f);

            // ── Title bar ──
            var tb = new Rect(r.x, r.y, r.width, 50f);
            DrawRect(tb, new Color(0.10f, 0.12f, 0.20f, 1f));
            DrawRect(new Rect(r.x, r.y + 48f, r.width, 2f), Accent);

            SetColor(Accent);
            GUI.Label(new Rect(r.x + 18f, r.y + 12f, 500f, 28f), "⚙  Mod Settings", _header!);
            SetColor(Color.white);

            // Close button
            SetColor(new Color(0.75f, 0.25f, 0.25f));
            if (GUI.Button(new Rect(r.x + r.width - 44f, r.y + 10f, 32f, 30f), "✕", _btn!))
                Hide();
            SetColor(Color.white);

            float bodyY = r.y + 52f;
            float bodyH = r.height - 52f - 56f;

            // ── Left list ──
            float lW = 240f;
            DrawRect(new Rect(r.x, bodyY, lW, bodyH), new Color(0.07f, 0.07f, 0.10f, 1f));
            float innerH = Mathf.Max(bodyH, _configs.Count * 58f);
            _leftScroll = GUI.BeginScrollView(
                new Rect(r.x, bodyY, lW, bodyH),
                _leftScroll,
                new Rect(0, 0, lW - 16f, innerH));
            for (int i = 0; i < _configs.Count; i++)
                DrawModListItem(i, lW);
            GUI.EndScrollView();

            // Divider
            DrawRect(new Rect(r.x + lW, bodyY, 2f, bodyH), AccentDark);

            // ── Right content ──
            float rX = r.x + lW + 2f;
            float rW = r.width - lW - 2f;
            if (_configs.Count == 0)
            {
                SetColor(TextSub);
                GUI.Label(new Rect(rX + 20f, bodyY + 20f, rW - 40f, 40f),
                    "No mod config files found in BepInEx/config/.", _label!);
                SetColor(Color.white);
            }
            else if (_selMod < _configs.Count)
                DrawModDetail(new Rect(rX, bodyY, rW, bodyH), _configs[_selMod]);

            // ── Footer ──
            float fY = r.y + r.height - 54f;
            DrawRect(new Rect(r.x, fY, r.width, 54f), new Color(0.09f, 0.09f, 0.13f, 1f));
            DrawRect(new Rect(r.x, fY, r.width, 2f), AccentDark);

            if (_status.Length > 0 && Time.unscaledTime < _statusUntil)
            {
                bool isErr = _status.StartsWith("✗");
                SetColor(isErr ? new Color(1f, 0.35f, 0.35f) : new Color(0.3f, 0.95f, 0.5f));
                GUI.Label(new Rect(r.x + 18f, fY + 18f, r.width - 340f, 22f), _status, _label!);
                SetColor(Color.white);
            }

            float bY = fY + 10f, bH = 34f, bW = 100f;
            float bR = r.x + r.width - 16f;

            SetColor(Green);
            if (GUI.Button(new Rect(bR - bW, bY, bW, bH), "Apply", _btnGreen!))     DoApply();
            SetColor(Color.white);

            SetColor(Red);
            if (GUI.Button(new Rect(bR - bW*2f - 8f, bY, bW, bH), "Discard", _btnRed!)) DoDiscard();
            SetColor(Color.white);

            SetColor(TextSub);
            if (GUI.Button(new Rect(bR - bW*3f - 18f, bY, bW, bH), "Reload", _btn!))
            { Reload(); SetStatus("↻ Reloaded from disk."); }
            SetColor(Color.white);
        }

        private void DrawModListItem(int i, float lW)
        {
            var m        = _configs[i];
            bool sel     = i == _selMod;
            bool dirty   = HasDirty(m);
            var itemRect = new Rect(4f, i * 58f + 4f, lW - 22f, 50f);

            DrawRect(itemRect, sel ? AccentDark : new Color(0.12f, 0.12f, 0.18f));
            if (sel) DrawBorder(itemRect, Accent, 1.5f);

            string name = m.ModName.Length > 30 ? m.ModName.Substring(0, 28) + ".." : m.ModName;
            SetColor(sel ? Accent : TextMain);
            GUI.Label(new Rect(itemRect.x + 8f, itemRect.y + 7f, itemRect.width - 22f, 20f), name, _label!);
            SetColor(Color.white);

            if (dirty)
            {
                SetColor(Dirty);
                GUI.Label(new Rect(itemRect.x + itemRect.width - 18f, itemRect.y + 7f, 18f, 20f), "●", _label!);
                SetColor(Color.white);
            }

            int cnt = EntryCount(m);
            SetColor(TextSub);
            GUI.Label(new Rect(itemRect.x + 8f, itemRect.y + 28f, itemRect.width, 18f),
                $"{cnt} setting{(cnt != 1 ? "s" : "")}", _desc!);
            SetColor(Color.white);

            if (Event.current.type == EventType.MouseDown && itemRect.Contains(Event.current.mousePosition))
            {
                _selMod      = i;
                _rightScroll = Vector2.zero;
                Event.current.Use();
            }
        }

        private void DrawModDetail(Rect r, ModConfig mod)
        {
            // Mod header
            DrawRect(new Rect(r.x, r.y, r.width, 38f), new Color(0.11f, 0.13f, 0.21f, 0.9f));
            SetColor(Accent);
            GUI.Label(new Rect(r.x + 14f, r.y + 9f, r.width - 28f, 22f), mod.ModName, _subHeader!);
            SetColor(Color.white);
            if (mod.PluginGuid.Length > 0)
            {
                SetColor(TextSub);
                GUI.Label(new Rect(r.x + 14f, r.y + 26f, r.width, 14f), mod.PluginGuid, _desc!);
                SetColor(Color.white);
            }

            float scrollY = r.y + 40f;
            float scrollH = r.height - 40f;

            // Restrict GUILayout rendering area to the right content rectangle
            GUILayout.BeginArea(new Rect(r.x + 10f, scrollY, r.width - 20f, scrollH - 8f));
            _rightScroll = GUILayout.BeginScrollView(_rightScroll, GUILayout.Width(r.width - 20f), GUILayout.Height(scrollH - 8f));

            foreach (var sec in mod.Sections)
            {
                // Section Header banner
                GUILayout.Space(6f);
                var secStyle = new GUIStyle(_label!) { fontStyle = FontStyle.Bold };
                SetStyleTextColors(secStyle, Accent);
                
                GUILayout.BeginHorizontal();
                GUILayout.Space(4f);
                GUILayout.Label(sec.Name, secStyle);
                GUILayout.EndHorizontal();
                
                // Draw line under section header
                var lineRect = GUILayoutUtility.GetRect(r.width - 24f, 2f);
                DrawRect(lineRect, AccentDark);
                GUILayout.Space(6f);

                foreach (var e in sec.Entries)
                {
                    bool dirty = e.IsDirty;
                    
                    // Render row as a dynamic card layout
                    GUILayout.BeginVertical(_cardStyle!);
                    GUILayout.BeginHorizontal();
                    
                    // Left Column: Key & Description (58% width)
                    GUILayout.BeginVertical(GUILayout.Width((r.width - 60f) * 0.58f));
                    var labelStyle = new GUIStyle(_label!) {
                        fontStyle = FontStyle.Bold,
                        normal = { textColor = dirty ? Dirty : TextMain }
                    };
                    GUILayout.Label(e.Key, labelStyle);
                    if (e.Description.Length > 0)
                    {
                        var descStyle = new GUIStyle(_desc!) { wordWrap = true };
                        GUILayout.Label(e.Description, descStyle);
                    }
                    GUILayout.EndVertical();
                    
                    GUILayout.Space(14f);

                    // Right Column: Controls & Metadata (37% width)
                    GUILayout.BeginVertical(GUILayout.Width((r.width - 60f) * 0.37f));
                    if (e.SettingType == "Boolean")
                    {
                        bool cur = e.PendingValue.Equals("true", StringComparison.OrdinalIgnoreCase);
                        GUILayout.BeginHorizontal();
                        bool nxt = GUILayout.Toggle(cur, "");
                        var toggleLblStyle = new GUIStyle(_label!) {
                            normal = { textColor = cur ? Green : TextSub }
                        };
                        GUILayout.Label(cur ? "Enabled" : "Disabled", toggleLblStyle);
                        GUILayout.EndHorizontal();
                        if (nxt != cur) e.PendingValue = nxt ? "true" : "false";
                    }
                    else
                    {
                        GUI.SetNextControlName($"tf_{e.Section}_{e.Key}");
                        var nv = GUILayout.TextField(e.PendingValue, _tf!, GUILayout.MinWidth(180f));
                        if (nv != e.PendingValue) e.PendingValue = nv;
                    }

                    // Metadata labels (Type & Default) with separation
                    GUILayout.Space(6f);
                    if (e.SettingType.Length > 0)
                    {
                        var typeStyle = new GUIStyle(_desc!) { normal = { textColor = TextSub } };
                        GUILayout.Label("Type: " + e.SettingType, typeStyle);
                    }
                    if (e.DefaultValue.Length > 0)
                    {
                        var defStyle = new GUIStyle(_desc!) { normal = { textColor = new Color(0.78f, 0.82f, 0.90f, 1f) } };
                        GUILayout.Label("Default: " + e.DefaultValue, defStyle);
                    }
                    
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                    
                    // Draw card border on Repaint event
                    if (Event.current.type == EventType.Repaint)
                    {
                        var boxRect = GUILayoutUtility.GetLastRect();
                        DrawBorder(boxRect, dirty ? Dirty : AccentDark, 1f);
                    }
                    
                    GUILayout.Space(4f);
                }
                GUILayout.Space(6f);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ─── Drawing helpers ──────────────────────────────────────────────────

        private static void DrawRect(Rect r, Color c)
        {
            var p = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = p;
        }

        private static void DrawBorder(Rect r, Color c, float t)
        {
            DrawRect(new Rect(r.x,           r.y,               r.width, t), c);
            DrawRect(new Rect(r.x,           r.y + r.height-t,  r.width, t), c);
            DrawRect(new Rect(r.x,           r.y,               t, r.height), c);
            DrawRect(new Rect(r.x+r.width-t, r.y,               t, r.height), c);
        }

        private static void SetColor(Color c) { GUI.color = c; }

        private static void SetStyleTextColors(GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            style.onNormal.textColor = color;
            style.onHover.textColor = color;
            style.onActive.textColor = color;
            style.onFocused.textColor = color;
        }

        // ─── Style building ───────────────────────────────────────────────────

        private void BuildStyles()
        {
            _stylesBuilt = true;

            // Generate texture and mark it as DontSave so Unity does not destroy it on scene load/match starts!
            Texture2D Mk(Color c) { 
                var t = new Texture2D(1,1); 
                t.SetPixel(0,0,c); 
                t.Apply(); 
                t.hideFlags = HideFlags.DontSave; 
                return t; 
            }

            _header = new GUIStyle() {
                fontSize = 20, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            SetStyleTextColors(_header, Accent);

            _subHeader = new GUIStyle() {
                fontSize = 14, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            SetStyleTextColors(_subHeader, Accent);

            _label = new GUIStyle() {
                fontSize = 12, wordWrap = false,
                alignment = TextAnchor.MiddleLeft
            };
            SetStyleTextColors(_label, TextMain);

            _desc = new GUIStyle() {
                fontSize = 11, wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };
            SetStyleTextColors(_desc, TextSub);

            // Button style based on default Unity skin
            var btnBase = new GUIStyle(GUI.skin.button) {
                fontSize = 12, fontStyle = FontStyle.Bold,
                padding  = new RectOffset(8,8,4,4),
                border   = new RectOffset(4,4,4,4),
                normal   = { background = Mk(new Color(0.18f,0.18f,0.28f)) },
                hover    = { background = Mk(new Color(0.24f,0.24f,0.38f)) },
                active   = { background = Mk(AccentDark) },
            };
            _btn = btnBase;
            SetStyleTextColors(_btn, TextMain);

            _btnGreen = new GUIStyle(btnBase) {
                normal  = { background = Mk(Green) },
                hover   = { background = Mk(new Color(0.28f,0.82f,0.44f)) },
                active  = { background = Mk(new Color(0.16f,0.55f,0.28f)) },
            };
            SetStyleTextColors(_btnGreen, new Color(0.08f, 0.08f, 0.12f, 1f)); // Dark text for Apply button

            _btnRed = new GUIStyle(btnBase) {
                normal  = { background = Mk(Red) },
                hover   = { background = Mk(new Color(0.88f,0.28f,0.28f)) },
                active  = { background = Mk(new Color(0.58f,0.14f,0.14f)) },
            };
            SetStyleTextColors(_btnRed, new Color(0.08f, 0.08f, 0.12f, 1f)); // Dark text for Discard button

            // Text field style based on default Unity skin
            _tf = new GUIStyle(GUI.skin.textField) {
                fontSize = 12, padding = new RectOffset(6,6,4,4),
                normal   = { background = Mk(new Color(0.07f,0.07f,0.12f)) },
                focused  = { background = Mk(new Color(0.10f,0.10f,0.22f)) },
                hover    = { background = Mk(new Color(0.09f,0.09f,0.15f)) },
            };
            SetStyleTextColors(_tf, TextMain);

            // Card Style for Entry Background
            var cardBg = Mk(new Color(0.12f, 0.12f, 0.18f, 0.65f));
            _cardStyle = new GUIStyle() {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(4, 4, 4, 4),
                normal = { background = cardBg }
            };
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Harmony Patches — Button Injection
    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(UISettingsScreen), "OnEnable")]
    public static class Patch_SettingsScreen_OnEnable
    {
        [HarmonyPostfix]
        public static void Postfix(UISettingsScreen __instance)
        {
            try { ButtonInjector.InjectSettingsScreen(__instance); }
            catch (Exception ex) { Plugin.Log?.LogError("SettingsScreen OnEnable inject: " + ex); }
        }
    }

    [HarmonyPatch(typeof(UIEscapeMenu), nameof(UIEscapeMenu.ShowMainPanel))]
    public static class Patch_EscapeMenu_ShowMainPanel
    {
        [HarmonyPostfix]
        public static void Postfix(UIEscapeMenu __instance)
        {
            try { ButtonInjector.InjectEscapeMenu(__instance); }
            catch (Exception ex) { Plugin.Log?.LogError("EscapeMenu inject: " + ex); }
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.ShowMainMenu))]
    public static class Patch_MainMenu_ShowMainMenu
    {
        [HarmonyPostfix]
        public static void Postfix(MainMenuManager __instance)
        {
            try { ButtonInjector.InjectMainMenu(__instance); }
            catch (Exception ex) { Plugin.Log?.LogError("MainMenu inject: " + ex); }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Button Injector
    // ─────────────────────────────────────────────────────────────────────────

    public static class ButtonInjector
    {
        public const string BtnName = "ModSettingsMenuButton";

        /// <summary>Inject into standard UISettingsScreen (lobby & global settings panels).</summary>
        public static void InjectSettingsScreen(UISettingsScreen screen)
        {
            var applyBtn = GetField<Button>(screen, "applyButtonControl");
            if (applyBtn == null) return;

            var parent = applyBtn.transform.parent;
            if (parent == null || parent.Find(BtnName) != null) return;

            // Find the defaults button to position exactly between them
            Button? defaultsBtn = FindDefaultsButton(screen);
            int siblingIndex = applyBtn.transform.GetSiblingIndex() + 1;
            if (defaultsBtn != null && defaultsBtn.transform.parent == parent)
            {
                int applyIdx = applyBtn.transform.GetSiblingIndex();
                int defIdx = defaultsBtn.transform.GetSiblingIndex();
                siblingIndex = Mathf.Min(applyIdx, defIdx) + 1;
            }

            CloneButton(applyBtn, parent, siblingIndex, "Mod Settings",
                () => { ModSettingsController.Instance?.Show(); }, defaultsBtn);

            Plugin.Log?.LogInfo("Mod Settings button injected into UISettingsScreen between Apply/Defaults.");
        }

        /// <summary>Inject into the Escape/Pause menu's main panel.</summary>
        public static void InjectEscapeMenu(UIEscapeMenu menu)
        {
            var mainPanel = GetField<GameObject>(menu, "mainPanel");
            if (mainPanel == null) return;

            var source = FindTemplateButton(mainPanel.transform);
            if (source == null) { Plugin.Log?.LogWarning("No suitable template button found in EscapeMenu."); return; }

            var parent = source.transform.parent;
            if (parent == null || parent.Find(BtnName) != null) return;

            // Keep the Escape Menu open when Mod Settings button is clicked.
            // This maintains the game's paused state in the background.
            CloneButton(source, parent, source.transform.GetSiblingIndex() + 1, "Mod Settings",
                () => { ModSettingsController.Instance?.Show(); });

            Plugin.Log?.LogInfo($"Mod Settings button added to EscapeMenu using '{source.name}' template.");
        }

        /// <summary>Inject into the main menu's button list.</summary>
        public static void InjectMainMenu(MainMenuManager mgr)
        {
            var settingsBtn = GetField<Button>(mgr, "settingsButton");
            if (settingsBtn == null) return;
            var parent = settingsBtn.transform.parent;
            if (parent == null || parent.Find(BtnName) != null) return;

            CloneButton(settingsBtn, parent, settingsBtn.transform.GetSiblingIndex() + 1, "Mod Settings",
                () => ModSettingsController.Instance?.Show());

            Plugin.Log?.LogInfo("Mod Settings button added to MainMenu.");
        }

        // ── helpers ──

        private static Button? FindDefaultsButton(UISettingsScreen screen)
        {
            foreach (var b in screen.GetComponentsInChildren<Button>(true))
            {
                string name = b.name.ToLower();
                if (name.Contains("default") || name.Contains("restore") || name.Contains("reset"))
                    return b;

                var tmp = b.GetComponentInChildren<TMPro.TMP_Text>(true);
                if (tmp != null && tmp.text.ToLower().Contains("default"))
                    return b;

                var txt = b.GetComponentInChildren<Text>(true);
                if (txt != null && txt.text.ToLower().Contains("default"))
                    return b;
            }
            return null;
        }

        private static void CloneButton(Component source, Transform parent, int siblingIndex,
            string label, UnityEngine.Events.UnityAction onClick, Component? defaultsBtn = null)
        {
            var go  = UnityEngine.Object.Instantiate(source.gameObject, parent);
            go.name = BtnName;
            go.transform.SetSiblingIndex(siblingIndex);
            
            // Force cloned button to be active
            go.SetActive(true);

            // Handle manual absolute coordinates if parent has no LayoutGroup
            var rectSource = source.GetComponent<RectTransform>();
            var rectMine = go.GetComponent<RectTransform>();
            if (rectSource != null && rectMine != null)
            {
                var layoutGroup = parent.GetComponent<LayoutGroup>();
                if (layoutGroup == null)
                {
                    var rectDefault = defaultsBtn?.GetComponent<RectTransform>();
                    if (rectDefault != null && defaultsBtn != null && defaultsBtn.transform.parent == parent)
                    {
                        // Put it mathematically at the midpoint between the source (Apply) and Defaults button
                        rectMine.anchoredPosition = new Vector2(
                            (rectSource.anchoredPosition.x + rectDefault.anchoredPosition.x) * 0.5f,
                            (rectSource.anchoredPosition.y + rectDefault.anchoredPosition.y) * 0.5f
                        );
                        Plugin.Log?.LogInfo($"[ModSettingsMenu] Positioned button absolutely at midpoint: {rectMine.anchoredPosition} between Apply ({rectSource.anchoredPosition}) and Defaults ({rectDefault.anchoredPosition})");
                    }
                    else
                    {
                        // Fallback shift to the right by 170f
                        rectMine.anchoredPosition = new Vector2(
                            rectSource.anchoredPosition.x + 170f,
                            rectSource.anchoredPosition.y
                        );
                        Plugin.Log?.LogInfo($"[ModSettingsMenu] Positioned button absolutely with right-shift: {rectMine.anchoredPosition} from Apply ({rectSource.anchoredPosition})");
                    }
                }
            }

            // Clean up sub-avatar/hero graphics
            var images = go.GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                if (img.gameObject != go && (img.name.ToLower().Contains("avatar") || img.name.ToLower().Contains("hero") || img.name.ToLower().Contains("portrait")))
                {
                    img.gameObject.SetActive(false);
                }
            }

            // Strip game-specific behavior scripts (like UISelectableEntry, GameObjectLocalizer)
            // that otherwise override the text, translations, or click events.
            foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (c == null) continue;
                string ns = c.GetType().Namespace ?? "";
                string name = c.GetType().Name;

                // Keep Unity standard UI components and TextMeshPro
                if (ns.StartsWith("UnityEngine") || ns.StartsWith("TMPro") ||
                    name == "Button" || name == "Image" || name == "Text" || name == "TMP_Text")
                {
                    // But explicitly destroy any localization/localizer scripts
                    string lowerName = name.ToLower();
                    if (lowerName.Contains("localize") || lowerName.Contains("localizer") || 
                        lowerName.Contains("localization") || lowerName.Contains("translation"))
                    {
                        Plugin.Log?.LogInfo($"Destroyed localization component: {c.GetType().FullName}");
                        UnityEngine.Object.Destroy(c);
                    }
                    continue;
                }

                // Destroy custom game scripts that will override behaviors
                Plugin.Log?.LogInfo($"Destroyed custom game script: {c.GetType().FullName}");
                UnityEngine.Object.Destroy(c);
            }

            // Disable shortcut/keyboard/gamepad glyph indicators (like the circle containing key glyphs like 'A')
            var allTransforms = go.GetComponentsInChildren<Transform>(true);
            foreach (var t in allTransforms)
            {
                if (t == go.transform) continue;
                string tname = t.name.ToLower();
                
                // Gamepad key identifiers (length <= 2, e.g. "a", "b", "x", "y", "lb", "rt")
                // Or standard shortcut elements (circle, icon, glyph, keyboard, gamepad, controller, key, hint, prompt, image)
                if (tname.Length <= 2 ||
                    tname.Contains("circle") || tname.Contains("glyph") || 
                    tname.Contains("icon") || tname.Contains("gamepad") || 
                    tname.Contains("controller") || tname.Contains("keyboard") || 
                    tname.Contains("key") || tname.Contains("hint") || 
                    tname.Contains("prompt") || tname.Contains("button") ||
                    tname.Contains("image"))
                {
                    // Only skip deactivating if it is the EXACT main label text element!
                    bool isMainLabel = tname == "text (tmp)" || tname == "text" || tname == "label";
                    if (!isMainLabel)
                    {
                        // Also do not disable background panels/frame graphics
                        bool isBackgroundOrFrame = tname.Contains("background") || tname.Contains("highlight") ||
                                                   tname.Contains("frame") || tname.Contains("border") ||
                                                   tname.Contains("base") || tname.Contains("visual");
                        if (!isBackgroundOrFrame)
                        {
                            Plugin.Log?.LogInfo($"Disabling shortcut icon component/child: {t.name}");
                            t.gameObject.SetActive(false);
                            continue;
                        }
                    }
                }

                // If it is any text component that is NOT the main label, disable its GameObject (e.g. shortcut text "A")
                // AND also disable its parent (which is the circle container/glyph graphic!)
                var tmpText = t.GetComponent<TMPro.TMP_Text>();
                if (tmpText != null && tname != "text (tmp)" && tname != "text" && tname != "label")
                {
                    Plugin.Log?.LogInfo($"Disabling extra text child: {t.name} (Text: '{tmpText.text}')");
                    t.gameObject.SetActive(false);
                    if (t.parent != null && t.parent != go.transform)
                    {
                        Plugin.Log?.LogInfo($"Disabling extra text child parent: {t.parent.name}");
                        t.parent.gameObject.SetActive(false);
                    }
                }

                var unityText = t.GetComponent<Text>();
                if (unityText != null && tname != "text (tmp)" && tname != "text" && tname != "label")
                {
                    Plugin.Log?.LogInfo($"Disabling extra text child: {t.name} (Text: '{unityText.text}')");
                    t.gameObject.SetActive(false);
                    if (t.parent != null && t.parent != go.transform)
                    {
                        Plugin.Log?.LogInfo($"Disabling extra text child parent: {t.parent.name}");
                        t.parent.gameObject.SetActive(false);
                    }
                }
            }

            // Explicitly set the label now that localization/custom scripts are stripped.
            // Target ONLY exact/main label texts, avoiding shortcut texts like Text (TMP)_1.
            var tmps = go.GetComponentsInChildren<TMPro.TMP_Text>(true);
            foreach (var tmp in tmps)
            {
                string name = tmp.gameObject.name.ToLower();
                if ((name == "text (tmp)" || name == "text" || name == "label") && 
                    !name.Contains("keyboard") && !name.Contains("key") && !name.Contains("shortcut"))
                {
                    tmp.text = label;
                }
            }

            var txts = go.GetComponentsInChildren<Text>(true);
            foreach (var t in txts)
            {
                string name = t.gameObject.name.ToLower();
                if ((name == "text (tmp)" || name == "text" || name == "label") && 
                    !name.Contains("keyboard") && !name.Contains("key") && !name.Contains("shortcut"))
                {
                    t.text = label;
                }
            }

            // Wire click
            var btn = go.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(onClick);
        }

        public static T? GetField<T>(object obj, string name) where T : class
        {
            var f = obj.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f?.GetValue(obj) as T;
        }

        private static Button? FindTemplateButton(Transform root)
        {
            var buttons = root.GetComponentsInChildren<Button>(true);

            // Priority 1: Settings button
            foreach (var b in buttons)
            {
                string name = b.name.ToLower();
                if ((name.Contains("settings") || name.Contains("option") || name.Contains("config")) &&
                    !name.Contains("hero") && !name.Contains("portrait") && !name.Contains("avatar"))
                    return b;
            }

            // Priority 2: Resume/Continue button
            foreach (var b in buttons)
            {
                string name = b.name.ToLower();
                if ((name.Contains("resume") || name.Contains("continue")) &&
                    !name.Contains("hero") && !name.Contains("portrait") && !name.Contains("avatar"))
                    return b;
            }

            // Priority 3: Other standard menu buttons (MainMenu, Quit, Exit, Back)
            foreach (var b in buttons)
            {
                string name = b.name.ToLower();
                if ((name.Contains("menu") || name.Contains("quit") || name.Contains("exit") || name.Contains("back")) &&
                    !name.Contains("hero") && !name.Contains("portrait") && !name.Contains("avatar"))
                    return b;
            }

            // Priority 4: Any button that is not a hero/portrait/avatar/kick/close
            foreach (var b in buttons)
            {
                string name = b.name.ToLower();
                if (!name.Contains("hero") && !name.Contains("portrait") && !name.Contains("avatar") &&
                    !name.Contains("kick") && !name.Contains("close") && !name.Contains("player"))
                    return b;
            }

            return null;
        }
    }
}
