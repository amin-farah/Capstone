using UnityEditor;
using UnityEngine;

namespace BlackBox.Editor
{
    public static class MenuItems
    {
        [InitializeOnLoadMethod]
        public static void CheckShowWelcomeScreen()
        {
            if (!BlackBoxSettings.WelcomeWindowSeen.value)
            {
                EditorApplication.delayCall += WelcomeScreen.Init;
                BlackBoxSettings.WelcomeWindowSeen.SetValue(true, true);
            }
        }
        
        [MenuItem(Constants.MenuItemBaseName + "Documentation", false, 100)]
        public static void OpenDocumentation() => Application.OpenURL(Constants.DocumentationUrl);
        
        [MenuItem(Constants.MenuItemBaseName + "Email support", false, 101)]
        public static void EmailSupport() => Application.OpenURL(Constants.SupportEmail);
        
        [MenuItem(Constants.MenuItemBaseName + "Join Discord", false, 102)]
        public static void JoinDiscord() => Application.OpenURL(Constants.ReviewUrl);
        
        [MenuItem(Constants.MenuItemBaseName + "Write a review", false, 103)]
        public static void LeaveAReview() => Application.OpenURL(Constants.ReviewUrl);
        
        private const string UnlockAllPrefabsMenuPath = Constants.MenuItemBaseName + BlackBoxSettings.DisableLockingName + " %#u";

        [MenuItem(UnlockAllPrefabsMenuPath, false, 1)]
        private static void ToggleGlobalLock()
        {
            BlackBoxSettings.DisableLocking.SetValue(!BlackBoxSettings.DisableLocking.value, true);
            BlackBoxSettingsProvider.OnSettingsSaved(); // Will save settings runtime-side, and update all Prefabs in the scene
            ActiveEditorTracker.sharedTracker.ForceRebuild();
        }
        
        [MenuItem(UnlockAllPrefabsMenuPath, true)]
        public static bool ToggleGlobalLockValidate()
        {
            Menu.SetChecked(UnlockAllPrefabsMenuPath, BlackBoxSettings.DisableLocking.value);
            return true;
        }
    }
}