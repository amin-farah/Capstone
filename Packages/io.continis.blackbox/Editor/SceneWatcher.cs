using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using UEditor = UnityEditor.Editor;

namespace BlackBox.Editor
{
    public static class SceneWatcher
    {
        /// <summary>
        /// Skips the selection check function once, for when selection has just been changed by the check itself.
        /// </summary>
        private static bool _skipOneSelectionCheck;

        /// <summary>
        /// Cached list of BlackBoxes in the active prefab stage, used by <see cref="PollStageHideFlags"/>
        /// to avoid walking the stage hierarchy every editor tick.
        /// </summary>
        private static readonly List<BlackBox> _stageBlackBoxes = new();
        private static bool _stageBlackBoxesDirty = true;

        [InitializeOnLoadMethod]
        private static void Initialise()
        {

#if UNITY_2022_1_OR_NEWER
            EditorApplication.delayCall += () => DisableBlackBoxIcon();
#endif

            UpdateBlackBoxes();
            EditorSceneManager.sceneOpened += OnSceneOpened;
            Selection.selectionChanged += CheckSelection;
            EditorApplication.contextualPropertyMenu += OnPropertyContextMenu;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            ObjectChangeEvents.changesPublished += OnObjectChanged;
            EditorApplication.update += PollStageHideFlags;
            SceneView.duringSceneGui += BlockDragOnLockedSelection;

            // Invalidate the stage-BlackBox cache on any event that can add/remove BlackBoxes.
            PrefabStage.prefabStageOpened += _ => _stageBlackBoxesDirty = true;
            PrefabStage.prefabStageClosing += _ => _stageBlackBoxesDirty = true;
            EditorApplication.hierarchyChanged += () => _stageBlackBoxesDirty = true;

#if HATS
            TrackTeamSettings();
#endif
        }

        private static void OnPlayModeChanged(PlayModeStateChange obj)
        {
            if (!BlackBoxSettings.UnlockInPlayMode) return;
            
            switch (obj)
            {
                case PlayModeStateChange.EnteredPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                    BlackBoxMemory.instance.ClearTempUnlock();
                    UpdateBlackBoxes();
                    EditorApplication.RepaintHierarchyWindow();
                    break;
            }
        }

