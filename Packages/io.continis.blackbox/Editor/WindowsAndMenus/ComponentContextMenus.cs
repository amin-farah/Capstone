using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlackBox.Editor
{
    public static class ComponentContextMenus
    {
        #region RevealEntireComponent
        
        private const string RevealComponent = "CONTEXT/Component/BlackBox/Entire Component/Reveal in the ";
        private const string RevealCompInDefaultList = RevealComponent + "First List";
        private const string RevealCompInSecondList = RevealComponent + "Second List";
        private const string RevealCompInThirdList = RevealComponent + "Third List";
        private const string RevealCompInFourthList = RevealComponent + "Fourth List";

        [MenuItem(RevealCompInDefaultList, false, 100)]
        private static void AddComponentToList0(MenuCommand menuCommand) => AddComponentToRevealed(menuCommand, 0);

        [MenuItem(RevealCompInSecondList, false, 101)]
        private static void AddComponentToList1(MenuCommand menuCommand) => AddComponentToRevealed(menuCommand, 1);

        [MenuItem(RevealCompInThirdList, false, 102)]
        private static void AddComponentToList2(MenuCommand menuCommand) => AddComponentToRevealed(menuCommand, 2);

        [MenuItem(RevealCompInFourthList, false, 103)]
        private static void AddComponentToList3(MenuCommand menuCommand) => AddComponentToRevealed(menuCommand, 3);

        private static void AddComponentToRevealed(MenuCommand menuCommand, int listIndex)
        {
            Component component = (Component)menuCommand.context;

            PrefabStage activeStage = PrefabStageUtility.GetCurrentPrefabStage();
            GameObject root = activeStage.prefabContentsRoot;
            if (root.TryGetComponent(out BlackBox blackBox))
            {
                SerializedObject serializedObject = new(blackBox);
                SerializedProperty revealList = serializedObject.FindProperty("_revealedLists")
                    .GetArrayElementAtIndex(listIndex)
                    .FindPropertyRelative(nameof(RevealedItemsList.revealedItems));

                BlackBoxEditor.AddToRevealed(serializedObject, revealList, component.gameObject, component,
                    component.GetType().Name, component.GetType().Name, RevealType.EntireComponent);

                // Try to refresh the visible Inspector, if the inspected object has the BlackBox component
                if (Selection.activeGameObject == blackBox.gameObject)
                {
                    EditorApplication.delayCall += RefreshVisibleInspector;
                }
            }
        }

        [MenuItem(RevealCompInDefaultList, true)]
        private static bool ValidateAddComponentToList0(MenuCommand menuCommand) =>
            ValidateComponentAction(menuCommand, 0);

        [MenuItem(RevealCompInSecondList, true)]
        private static bool ValidateAddComponentToList1(MenuCommand menuCommand) =>
            ValidateComponentAction(menuCommand, 1);

        [MenuItem(RevealCompInThirdList, true)]
        private static bool ValidateAddComponentToList2(MenuCommand menuCommand) =>
            ValidateComponentAction(menuCommand, 2);

        [MenuItem(RevealCompInFourthList, true)]
        private static bool ValidateAddComponentToList3(MenuCommand menuCommand) =>
            ValidateComponentAction(menuCommand, 3);

        #endregion

        #region RevealAllProps
        
        private const string RevealAllProps = "CONTEXT/Component/BlackBox/All Properties/Reveal All in the ";
        private const string RevealPropsInDefaultList = RevealAllProps + "First List";
        private const string RevealPropsInSecondList = RevealAllProps + "Second List";
        private const string RevealPropsInThirdList = RevealAllProps + "Third List";
        private const string RevealPropsInFourthList = RevealAllProps + "Fourth List";

        [MenuItem(RevealPropsInDefaultList, false, 100)]
        private static void AddAllPropsToList0(MenuCommand menuCommand) => AddAllPropsToRevealed(menuCommand, 0);

        [MenuItem(RevealPropsInSecondList, false, 101)]
        private static void AddAllPropsToList1(MenuCommand menuCommand) => AddAllPropsToRevealed(menuCommand, 1);

        [MenuItem(RevealPropsInThirdList, false, 102)]
        private static void AddAllPropsToList2(MenuCommand menuCommand) => AddAllPropsToRevealed(menuCommand, 2);

        [MenuItem(RevealPropsInFourthList, false, 103)]
        private static void AddAllPropsToList3(MenuCommand menuCommand) => AddAllPropsToRevealed(menuCommand, 3);

        private static void AddAllPropsToRevealed(MenuCommand menuCommand, int listIndex)
        {
            Component component = (Component)menuCommand.context;

            PrefabStage activeStage = PrefabStageUtility.GetCurrentPrefabStage();
            GameObject root = activeStage.prefabContentsRoot;
            if (root.TryGetComponent(out BlackBox blackBox))
            {
                SerializedObject blackBoxSerObj = new(blackBox);
                SerializedProperty revealList = blackBoxSerObj.FindProperty("_revealedLists")
                    .GetArrayElementAtIndex(listIndex)
                    .FindPropertyRelative(nameof(RevealedItemsList.revealedItems));

                SerializedObject componentSerObj = new(component);
                SerializedProperty property = componentSerObj.GetIterator();
                if (property.NextVisible(true))
                {
                    do
                    {
                        if (property.name is "m_Script"
                            or "m_ConstrainProportionsScale") continue;

                        if (property.depth == 0)
                        {
                            BlackBoxEditor.AddToRevealed(blackBoxSerObj, revealList, component.gameObject, component,
                                property.propertyPath, property.displayName, RevealType.ComponentProperty);
                        }

                    } while (property.NextVisible(false));
                }

                // Try to refresh the visible Inspector, if the inspected object has the BlackBox component
                if (Selection.activeGameObject == blackBox.gameObject)
                {
                    EditorApplication.delayCall += RefreshVisibleInspector;
                }
            }
        }
        
        [MenuItem(RevealPropsInDefaultList, true)]
        private static bool ValidateAddAllPropsToList0(MenuCommand menuCommand) =>
            ValidateComponentAction(menuCommand, 0);
        
        [MenuItem(RevealPropsInSecondList, true)]
        private static bool ValidateAddAllPropsToList1(MenuCommand menuCommand) =>
            ValidateComponentAction(menuCommand, 1);
        
        [MenuItem(RevealPropsInThirdList, true)]
        private static bool ValidateAddAllPropsToList2(MenuCommand menuCommand) =>
            ValidateComponentAction(menuCommand, 2);
        
        [MenuItem(RevealPropsInFourthList, true)]
        private static bool ValidateAddAllPropsToList3(MenuCommand menuCommand) =>
            ValidateComponentAction(menuCommand, 3);

        #endregion

        #region RevealComponentReference

        private const string RevealComponentRef = "CONTEXT/Component/BlackBox/Reference to Component/Reveal in the ";
        private const string RevealComponentRefInDefaultList = RevealComponentRef + "First List";
        private const string RevealComponentRefInSecondList = RevealComponentRef + "Second List";
        private const string RevealComponentRefInThirdList = RevealComponentRef + "Third List";
        private const string RevealComponentRefInFourthList = RevealComponentRef + "Fourth List";
        
        [MenuItem(RevealComponentRefInDefaultList, false, 100)]
        private static void RevealComponentRefInList0(MenuCommand menuCommand) => RevealComponentRefInList(menuCommand, 0);

        [MenuItem(RevealComponentRefInSecondList, false, 101)]
        private static void RevealComponentRefInList1(MenuCommand menuCommand) => RevealComponentRefInList(menuCommand, 1);

        [MenuItem(RevealComponentRefInThirdList, false, 102)]
        private static void RevealComponentRefInList2(MenuCommand menuCommand) => RevealComponentRefInList(menuCommand, 2);

        [MenuItem(RevealComponentRefInFourthList, false, 103)]
        private static void RevealComponentRefInList3(MenuCommand menuCommand) => RevealComponentRefInList(menuCommand, 3);

        private static void RevealComponentRefInList(MenuCommand menuCommand, int listIndex)
        {
            Component component = (Component)menuCommand.context;

            PrefabStage activeStage = PrefabStageUtility.GetCurrentPrefabStage();
            GameObject root = activeStage.prefabContentsRoot;
            if (root.TryGetComponent(out BlackBox blackBox))
            {
                SerializedObject blackBoxSerObj = new(blackBox);
                SerializedProperty revealList = blackBoxSerObj.FindProperty("_revealedLists")
                    .GetArrayElementAtIndex(listIndex)
                    .FindPropertyRelative(nameof(RevealedItemsList.revealedItems));

                BlackBoxEditor.AddToRevealed(blackBoxSerObj, revealList, component.gameObject, component,
                    "", component.GetType().Name, RevealType.ObjectReference);

                // Try to refresh the visible Inspector, if the inspected object has the BlackBox component
                if (Selection.activeGameObject == blackBox.gameObject)
                {
                    EditorApplication.delayCall += RefreshVisibleInspector;
                }
            }
        }
        
        [MenuItem(RevealComponentRefInDefaultList, true)]
        private static bool ValidateAddComponentRef0(MenuCommand menuCommand) =>
            ValidateComponentAction(menuCommand, 0);
        
        [MenuItem(RevealComponentRefInSecondList, true)]
        private static bool ValidateAddComponentRef1(MenuCommand menuCommand) =>
            ValidateComponentAction(menuCommand, 1);
        
        [MenuItem(RevealComponentRefInThirdList, true)]
        private static bool ValidateAddComponentRef2(MenuCommand menuCommand) =>
            ValidateComponentAction(menuCommand, 2);
        
        [MenuItem(RevealComponentRefInFourthList, true)]
        private static bool ValidateAddComponentRef3(MenuCommand menuCommand) =>
            ValidateComponentAction(menuCommand, 3);

        #endregion
        
        #region RevealChildGameObject
        
        private const string CommonRevealGOItemPath = "/BlackBox/Child GameObject/Reveal";
        private const string GOByComponent = "CONTEXT/Component" + CommonRevealGOItemPath;
        private const string UsingThisComponent = " using this component";
        private const string GOByComponentList0 = GOByComponent + UsingThisComponent + " in the First List";
        private const string GOByComponentList1 = GOByComponent + UsingThisComponent + " in the Second List";
        private const string GOByComponentList2 = GOByComponent + UsingThisComponent + " in the Third List";
        private const string GOByComponentList3 = GOByComponent + UsingThisComponent + " in the Fourth List";
        private const string GOOnGameObject = "GameObject" + CommonRevealGOItemPath;
        private const string GOOnGameObject0 = GOOnGameObject + " in the First List";
        private const string GOOnGameObject1 = GOOnGameObject + " in the Second List";
        private const string GOOnGameObject2 = GOOnGameObject + " in the Third List";
        private const string GOOnGameObject3 = GOOnGameObject + " in the Fourth List";
        private const int RevealChildOnGOPriority = 0;

        [MenuItem(GOByComponentList0, false, 100)]
        private static void Comp_RevealGOInList0(MenuCommand menuCommand) => RevealFromComponent(menuCommand, 0);
        
        [MenuItem(GOByComponentList1, false, 101)]
        private static void Comp_RevealGOInList1(MenuCommand menuCommand) => RevealFromComponent(menuCommand, 1);
        
        [MenuItem(GOByComponentList2, false, 102)]
        private static void Comp_RevealGOInList2(MenuCommand menuCommand) => RevealFromComponent(menuCommand, 2);
        
        [MenuItem(GOByComponentList3, false, 103)]
        private static void Comp_RevealGOInList3(MenuCommand menuCommand) => RevealFromComponent(menuCommand, 3);
        
        [MenuItem(GOOnGameObject0, false, RevealChildOnGOPriority)]
        private static void GO_RevealGOinList0(MenuCommand menuCommand) => RevealFromGO(menuCommand, 0);
        
        [MenuItem(GOOnGameObject1, false, RevealChildOnGOPriority+1)]
        private static void GO_RevealGOinList1(MenuCommand menuCommand) => RevealFromGO(menuCommand, 1);
        
        [MenuItem(GOOnGameObject2, false, RevealChildOnGOPriority+2)]
        private static void GO_RevealGOinList2(MenuCommand menuCommand) => RevealFromGO(menuCommand, 2);
        
        [MenuItem(GOOnGameObject3, false, RevealChildOnGOPriority+3)]
        private static void GO_RevealGOinList3(MenuCommand menuCommand) => RevealFromGO(menuCommand, 3);

        private static void RevealFromComponent(MenuCommand menuCommand, int listIndex) => RevealGameObject((Component)menuCommand.context, listIndex);
        private static void RevealFromGO(MenuCommand menuCommand, int listIndex)
        {
            Object ctx = menuCommand.context;
            // Can be null when the target is a the ancestor of a revealed child GO
            if (ctx != null)
                RevealGameObject(((GameObject)ctx).transform, listIndex);
            else
            {
                Debug.LogWarning(
                    $"{Constants.LogPrefix} Object cannot be revealed because it's only visible as one of the ancestors " +
                    "of another revealed child. Reveal this object within the nested Prefab first," +
                    " then it will be available to be revealed in this one too.");
            }
        }

        private static void RevealGameObject(Component component, int listIndex)
        {
            PrefabStage activeStage = PrefabStageUtility.GetCurrentPrefabStage();
            GameObject root = activeStage.prefabContentsRoot;
            if (root.TryGetComponent(out BlackBox blackBox))
            {
                SerializedObject blackBoxSerObj = new(blackBox);
                SerializedProperty revealList = blackBoxSerObj.FindProperty("_revealedLists")
                    .GetArrayElementAtIndex(listIndex)
                    .FindPropertyRelative(nameof(RevealedItemsList.revealedItems));

                BlackBoxEditor.AddToRevealed(blackBoxSerObj, revealList, component.gameObject, component,
                    "", component.gameObject.name, RevealType.GameObject);

                // Try to refresh the visible Inspector, if the inspected object has the BlackBox component
                if (Selection.activeGameObject == blackBox.gameObject)
                {
                    EditorApplication.delayCall += RefreshVisibleInspector;
                }
            }
        }
        
        [MenuItem(GOByComponentList0, true)]
        private static bool ValidateComp_RevealGOInList0(MenuCommand menuCommand) =>
            ValidateComponentAction(menuCommand, 0);
        
        [MenuItem(GOByComponentList1, true)]
        private static bool ValidateComp_RevealGOInList1(MenuCommand menuCommand) =>
            ValidateComponentAction(menuCommand, 1);
        
        [MenuItem(GOByComponentList2, true)]
        private static bool ValidateComp_RevealGOInList2(MenuCommand menuCommand) =>
            ValidateComponentAction(menuCommand, 2);
        
        [MenuItem(GOByComponentList3, true)]
        private static bool ValidateComp_RevealGOInList3(MenuCommand menuCommand) =>
            ValidateComponentAction(menuCommand, 3);
        
        [MenuItem(GOOnGameObject0, true)]
        private static bool ValidateGO_RevealGOinList0(MenuCommand menuCommand) =>
            ValidateGOAction(menuCommand, 0);
        
        [MenuItem(GOOnGameObject1, true)]
        private static bool ValidateGO_RevealGOinList1(MenuCommand menuCommand) =>
            ValidateGOAction(menuCommand, 1);
        
        [MenuItem(GOOnGameObject2, true)]
        private static bool ValidateGO_RevealGOinList2(MenuCommand menuCommand) =>
            ValidateGOAction(menuCommand, 2);
        
        [MenuItem(GOOnGameObject3, true)]
        private static bool ValidateGO_RevealGOinList3(MenuCommand menuCommand) =>
            ValidateGOAction(menuCommand, 3);
        
        #endregion

        #region Common

        private static bool ValidateComponentAction(MenuCommand menuCommand, int listIndex)
        {
            PrefabStage activeStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (activeStage == null) return false;
            
            // TODO: Allow context menu also in scene, to account revealing on unlocked Prefabs, and temp-unlocked
            
            Component comp = (Component)menuCommand.context;
            GameObject root = activeStage.prefabContentsRoot;
            if (comp == null || comp.GetType() == typeof(BlackBox)) return false;

            if (!root.TryGetComponent(out BlackBox bb)) return false;

            SerializedObject serializedObject = new(bb);
            return serializedObject.FindProperty("_revealedLists").arraySize > listIndex;

        }

        private static bool ValidateGOAction(MenuCommand menuCommand, int listIndex)
        {
            PrefabStage activeStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (activeStage == null) return false;
            
            // TODO: Allow context menu also in scene, to account revealing on unlocked Prefabs, and temp-unlocked
            
            GameObject root = activeStage.prefabContentsRoot;
            if (!root.TryGetComponent(out BlackBox bb)) return false;
            
            SerializedObject serializedObject = new(bb);
            return serializedObject.FindProperty("_revealedLists").arraySize > listIndex;
        }

        private static void RefreshVisibleInspector() => BlackBoxEditor.CurrentEditor.RefreshRevealedProperties();

        #endregion
    }
}