#if ODIN_INSPECTOR
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace BlackBox.Editor
{
    /// <summary>
    /// Odin builds its own right-click menu for the properties it draws, bypassing the
    /// EditorApplication.contextualPropertyMenu hook that SceneWatcher uses. This drawer sits in every
    /// property's drawer chain purely to contribute the BlackBox "Reveal" items to Odin's menu; it does
    /// not alter rendering, forwarding drawing to the next drawer.
    /// </summary>
    [DrawerPriority(DrawerPriorityLevel.SuperPriority)]
    public sealed class BlackBoxRevealContextMenuDrawer<T> : OdinValueDrawer<T>, IDefinesGenericMenuItems
    {
        protected override void DrawPropertyLayout(GUIContent label) => CallNextDrawer(label);

        public void PopulateGenericMenu(InspectorProperty property, GenericMenu genericMenu)
        {
            // Map the Odin property back to its backing Unity SerializedProperty, then reuse the same
            // reveal logic as the Unity context menu.
            SerializedObject serializedObject = property.Tree.UnitySerializedObject;
            if (serializedObject == null) return;

            SerializedProperty unityProperty = property.Tree.GetUnityPropertyForPath(property.Path);
            if (unityProperty == null) return;

            // GetUnityPropertyForPath emits a stand-in for paths not backed by Unity serialization
            // (e.g. [ShowInInspector] getters); those can't be revealed, so re-resolve to reject them.
            if (serializedObject.FindProperty(unityProperty.propertyPath) == null) return;

            SceneWatcher.AddRevealMenuItems(genericMenu, unityProperty);
        }
    }
}
#endif