        private static void OnObjectChanged(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; i++)
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i,
                            out ChangeGameObjectOrComponentPropertiesEventArgs propArgs);
#if UNITY_6000_4_OR_NEWER
                        FindBlackBoxAncestor(propArgs.entityId)?.ForceUpdateAppearance();
#else
                        FindBlackBoxAncestor(propArgs.instanceId)?.ForceUpdateAppearance();
#endif
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructure:
                        stream.GetChangeGameObjectStructureEvent(i, out ChangeGameObjectStructureEventArgs structArgs);
#if UNITY_6000_4_OR_NEWER
                        FindBlackBoxAncestor(structArgs.entityId)?.ForceUpdateAppearance();
#else
                        FindBlackBoxAncestor(structArgs.instanceId)?.ForceUpdateAppearance();
#endif
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                        stream.GetChangeGameObjectStructureHierarchyEvent(i,
                            out ChangeGameObjectStructureHierarchyEventArgs hierArgs);
#if UNITY_6000_4_OR_NEWER
                        FindBlackBoxAncestor(hierArgs.entityId)?.ForceUpdateAppearance();
#else
                        FindBlackBoxAncestor(hierArgs.instanceId)?.ForceUpdateAppearance();
#endif
                        break;
                    case ObjectChangeKind.UpdatePrefabInstances:
                        stream.GetUpdatePrefabInstancesEvent(i, out UpdatePrefabInstancesEventArgs prefabArgs);
#if UNITY_6000_4_OR_NEWER
                        for (int j = 0; j < prefabArgs.entityIds.Length; j++)
                            FindBlackBoxAncestor(prefabArgs.entityIds[j])?.ForceUpdateAppearance();
#else
                        foreach (int t in prefabArgs.instanceIds)
                            FindBlackBoxAncestor(t)?.ForceUpdateAppearance();
#endif
                        break;
                }

            // In Prefab Stage, override changes (Revert/Apply) modify the asset rather than
            // the stage scene objects, so the instanceId resolution above won't find the
            // stage's BlackBox. Handle this by updating the stage root directly.
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null
                && stage.prefabContentsRoot != null
                && stage.prefabContentsRoot.TryGetComponent(out BlackBox rootBlackBox))
                rootBlackBox.ForceUpdateAppearance();
        }

        /// <summary>
        /// Runs every editor tick. Re-applies hideFlags on locked BlackBoxes in the active prefab stage
        /// if Unity's prefab pipeline has wiped them since the last tick.
        /// </summary>
        private static void PollStageHideFlags()
        {
            if (BlackBoxSettings.DisableLocking.value) return;

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || stage.scene.IsValid() == false)
            {
                // Outside Prefab Mode: nothing to do, but keep the cache clean for the next stage.
                if (_stageBlackBoxes.Count > 0)
                {
                    _stageBlackBoxes.Clear();
                    _stageBlackBoxesDirty = false;
                }
                return;
            }

            if (_stageBlackBoxesDirty)
            {
                _stageBlackBoxes.Clear();
                foreach (GameObject root in stage.scene.GetRootGameObjects())
                    _stageBlackBoxes.AddRange(root.GetComponentsInChildren<BlackBox>(true));
                _stageBlackBoxesDirty = false;
            }

            // Iterate backwards so we can prune destroyed entries without disturbing iteration.
            for (int i = _stageBlackBoxes.Count - 1; i >= 0; i--)
            {
                BlackBox bb = _stageBlackBoxes[i];
                if (bb == null)
                {
                    _stageBlackBoxes.RemoveAt(i);
                    continue;
                }

                // Cheap probe: if our own hideFlags are back to None, the prefab merge has
                // wiped this BlackBox (and almost certainly its siblings too) — re-apply.
                if (bb.hideFlags == HideFlags.None && bb.IsLocked && !bb.WillShowContents)
                    bb.ForceUpdateAppearance();
            }
        }

        /// <summary>
        /// Consumes Scene-view MouseDrag events when the selection is locked by a BlackBox:
        /// either the BlackBox itself (with HideTransform) or a revealed-child GameObject
        /// whose Transform is not itself revealed.
        /// </summary>
        private static void BlockDragOnLockedSelection(SceneView sceneView)
        {
            if (BlackBoxSettings.DisableLocking.value) return;

            Event e = Event.current;
            if (e == null) return;

            // Use typeForControl for Layout/Repaint so we early-out during those phases regardless of hot control ownership.
            EventType typeForControl = e.GetTypeForControl(GUIUtility.hotControl);
            if (typeForControl is EventType.Layout or EventType.Repaint) return;
            if (Tools.viewToolActive) return;
            if (e.type != EventType.MouseDrag) return;

            Transform[] selected = Selection.transforms;
            if (selected == null) return;

            for (int i = 0; i < selected.Length; i++)
            {
                if (ShouldBlockDragForSelection(selected[i]))
                {
                    e.Use();
                    return;
                }
            }
        }

        private static bool ShouldBlockDragForSelection(Transform t)
        {
            if (t == null) return false;

            BlackBox bb = null;
            Transform walk = t;
            while (walk != null)
            {
                if (walk.TryGetComponent(out bb)) break;
                walk = walk.parent;
            }
            if (bb == null) return false;

            // Selection IS a BlackBox — preserves the prior CheckIfAllowTransformations gate exactly.
            if (walk == t) return bb.HideTransform && !bb.IsTempUnlocked;

            // Selection is a descendant — only block when bb hides its contents and t is a
            // revealed child whose Transform is not itself a revealed item.
            return !bb.WillShowContents && bb.IsRevealedChildWithLockedTransform(t);
        }

