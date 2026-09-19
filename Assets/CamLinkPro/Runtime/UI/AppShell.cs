using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using CamLinkPro.Ar;
using CamLinkPro.Preferences;
using CamLinkPro.UI.Screens;

namespace CamLinkPro.UI
{
    /// Single persistent UIDocument for the whole app. Screens are UXML files
    /// loaded from Resources and instantiated into ContentContainer; each
    /// screen gets a small dedicated controller (see IScreenController)
    /// instead of one monolithic HUD-controller class.
    [RequireComponent(typeof(UIDocument))]
    public class AppShell : MonoBehaviour
    {
        readonly Dictionary<ScreenId, string> _uxmlResourcePaths = new()
        {
            { ScreenId.Landing, "UI/Screens/Landing" },
            { ScreenId.SettingsGeneral, "UI/Screens/Settings" },
            { ScreenId.ArPreflight, "UI/Screens/ArPreflight" },
            { ScreenId.RecordingHud, "UI/Screens/RecordingHud" },
            { ScreenId.ScanQr, "UI/Screens/ScanQr" },
            { ScreenId.QrTimeout, "UI/Screens/QrTimeout" },
            { ScreenId.StorageFull, "UI/Screens/StorageFull" },
        };

        readonly Dictionary<ScreenId, Func<IScreenController>> _controllerFactories = new()
        {
            { ScreenId.Landing, () => new LandingScreen() },
            { ScreenId.SettingsGeneral, () => new SettingsScreen() },
            { ScreenId.ArPreflight, () => new ArPreflightScreen() },
            { ScreenId.RecordingHud, () => new RecordingHudScreen() },
            { ScreenId.ScanQr, () => new ScanQrScreen() },
            { ScreenId.QrTimeout, () => new QrTimeoutScreen() },
            { ScreenId.StorageFull, () => new StorageFullScreen() },
        };

        VisualElement _contentContainer;
        IScreenController _activeController;

        public ArSessionController ArSession { get; private set; }

        /// Settings is reachable from more than one screen; whoever navigates
        /// there sets this first so Settings' Back button returns to wherever
        /// it was actually opened from instead of always going Home.
        public ScreenId SettingsReturnTo { get; set; } = ScreenId.Landing;

        VisualElement _currentScreenRoot;

        void Awake()
        {
            PresetService.CaptureDefaultsIfMissing();

            var doc = GetComponent<UIDocument>();
            var panelSettings = Resources.Load<PanelSettings>("UI/AppPanelSettings");
            ApplyDensityScale(panelSettings);
            doc.panelSettings = panelSettings;
            _contentContainer = doc.rootVisualElement;

            ArSession = gameObject.AddComponent<ArSessionController>();
        }

        /// UI Toolkit's ScaleWithScreenSize only accounts for resolution ratio
        /// against the reference resolution — it has no idea about the device's
        /// physical pixel density. On a ~400dpi phone that leaves every authored
        /// pixel size rendering near 1:1 with device pixels, i.e. physically
        /// tiny. Scale the panel roughly the way Android scales dp->px
        /// (dpi/160), damped by DensityFactor — the full 1:1 dp convention
        /// shrinks the logical canvas so much that our authored paddings
        /// (sized for a much larger logical canvas) overflow and overlap.
        const float DensityFactor = 0.65f;

        static void ApplyDensityScale(PanelSettings settings)
        {
            if (settings == null)
                return;
            var dpi = Screen.dpi;
            settings.scale = dpi > 0f ? Mathf.Clamp(dpi / 160f * DensityFactor, 1f, 2.4f) : 1.8f;
        }

        void Start()
        {
            Navigate(ScreenId.Landing);
        }

        public void Navigate(ScreenId screenId)
        {
            _activeController?.Unmount();
            _contentContainer.Clear();

            if (!_uxmlResourcePaths.TryGetValue(screenId, out var path))
            {
                Debug.LogError($"[AppShell] No UXML registered for {screenId}");
                return;
            }

            var asset = Resources.Load<VisualTreeAsset>(path);
            if (asset == null)
            {
                Debug.LogError($"[AppShell] Could not load UXML resource at {path}");
                return;
            }

            var screenRoot = asset.Instantiate();
            screenRoot.style.flexGrow = 1;
            // The wrapper TemplateContainer itself has no background — without
            // this, whatever renders behind the panel (the AR camera
            // passthrough on the HUD/preflight screens) bleeds through in the
            // safe-area gutter, which is padding on THIS element, not on the
            // screen's own opaque .screen-root child.
            screenRoot.style.backgroundColor = new StyleColor(new Color(13f / 255f, 12f / 255f, 11f / 255f));
            ApplySafeArea(screenRoot);
            _contentContainer.Add(screenRoot);
            _currentScreenRoot = screenRoot;

            _activeController = _controllerFactories[screenId]();
            _activeController.Mount(screenRoot, this);

            // Applied after Mount so elements a screen builds dynamically
            // (recent-connection chips, tab panels, etc.) get themed too.
            ThemeService.Apply(screenRoot);
        }

        /// Re-runs typography scaling on the currently visible screen — used by
        /// Settings so a font-size/boldness change is felt immediately instead
        /// of only on the next navigation.
        public void ReapplyTheme()
        {
            if (_currentScreenRoot != null)
                ThemeService.Apply(_currentScreenRoot);
        }

        /// Used by Settings so toggling Safe Area is felt immediately.
        public void ReapplySafeArea()
        {
            if (_currentScreenRoot != null)
                ApplySafeArea(_currentScreenRoot);
        }

        void ApplySafeArea(VisualElement screenRoot)
        {
            void Apply()
            {
                if (!AppPrefs.SafeAreaEnabled.Value)
                {
                    screenRoot.style.paddingLeft = 0;
                    screenRoot.style.paddingRight = 0;
                    screenRoot.style.paddingTop = 0;
                    screenRoot.style.paddingBottom = 0;
                    return;
                }

                var panel = screenRoot.panel;
                var ppp = panel != null && panel.scaledPixelsPerPoint > 0f ? panel.scaledPixelsPerPoint : 1f;
                var safe = Screen.safeArea;
                screenRoot.style.paddingLeft = safe.xMin / ppp;
                screenRoot.style.paddingRight = (Screen.width - safe.xMax) / ppp;
                screenRoot.style.paddingTop = (Screen.height - safe.yMax) / ppp;
                screenRoot.style.paddingBottom = safe.yMin / ppp;
            }

            if (screenRoot.panel != null)
                Apply();
            else
                screenRoot.RegisterCallback<AttachToPanelEvent>(_ => Apply());
        }
    }
}
