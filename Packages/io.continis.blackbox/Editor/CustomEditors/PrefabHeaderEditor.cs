using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BlackBox.Editor
{
    public static class PrefabHeaderEditor
    {
        private static Type _prefabOverrideWindowType = Type.GetType("UnityEditor.PrefabOverridesWindow,UnityEditor");
        private static FieldInfo _windowContentField = typeof(PopupWindow).GetField("m_WindowContent", BindingFlags.Instance | BindingFlags.NonPublic);
        
        [InitializeOnLoadMethod]
        public static void FixHeader()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI -= AddToHeaderGUI;
            UnityEditor.Editor.finishedDefaultHeaderGUI += AddToHeaderGUI;
        }

        private static void AddToHeaderGUI(UnityEditor.Editor inspector)
        {
            if (BlackBoxSettings.DisableLocking) return;
            if (inspector.target == null) return;
            if (inspector.targets.Length > 1) return;
            GameObject inspectorTarget = inspector.target as GameObject;
            if (inspectorTarget == null) return;
            
            // TODO: better check on whether to show the button or not
            if (!PrefabUtility.IsPartOfNonAssetPrefabInstance(inspectorTarget)) return;
            
            if (inspectorTarget.TryGetComponent(out BlackBox blackBox))
            {
                if (!blackBox.IsApplyDisabled) return;
                
                // Close popup
                Object[] popupWindows = Resources.FindObjectsOfTypeAll(typeof (PopupWindow));
                foreach(Object popupWindowAsObject in popupWindows)
                {
                    PopupWindow popupWindow = popupWindowAsObject as PopupWindow;
                    if (_windowContentField.GetValue(popupWindow).GetType() == _prefabOverrideWindowType)
                        popupWindow.Close();
                }
                
                // Draw the overlaid button
                float currentView = EditorGUIUtility.currentViewWidth;

#if !UNITY_2022_1_OR_NEWER
                float buttonPos = currentView * .65f;
                Rect coverRect = new Rect(buttonPos, 54f, 2000f, EditorGUIUtility.singleLineHeight);
#else
                Rect coverRect = new(64f, 86f, currentView - 66f - 4f, EditorGUIUtility.singleLineHeight);
#endif
                
                GUILayout.BeginArea(coverRect);
                GUILayout.BeginHorizontal();

                Color prevContentColor = GUI.contentColor;
                GUI.contentColor = Color.clear;
                Color col = EditorGUIUtility.isProSkin
                    ? new Color(0.24f, 0.24f, 0.24f, .6f)
                    : new Color(0.8f, 0.8f, 0.8f, .6f);
		
                Texture2D tex = new(1, 1);
                tex.SetPixel(0, 0, col);
                tex.Apply();
                GUIStyle style = EditorStyles.inspectorFullWidthMargins;
                style.normal.background = tex;
                GUIContent buttonContent = new("Overridesss", "Overrides Dropdown disabled by the BlackBox component. " +
                                                              "Edit the Prefab and turn off Disable Apply to re-enable it.");
                GUILayout.Button(buttonContent, style, GUILayout.Height(22f));
                
                Color prevColor = GUI.color;
                Color prevColorBg = GUI.backgroundColor;
                GUI.color = Color.clear;
                GUI.backgroundColor = Color.clear;
                
                GUILayout.Button("",GUILayout.MaxWidth(25), GUILayout.MinWidth(4));
                GUILayout.Button(EditorGUIUtility.TrTextContent("Select"), EditorStyles.miniButton);
                GUILayout.Button(EditorGUIUtility.TrTextContent("Open"), EditorStyles.miniButton);
                GUILayout.EndHorizontal();
                GUILayout.EndArea();

                GUI.enabled = true;
                GUI.color = prevColor;
                GUI.backgroundColor = prevColorBg;
                GUI.contentColor = prevContentColor;
            }
        }
    }
}
