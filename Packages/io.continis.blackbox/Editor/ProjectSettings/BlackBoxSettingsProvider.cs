using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlackBox.Editor
{
    internal static class BlackBoxSettingsProvider
    {
        private const string SettingsPath = "Project/BlackBox";

        private const float LabelWidth = 250;

        [SettingsProvider]
        private static SettingsProvider CreateSettingsProvider()
        {
            SettingsProvider provider = new(SettingsPath, SettingsScope.Project)
            {
                activateHandler = OnActivate,
                keywords = new HashSet<string>
                {
                    "Prefab", "Prefabs", "Blackbox", "Encapsulation", "Lock", "Temp",
                    "Unlock", "Transform", "Nested", "Root", "Variant"
                }
            };

            return provider;
        }

        private static void OnActivate(string searchContext, VisualElement rootElement)
        {
            VisualElement container = new()
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
            container.Add(title);

            // Auto-add BlackBox Component
            container.Add(CreateCategoryHeader(BlackBoxSettings.AutoAddCategory));
            container.Add(CreateToggle(BlackBoxSettings.AutoAddToPrefabs,
                BlackBoxSettings.ToNewPrefabsName, BlackBoxSettings.AutoAddToPrefabsDesc));
            container.Add(CreateToggle(BlackBoxSettings.AutoAddToVariants,
                BlackBoxSettings.ToNewPrefabVariantsName, BlackBoxSettings.AutoAddToVariantsDesc));

            // Defaults for New BlackBoxes
            container.Add(CreateCategoryHeader(BlackBoxSettings.DefaultsCategory));
            container.Add(CreateToggle(BlackBoxSettings.LockedByDefault,
                BlackBoxSettings.LockedName, BlackBoxSettings.LockedDesc));
            container.Add(CreateToggle(BlackBoxSettings.ApplyDisabledByDefault,
                BlackBoxSettings.DisableApplyName, BlackBoxSettings.DisableApplyDesc));
            container.Add(CreateToggle(BlackBoxSettings.HideTransformByDefault,
                BlackBoxSettings.HideTransformName, BlackBoxSettings.HideTransformDesc));
            container.Add(CreateToggle(BlackBoxSettings.UnlockWhenNestedByDefault,
                BlackBoxSettings.UnlockWhenNestedName, BlackBoxSettings.UnlockWhenNestedDesc));
            container.Add(CreateToggle(BlackBoxSettings.UnlockIfVariantRootByDefault,
                BlackBoxSettings.UnlockIfVariantRootName, BlackBoxSettings.UnlockIfVariantRootDesc));
            container.Add(CreateEnumField(BlackBoxSettings.DefaultSelectionType,
                BlackBoxSettings.SelectionTypeName, BlackBoxSettings.SelectionTypeDesc));

            // Workflows
            container.Add(CreateCategoryHeader(BlackBoxSettings.WorkflowsCategory));
            container.Add(CreateToggle(BlackBoxSettings.EnableTempUnlocking,
                BlackBoxSettings.EnableTempUnlockingName, BlackBoxSettings.EnableTempUnlockingDesc));

            rootElement.Add(container);
        }

        private static Label CreateCategoryHeader(string text)
        {
            return new Label(text)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginTop = 8,
                    marginBottom = 4
                }
            };
        }

        private static Toggle CreateToggle(PackageSetting<bool> setting, string label, string tooltip)
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
                OnSettingsSaved();
            });

            return toggle;
        }

        private static EnumField CreateEnumField(PackageSetting<SelectionType> setting, string label, string tooltip)
        {
            EnumField field = new(label, setting.value)
            {
                tooltip = tooltip
            };
            field.labelElement.style.minWidth = LabelWidth;

            field.RegisterValueChangedCallback(evt =>
            {
                setting.SetValue((SelectionType)evt.newValue, true);
                OnSettingsSaved();
            });

            return field;
        }

        internal static void OnSettingsSaved()
        {
            SaveSettingsRuntimeSide();
            SceneWatcher.UpdateAllPrefabsInScene();
        }

        [InitializeOnLoadMethod]
        private static void SaveSettingsRuntimeSide()
        {
            BlackBox.LockingDisabled = BlackBoxSettings.DisableLocking.value;
            BlackBox.LockedDefault = BlackBoxSettings.LockedByDefault.value;
            BlackBox.DefaultSelectionType = BlackBoxSettings.DefaultSelectionType.value;
            BlackBox.ApplyDisabledByDefault = BlackBoxSettings.ApplyDisabledByDefault.value;
            BlackBox.HideTransformByDefault = BlackBoxSettings.HideTransformByDefault.value;
            BlackBox.UnlockWhenNestedByDefault = BlackBoxSettings.UnlockWhenNestedByDefault.value;
            BlackBox.UnlockIfVariantRootByDefault = BlackBoxSettings.UnlockIfVariantRootByDefault.value;
            BlackBox.UnlockInPlayMode = BlackBoxSettings.UnlockInPlayMode.value;
        }
    }
}