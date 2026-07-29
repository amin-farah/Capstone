namespace BlackBox.Editor
{
    public static class BlackBoxSettings
    {
        // Categories
        internal const string AutoAddCategory = "Auto-add BlackBox Component";
        internal const string DefaultsCategory = "Defaults for New BlackBoxes";

        internal const string WorkflowsCategory = "Workflows";

        // Setting Names
        internal const string ToNewPrefabsName = "To New Prefabs";
        internal const string ToNewPrefabVariantsName = "To New Prefab Variants";
        internal const string LockedName = "Locked";
        internal const string SelectionTypeName = "Selection Type";
        internal const string HideTransformName = "Hide Transform";
        internal const string DisableApplyName = "Disable Apply";
        internal const string UnlockWhenNestedName = "Unlock When Nested";
        internal const string UnlockIfVariantRootName = "Unlock If Variant Root";
        internal const string EnableTempUnlockingName = "Enable Temp Unlocking";
        internal const string DisableLockingName = "Unlock All Prefabs";
        internal const string UnlockInPlayModeName = "Unlock In Play Mode";

        // Descriptions
        internal const string AutoAddToPrefabsDesc =
            "Automatically adds a BlackBox component to all newly created Prefabs.";

        internal const string AutoAddToVariantsDesc =
            "Automatically adds a BlackBox component to all newly created Prefab variants.";

        internal const string LockedDesc = "Whether a BlackBox start locked when just added.";

        internal const string SelectionTypeDesc =
            "Determines the selection type of a newly added BlackBox component. See the tooltip on the SelectionType property of a component for more info – or consult the documentation.";

        internal const string HideTransformDesc =
            "Whether the Transform component is hidden by default on new BlackBox instances.";

        internal const string DisableApplyDesc = "Whether Apply is disabled by default on new BlackBox instances.";

        internal const string UnlockWhenNestedDesc =
            "Whether new BlackBoxed Prefabs have the Unlock When Nested property on by default.";

        internal const string UnlockIfVariantRootDesc =
            "Whether new BlackBoxed Prefabs have the Unlock If Variant Root property on by default.";

        internal const string EnableTempUnlockingDesc = "Whether temporarily unlocking BlackBoxed Prefabs is allowed.";

        internal const string DisableLockingDesc =
            "General switch to disable locking on all BlackBox components in the project. " +
            "Set it to on to unlock all Prefabs, regardless of their unique setting. Set it to off to restore normal locking behaviour.";

        internal const string UnlockInPlayModeDesc =
            "If this is enabled, Prefabs will unlock themselves upon entering Play Mode. This can be useful when using the search feature of the hierarchy, as invisible objects wouldn't show up otherwise.";

        internal static PackageSetting<bool> WelcomeWindowSeen = new("general.welcomeWindowSeen", false);

        // Shared team settings
        internal static PackageSetting<bool> AutoAddToPrefabs = new("general.autoAddToPrefab", false);
        internal static PackageSetting<bool> AutoAddToVariants = new("general.autoAddToVariant", false);

        // Defaults
        internal static PackageSetting<bool> LockedByDefault = new("general.lockedByDefault", true);
        internal static PackageSetting<bool> ApplyDisabledByDefault = new("general.applyDisabledByDefault", true);
        internal static PackageSetting<bool> HideTransformByDefault = new("general.hideTransformByDefault", false);

        internal static PackageSetting<bool>
            UnlockWhenNestedByDefault = new("general.unlockWhenNestedByDefault", false);

        internal static PackageSetting<bool> UnlockIfVariantRootByDefault =
            new("general.unlockIfVariantRootByDefault", false);

        internal static PackageSetting<SelectionType> DefaultSelectionType =
            new("general.defaultSelectionType", SelectionType.UseBoundingBoxes);

        // Workflows
        internal static PackageSetting<bool> EnableTempUnlocking = new("general.enableTempUnlocking", true);

        // Preferences (per-user, shown in Preferences window)
        internal static UserPref<bool> DisableLocking = new("prefs.disableLocking", false);
        internal static UserPref<bool> UnlockInPlayMode = new("prefs.unlockPrefabsInPlayMode", true);
    }
}