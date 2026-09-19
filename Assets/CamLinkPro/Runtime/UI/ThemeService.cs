using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using CamLinkPro.Preferences;

namespace CamLinkPro.UI
{
    /// Central place that makes text size / boldness actually reach every
    /// screen. Screens author text with role classes (text-display, text-title,
    /// ...) instead of ad hoc inline font sizes; ThemeService rewrites those
    /// sizes (and touch-target padding) from AppPrefs every time a screen is
    /// mounted, and can be re-run live while a screen is already showing.
    public static class ThemeService
    {
        public static readonly Dictionary<string, float> RoleBaseSize = new()
        {
            { "text-display", 40f },
            { "text-title", 24f },
            { "text-subtitle", 18f },
            { "text-body", 15f },
            { "text-caption", 12f },
            { "text-micro", 11f },
            { "text-mono", 14f },
        };

        const float BaseButtonPaddingV = 14f;
        const float BaseButtonPaddingH = 32f;

        public static void Apply(VisualElement root)
        {
            var scale = Mathf.Clamp(AppPrefs.FontScale.Value, 0.9f, 1.1f);
            var bold = AppPrefs.BoldTextEnabled.Value;

            foreach (var (className, baseSize) in RoleBaseSize)
            {
                root.Query(className: className).ForEach(el =>
                {
                    el.style.fontSize = Mathf.Round(baseSize * scale);
                    el.style.unityFontStyleAndWeight = bold
                        ? new StyleEnum<FontStyle>(FontStyle.Bold)
                        : StyleKeyword.Null;
                });
            }

            // Touch targets grow with text so buttons stay comfortably tappable
            // instead of clipping enlarged labels.
            var growth = 1f + (scale - 1f) * 0.6f;
            foreach (var className in new[] { "button-primary", "button-secondary", "button-danger" })
            {
                root.Query(className: className).ForEach(el =>
                {
                    el.style.paddingTop = Mathf.Round(BaseButtonPaddingV * growth);
                    el.style.paddingBottom = Mathf.Round(BaseButtonPaddingV * growth);
                    el.style.paddingLeft = Mathf.Round(BaseButtonPaddingH * growth);
                    el.style.paddingRight = Mathf.Round(BaseButtonPaddingH * growth);
                });
            }
        }
    }
}
