using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlackBox.Editor
{
    internal static class BlackBoxPreferencesProvider
    {
        private const string PreferencesPath = "Preferences/BlackBox";

        private const float LabelWidth = 250;

        [SettingsProvider]
        private static SettingsProvider CreatePreferencesProvider()
        {
            SettingsProvider provider = new(PreferencesPath, SettingsScope.User)
            {
                activateHandler = OnActivate,
                keywords = new HashSet<string> { "Prefab", "Blackbox", "Lock", "Unlock" }
            };

            return provider;
        }

        private static void OnActivate(string searchContext, VisualElement rootElement)
        {
            VisualElement prefsContainer = new()
            {
                style =
                {
                    marginTop = 2,
                    marginLeft = 9
                }
            };

            Label title = new("BlackBox")
            {
                style =
                {
                    fontSize = 18,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 12
                }
            };
            prefsContainer.Add(title);

            prefsContainer.Add(CreateToggle(BlackBoxSettings.DisableLocking,
                BlackBoxSettings.DisableLockingName,
                BlackBoxSettings.DisableLockingDesc));

            prefsContainer.Add(CreateToggle(BlackBoxSettings.UnlockInPlayMode,
                BlackBoxSettings.UnlockInPlayModeName,
                BlackBoxSettings.UnlockInPlayModeDesc));

            rootElement.Add(prefsContainer);
        }

        private static Toggle CreateToggle(UserPref<bool> setting, string label, string tooltip)
        {
            Toggle toggle = new(label)
            {
                value = setting.value,
                tooltip = tooltip
            };
            toggle.labelElement.style.minWidth = LabelWidth;

            toggle.RegisterValueChangedCallback(evt =>
            {
                setting.SetValue(evt.newValue, true);
                BlackBoxSettingsProvider.OnSettingsSaved();
            });

            return toggle;
        }
    }
}