#if UNITY_6000_3_OR_NEWER
        private static BlackBox FindBlackBoxAncestor(EntityId entityId)
        {
            Object obj = EditorUtility.EntityIdToObject(entityId);
#else
        private static BlackBox FindBlackBoxAncestor(int instanceId)
        {
            Object obj = EditorUtility.InstanceIDToObject(instanceId);
#endif
            if (obj == null) return null;

            GameObject go = obj switch
            {
                GameObject gameObject => gameObject,
                Component comp => comp.gameObject,
                _ => null
            };
            if (go == null) return null;

            Transform t = go.transform;
            while (t != null)
            {
                if (t.TryGetComponent(out BlackBox bb)) return bb;
                t = t.parent;
            }

            return null;
        }

#if HATS
        // Brings current Team settings (which are Editor only) to the BlackBox script,
        // and tracks subsequent changes.
        private static void TrackTeamSettings()
        {
            BlackBox.ActiveTeamIndex = Hats.Editor.Teams.GetActiveTeamIndex();
            Hats.Editor.Teams.TeamChanged += i => BlackBox.ActiveTeamIndex = i;
        }
#endif

#if UNITY_2022_1_OR_NEWER
        /// <summary> Disable the BlackBox icon in the scene, only once, upon launching the Unity editor. </summary>
        static void DisableBlackBoxIcon()
        {
            if (SessionState.GetBool("BlackBoxIconDisabled", false)) return;
            
            foreach (GizmoInfo info in GizmoUtility.GetGizmoInfo())
            {
                if (info.name != nameof(BlackBox)) continue;
                    
                info.iconEnabled = false;
                GizmoUtility.ApplyGizmoInfo(info);
                SessionState.SetBool("BlackBoxIconDisabled", true);
                break;
            }
        }
#endif

        /// <summary> Ensures the user can't select a sub-object of a locked BlackBox prefab.</summary>
        private static void CheckSelection()
        {
            BlackBoxMemory.instance.AddingOverrides = false;
            
            if (_skipOneSelectionCheck)
            {
                // Skip the checks again, because the selection has just been changed by this very method
                _skipOneSelectionCheck = false;
                return;
            }
            
            Transform selectionsTransform = null;
            
            // While a Prefab is temp-unlocked, selections inside it are allowed — but selection must
            // still snap out of any deeper, still-locked nested Prefab.
            if (Selection.activeGameObject != null)
            {
                selectionsTransform = Selection.activeGameObject.transform;
                BlackBox unlockedInstance = BlackBoxMemory.instance.UnlockedBlackBox;
                if (unlockedInstance != null &&
                    selectionsTransform.IsChildOf(unlockedInstance.gameObject.transform))
                {
                    GameObject snapTarget = ResolveSelectionInsideTempUnlocked(unlockedInstance, selectionsTransform);
                    if (snapTarget != null && snapTarget != Selection.activeGameObject)
                    {
                        EditorApplication.delayCall += () =>
                        {
                            Selection.activeGameObject = snapTarget;
                            _skipOneSelectionCheck = true;
                        };
                    }
                    return; // keep temp unlock active
                }
            }
            
            // At this point we're sure a temp-unlocked Prefab is not selected,
            // so we can clear the temp-unlock state
            BlackBoxMemory.instance.ClearTempUnlock();
            
            if (Selection.activeGameObject == null) return;
            if (BlackBoxSettings.DisableLocking.value) return;
            if (!PrefabUtility.IsPartOfAnyPrefab(Selection.activeGameObject)) return; // TODO [2.0]: Handle GameObjects
            if (PrefabUtility.IsPartOfPrefabAsset(Selection.activeGameObject)) return;
            
            //Debug.Log($"Checking selection for: {Selection.activeGameObject}");
            
            GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(Selection.activeGameObject);
            if (root == Selection.activeGameObject) return;
            
            GameObject objectToSelect = Selection.activeGameObject;
            if (root.TryGetComponent(out BlackBox rootBlackBox)
                && !rootBlackBox.WillShowContents)
            {
                // Root Prefab is locked, hence we want to reset the selection to the root
                
                // Unless...
                if (rootBlackBox.IsGORevealedAsChildBecauseOfAnotherComponent(selectionsTransform)) return;
                if (rootBlackBox.IsAncestorOfARevealedChild(selectionsTransform)) return;
                if (rootBlackBox.HasRevealedChildren())
                {
                    GameObject parentPrefab = PrefabUtility.GetNearestPrefabInstanceRoot(Selection.activeGameObject);
                    if (parentPrefab.TryGetComponent(out BlackBox nestedBlackBox))
                    {
                        if (nestedBlackBox.IsGORevealedAsChildBecauseOfAnotherComponent(selectionsTransform)) return;
                        if (nestedBlackBox.IsAncestorOfARevealedChild(selectionsTransform)) return;
                    }
                }
                
                objectToSelect = root;
                //Debug.Log($"Checking root's WillShowContents: {rootBlackBox.WillShowContents}");
            }
            else
            {
                // Root Prefab is showing its children
                // Need to go up the Prefab chain and inspect each BlackBox on the way, to reset the selection there
                GameObject parentPrefab = Selection.activeGameObject;
                while (parentPrefab != root)
                {
                    parentPrefab = PrefabUtility.GetNearestPrefabInstanceRoot(parentPrefab.transform.parent);
                    if (parentPrefab.TryGetComponent(out BlackBox blackBox))
                    {
                        if (blackBox.IsGORevealedAsChildBecauseOfAnotherComponent(selectionsTransform)) return;
                        if (blackBox.IsAncestorOfARevealedChild(selectionsTransform)) return;
                        
                        if (!blackBox.WillShowContents) objectToSelect = parentPrefab;
                        //Debug.Log($"Checking now: {parentPrefab}'s WillShowContents: {blackBox.WillShowContents}");
                    }
                }
            }

            //Debug.Log($"Finished, selection will be: {objectToSelect}");

            if (objectToSelect == Selection.activeGameObject || objectToSelect == null) return;
            
            EditorApplication.delayCall += () =>
            {
                Selection.activeGameObject = objectToSelect;
                _skipOneSelectionCheck = true;
            };
        }

        /// <summary>
        /// While <paramref name="tempUnlocked"/> is temp-unlocked, returns the nested Prefab the selection
        /// should snap to — the innermost still-locked BlackBoxed Prefab strictly between the selection and
        /// the temp-unlocked Prefab — or null when the selection is freely allowed.
        /// </summary>
        private static GameObject ResolveSelectionInsideTempUnlocked(BlackBox tempUnlocked, Transform selection)
        {
            Transform boundary = tempUnlocked.transform;
            if (selection == boundary) return null;

            GameObject snapTarget = null;

            GameObject prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(selection);
            while (prefabRoot != null &&
                   prefabRoot.transform != boundary &&
                   prefabRoot.transform.IsChildOf(boundary))
            {
                if (prefabRoot.TryGetComponent(out BlackBox box))
                {
                    // Revealed children stay selectable.
                    if (box.IsGORevealedAsChildBecauseOfAnotherComponent(selection)) return null;
                    if (box.IsAncestorOfARevealedChild(selection)) return null;

                    if (!box.WillShowContents) snapTarget = prefabRoot;
                }

                Transform parent = prefabRoot.transform.parent;
                if (parent == null) break;
                prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(parent);
            }

            return snapTarget;
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            BlackBoxMemory.instance.ClearTempUnlock();
            UpdateBlackBoxes(false, true);
        }

        /// <summary>
        /// Force updates all BlackBoxes in the scene.
        /// </summary>
        /// <param name="doActive">Updates all active GameObjects with a BlackBox.</param>
        /// <param name="doInactive">Updates all inactive GameObjects with a BlackBox.</param>
        private static void UpdateBlackBoxes(bool doActive = true, bool doInactive = true)
        {
            if (BlackBoxSettings.DisableLocking.value) return;
            
            BlackBox[] blackBoxes =
#if UNITY_6000_4_OR_NEWER
                Object.FindObjectsByType<BlackBox>(FindObjectsInactive.Include);
#else
                Object.FindObjectsByType<BlackBox>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#endif
            
            // TODO: For optimisation, can we just look for the ones on the root? Theoretically they should take care of updating their children? (right??)
            
            foreach (BlackBox blackBox in blackBoxes)
            {
                if (blackBox.gameObject.activeSelf)
                {
                    if(doActive)
                        blackBox.ForceUpdateAppearance();
                }
                else if (doInactive) blackBox.ForceUpdateAppearance();
            }
        }

        public static void UpdateAllPrefabsInScene()
        {
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            BlackBox[] blackBoxes;
            if (prefabStage != null)
            {
                blackBoxes = prefabStage.scene.GetRootGameObjects()
                    .SelectMany(go => go.GetComponentsInChildren<BlackBox>(true))
                    .Where(box => box.gameObject != prefabStage.prefabContentsRoot)
                    .ToArray();
            }
#if UNITY_6000_4_OR_NEWER
            else blackBoxes = Object.FindObjectsByType<BlackBox>(FindObjectsInactive.Include);
#else
            else blackBoxes = Object.FindObjectsByType<BlackBox>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#endif

            foreach (BlackBox blackBox in blackBoxes) blackBox.ForceUpdateAppearance();
        }

        /// <summary>
        /// Adds the "Reveal as part of {list}" items for <paramref name="property"/> to a context menu.
        /// Shared by Unity's property context menu and, when present, Odin's (see OdinRevealContextMenu).
        /// </summary>
        internal static void AddRevealMenuItems(GenericMenu menu, SerializedProperty property)
        {
            // TODO: Allow context menu also in scene, to account revealing on unlocked Prefabs, temp-unlocked, and in the future on GameObjects
            PrefabStage activeStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (activeStage == null) return; // Reveal items only in Prefab mode

            RevealType revealType;

            Component comp = property.serializedObject.targetObject as Component;
            if (comp != null && comp.GetType() == typeof(BlackBox))
                return; // No action for properties of the BlackBox component itself

            GameObject go = property.serializedObject.targetObject as GameObject;
            if (go == null)
            {
                if (comp != null)
                {
                    go = comp.gameObject;
                    revealType = RevealType.ComponentProperty;
                }
                else
                    return; // It's not a property attached to a Component (maybe it's attached to a ScriptableObject)
            }
            else
            {
                revealType = RevealType.GameObjectProperty;
            }

            GameObject root = activeStage.prefabContentsRoot;
            if (root.TryGetComponent(out BlackBox blackBox))
            {
                SerializedObject blackBoxSerObj = new(blackBox);
                SerializedProperty revealedListsArrayProp = blackBoxSerObj.FindProperty("_revealedLists");
                for (int i = 0; i < revealedListsArrayProp.arraySize; i++)
                {
                    SerializedProperty revealListProp = revealedListsArrayProp.GetArrayElementAtIndex(i);
                    string listName = revealListProp.FindPropertyRelative(nameof(RevealedItemsList.listName))
                        .stringValue;
                    PropertyInfoStruct infoStruct = new(blackBox, go, comp, property.serializedObject.targetObject, property.propertyPath, revealType, i);
                    string menuString = $"BlackBox/Reveal as part of {listName}";
                    menu.AddItem(new GUIContent(menuString), false, AddPropertyToRevealed, infoStruct);
                }
            }
        }

        private static void OnPropertyContextMenu(GenericMenu menu, SerializedProperty property)
        {
            AddRevealMenuItems(menu, property);

            // Disable Apply
            if (property.prefabOverride)
            {
                GameObject gameObject = property.serializedObject.targetObject as GameObject;
                if (gameObject == null) gameObject = ((Component)property.serializedObject.targetObject).gameObject;
                if (gameObject == null) return;

                GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(gameObject);
                if (!root.TryGetComponent(out BlackBox blackBox)) return;

                if (!blackBox.IsApplyDisabled) return;

                Type menuType = menu.GetType();
                FieldInfo field = menuType.GetField("m_MenuItems", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    // Get the current list instance (cast if necessary)
                    IList list = (IList)field.GetValue(menu);

                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        object menuItem = list[i];
                        Type menuItemType = menuType.Assembly.GetType("UnityEditor.GenericMenu+MenuItem");
                        FieldInfo contentField =
                            menuItemType!.GetField("content", BindingFlags.Public | BindingFlags.Instance);
                        GUIContent content = (GUIContent)contentField!.GetValue(menuItem);
                        if (content.text.StartsWith("Apply ")) list.RemoveAt(i);
                    }
                }
            }

        }

        private static void AddPropertyToRevealed(object obj)
        {
            PropertyInfoStruct infoStruct = (PropertyInfoStruct)obj;
            SerializedObject serializedObject = new(infoStruct.blackBox);
            SerializedProperty revealList = serializedObject.FindProperty("_revealedLists")
                .GetArrayElementAtIndex(infoStruct.listIndex)
                .FindPropertyRelative(nameof(RevealedItemsList.revealedItems));

            SerializedObject o = new(infoStruct.targetObject);
            SerializedProperty serPropToAdd = o.FindProperty(infoStruct.propertyPath);
            BlackBoxEditor.AddToRevealed(serializedObject, revealList, infoStruct.go, infoStruct.comp,
                infoStruct.propertyPath, serPropToAdd.displayName, infoStruct.revealType);

            // Try to refresh the visible Inspector, if the inspected object has the BlackBox component
            if (Selection.activeGameObject == infoStruct.blackBox.gameObject)
            {
                EditorApplication.delayCall += RefreshVisibleInspector;
            }
        }

        private static void RefreshVisibleInspector() => BlackBoxEditor.CurrentEditor.RefreshRevealedProperties();

        private struct PropertyInfoStruct
        {
            public BlackBox blackBox;
            public GameObject go;
            public Component comp;
            public Object targetObject;
            public string propertyPath;
            public RevealType revealType;
            public int listIndex;

            public PropertyInfoStruct(BlackBox blackBox, GameObject go, Component comp, Object targetObject, string propertyPath, RevealType revealType, int listIndex)
            {
                this.blackBox = blackBox;
                this.targetObject = targetObject;
                this.propertyPath = propertyPath;
                this.go = go;
                this.comp = comp;
                this.revealType = revealType;
                this.listIndex = listIndex;
            }
        }
    }
}