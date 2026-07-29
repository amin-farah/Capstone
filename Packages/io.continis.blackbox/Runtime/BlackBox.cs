using System;
using UnityEngine;
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Serialization;
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace BlackBox
{
#if UNITY_EDITOR
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [HelpURL("https://tools.continis.io/v/black-box")]
    public class BlackBox : MonoBehaviour, ISerializationCallbackReceiver
    {
        public static bool LockingDisabled;
        public static bool LockedDefault;
        public static SelectionType DefaultSelectionType;
        public static bool ApplyDisabledByDefault;
        public static bool HideTransformByDefault;
        public static bool UnlockWhenNestedByDefault;
        public static bool UnlockIfVariantRootByDefault;
        public static bool UnlockInPlayMode;
        
        // This is set externally, and it's the last list clicked on.
        // To read this, use CurrentListIndex
        public static int LastClickedListIndex { set; private get; }

        // Even if a list was deleted in the meanwhile since LastClickedListIndex was saved,
        // we don't try to get a list that doesn't exist
        private int CurrentListIndex => Mathf.Clamp(LastClickedListIndex, 0, _revealedLists.Length - 1);
        private bool HasRevealedLists => _revealedLists is { Length: > 0 };

#if HATS
        public static int ActiveTeamIndex;
        
        [SerializeField] private int _teamsAllowedToEdit = -1;
        private bool _exitingStage;
#endif
        [SerializeField] private float _serializedVersion;
        
        [SerializeField] private bool _locked = LockedDefault;
        [SerializeField] private bool _disableApply = ApplyDisabledByDefault;
        [SerializeField] private bool _hideTransform = HideTransformByDefault;
        [SerializeField] private bool _unlockIfNested = UnlockWhenNestedByDefault;
        [SerializeField] private bool _unlockIfVariantRoot = UnlockIfVariantRootByDefault;
        [SerializeField] private bool _allowBlackBoxOverrides;
        [SerializeField] private SelectionType _selectionType = DefaultSelectionType;
        [SerializeField] private RevealedItemsList[] _revealedLists;

        private GameObject _go;
        private Transform _transform;
        private Mesh _selectionMesh;
        private bool _canShowContents;
        private bool _isTempUnlocked;
        private bool _isNested;
        private bool _isVariantRoot;
        private bool _inScene;
        private bool _isPrefabRoot;
        private bool _isNotAPrefab;
        private bool _isRegularPrefab;
        private bool _isVariant;
        private bool _isAddedAsOverride;
        private bool _isOutermostPrefabInstanceRoot;
        private bool _isAsset;
        private bool _isPrefabModel;
        private List<Transform> _protectedAncestors; // These Transforms are ancestors to a revealed child, as such, their hideFlags shouldn't be modified because their descendant already set them up
        private Dictionary<GameObject, HashSet<Component>> _revealedGOComponents;

        // Base properties accessors
        public bool IsLocked => _locked;
        public bool UnlockIfNested => _unlockIfNested;
        public bool UnlockIfVariantRoot => _unlockIfVariantRoot;
        public bool HideTransform => _hideTransform;
        public bool IsApplyDisabled => _disableApply;
        public bool AllowBlackBoxOverrides => _allowBlackBoxOverrides;
        public bool IsAsset => AssetDatabase.Contains(gameObject); // Cannot cache it, as it won't be correctly initialised for assets
        public bool IsPrefabModel => _isPrefabModel;
        public bool IsPrefabRoot => _isPrefabRoot;
        public bool IsNotAPrefab => _isNotAPrefab;
        public bool IsRegularPrefab => _isRegularPrefab;
        public bool IsVariant => _isVariant;
        public bool IsAddedAsOverride => _isAddedAsOverride;
        public bool InScene => _inScene;
        
        /// <summary> A nested Prefab can be a child in Prefab Mode, but also a revealed child in the scene.</summary>
        public bool IsNested => _isNested;
        public bool IsVariantRoot => _isVariantRoot;

        /// <summary>The final word on whether the Prefab will show its contents.
        /// Takes into account the global Disable Unlocking.</summary>
        public bool WillShowContents => _canShowContents || LockingDisabled || (Application.isPlaying && UnlockInPlayMode);
        /// <summary> Whether the Prefab is kept unlocked because of where it is. This means that its <see cref="_locked"/> property might still be on, but it appears as unlocked.</summary>
        public bool IsUnlockedBecauseNestedOrVariantRoot => _isNested && _unlockIfNested || _isVariantRoot && _unlockIfVariantRoot;
        /// <summary> Whether the Prefab is already unlocked because of one of its settings. This determines if it can be Temp unlocked.</summary>
        public bool IsAlreadyUnlocked => !IsLocked || IsUnlockedBecauseNestedOrVariantRoot;
        
        public SelectionType GetSelectionType => _selectionType;
        public bool NeedsSelectionMesh { get; set; } = true;
        
        /// <summary> This BlackBox is nested under another BlackBox that is currently temp unlocked.</summary>
        public bool NestedInTempUnlockedRoot { get; private set; }

        public bool IsTempUnlocked
        {
            get => _isTempUnlocked;
            set
            {
                _isTempUnlocked = value;
                hideFlags = HideFlags.None;
                EvaluateAndUpdateAppearance();
            }
        }

        public Mesh SelectionMesh
        {
            get => _selectionMesh;
            set => _selectionMesh = value;
        }

        private void Awake()
        {
            CacheReferences();
        }

        internal void CacheReferences()
        {
            _go = gameObject;
            _transform = transform;
        }

        private void OnEnable()
        {
            EditorApplication.delayCall += FirstSetup; // For when using Undo, or Revert

            PrefabStage.prefabStageOpened += OnPrefabStageOpened;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;
        }

        private void OnValidate()
        {
            NeedsSelectionMesh = true; // The user might have changed the value of _selectionType
        }

        private void Reset()
        {
            _serializedVersion = Constants.CurrentSerializedVersion;
            _revealedLists = new[]
            {
                new RevealedItemsList("Default")
                {
                    groupByGameObjects = true,
                    visibility = ListShownWhen.AlwaysVisible,
                }
            };
        }

        public void FirstSetup()
        {
            // Catches the case when the user dragged an object into the scene super fast, then out again
            // At this point the script is now null, but the delayCall from OnEnable still executes
            if (this == null) return;

            // Avoids duplicating work, in case OnPrefabStageOpened triggered already
            // while the delayCall leading here was waiting to execute
            if (hideFlags.HasFlag(HideFlags.DontSaveInBuild)) return;
            
            hideFlags = HideFlags.DontSaveInBuild;
            OnPrefabStageOpened(PrefabStageUtility.GetCurrentPrefabStage());
        }

        private void OnPrefabStageOpened(PrefabStage prefabStage)
        {
            if(_go == null) CacheReferences();

            // No action for Prefabs (scene instance, or previous bases) not being edited
            if (prefabStage != null && !prefabStage.IsPartOfPrefabContents(_go)) return;
       
#if HATS
            if (EvaluateHats(prefabStage)) return;
#endif
            if (SkipUpdatingBecauseParentIsLocked(prefabStage)) return; // No setup needed

            EvaluateAndUpdateAppearance(prefabStage);
        }

#if HATS
        private bool EvaluateHats(PrefabStage prefabStage)
        {
            if (_exitingStage ||
                prefabStage == null ||
                prefabStage.prefabContentsRoot != _go ||
                CheckIfTeamCanEdit()) return false;
            
            _exitingStage = true;
            Debug.LogWarning($"(BlackBox) You cannot edit Prefab {_go.name} because you're not part of the allowed Hats team(s).");
            EditorApplication.delayCall += () =>
            {
                StageUtility.GoBackToPreviousStage();
                _exitingStage = false;
            };
            return true;

        }
        
        private bool CheckIfTeamCanEdit()
        {
            if (ActiveTeamIndex == -1) return true;
            return _teamsAllowedToEdit switch
            {
                -1 => true,
                0 => false,
                _ => (_teamsAllowedToEdit & (1 << ActiveTeamIndex)) != 0
            };
        }
#endif

        private void EvaluateAndUpdateAppearance(PrefabStage prefabStage = null)
        {
            // Only evaluate if locking is on,
            // because WillShowContents depends on LockingDisabled
            if(!LockingDisabled) Evaluate(prefabStage);
            UpdateAppearance();
        }

        private void OnPrefabStageClosing(PrefabStage closedStage)
        {
            if (Application.isPlaying) return; // We don't support exiting a Prefab stage while in Play mode
            
            // Return for Prefab contents not in the active Stage (aka other levels of nesting)
            PrefabStage thisObjectsStage = PrefabStageUtility.GetPrefabStage(_go);
            PrefabStage activeStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (thisObjectsStage != activeStage) return;
        
            // No action for Prefabs edited in the previous Stage that's getting closed
            bool isPartOfCurrentStage = closedStage.IsPartOfPrefabContents(_go);
            if (isPartOfCurrentStage) return;
            
            if (SkipUpdatingBecauseParentIsLocked(activeStage)) return; // No setup needed
            
            if(!LockingDisabled)
            {
                // Only evaluate if locking is on,
                // because WillShowContents depends on LockingDisabled
                Evaluate(activeStage);
            }
            UpdateAppearance();
        }

        internal void Evaluate(PrefabStage prefabStage)
        {
            PrefabAssetType prefabAssetType = PrefabUtility.GetPrefabAssetType(_go);
            
            _inScene = prefabStage == null;
            _isPrefabRoot = prefabStage != null && prefabStage.prefabContentsRoot == _go;
            _isNotAPrefab = prefabAssetType is PrefabAssetType.NotAPrefab;
            _isRegularPrefab = prefabAssetType is PrefabAssetType.Regular;
            _isVariant = prefabAssetType is PrefabAssetType.Variant;
            _isPrefabModel = prefabAssetType is PrefabAssetType.Model;
            _isAsset = AssetDatabase.Contains(_go);
            _isOutermostPrefabInstanceRoot = PrefabUtility.IsOutermostPrefabInstanceRoot(_go);
            _isNested = !_inScene && PrefabUtility.IsPartOfAnyPrefab(_go) && (!_isOutermostPrefabInstanceRoot || !_isPrefabRoot);
            _isVariantRoot = _isPrefabRoot && (_isVariant || _isPrefabModel || _isRegularPrefab);
            _isAddedAsOverride = PrefabUtility.IsAddedComponentOverride(this);
            
            bool editTime = !Application.isPlaying;
            _canShowContents = !_locked // Always show if unlocked
                               || IsTempUnlocked
                               || (_isPrefabRoot && (!_isVariantRoot || _unlockIfVariantRoot)) // Show if is root, but not a variant (to avoid overrides on _locked)
                               || (_isNested && _unlockIfNested)
                               || (editTime && !_isPrefabRoot && _isNotAPrefab) // Plain GameObject in the hierarchy
                               || (editTime && _isAddedAsOverride && !_isAsset); // Added-component override on an instance

            //Debug.Log($"'{_go.name}' ({_go.GetInstanceID()}) in '{_go.scene.name}' | PRoot: {isPrefabRoot} | Type: {prefabAssetType} | CanShow: {_canShowContents}", _go);
        }

        /// <summary> Verify if this Prefab instance in the scene is contained in another Prefab that keeps it locked </summary>
        private bool SkipUpdatingBecauseParentIsLocked(PrefabStage prefabStage)
        {
            bool isPrefabRoot = prefabStage != null && prefabStage.prefabContentsRoot == _go;
            if (isPrefabRoot) return false;
            
            bool isInstanceRoot = PrefabUtility.IsOutermostPrefabInstanceRoot(_go);
            if (isInstanceRoot) return false;

            // Attempting to catch an edge case that happens when code recompiles while in Prefab Mode 
            if(_transform.parent == null) return true;

            GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(_transform.parent);
            if (instanceRoot != null && instanceRoot.TryGetComponent(out BlackBox rootBlackBox))
            {
                NestedInTempUnlockedRoot = rootBlackBox.IsTempUnlocked;
                
                if (instanceRoot != _go && NestedInTempUnlockedRoot) return false;
                
                rootBlackBox.Evaluate(prefabStage);

                // Prefab has to update because parent is revealing it as a child
                // (through any of its components, not just its Transform)
                if (rootBlackBox.IsGORevealedAsChildBecauseOfAnotherComponent(_transform)) return false;
                
                if (!rootBlackBox.WillShowContents)
                {
                    // Parent is locked
                    if (_go.hideFlags == HideFlags.None || _transform.hideFlags == HideFlags.None)
                    {
                        _transform.hideFlags = HideFlags.HideInHierarchy;
                    }
                    
                    //Debug.Log($"{_go.name} will skip updating itself because parent ({instanceRoot.name}) is locked", _go);
                    return true;
                }
            }

            return false;
        }

        private void UpdateAppearance()
        {
            UpdateOwnComponents();
            UpdateChildrenVisibility();
        }

        /// <summary>
        /// Updates components' hideFlags based on <see cref="WillShowContents"/>
        /// </summary>
        private void UpdateOwnComponents()
        {
            BlackBox revealingAncestor = FindAncestorRevealingThisGameObject();

            // A content-hiding ancestor that reveals this GameObject as a child controls what it shows:
            // only the revealed component(s). This wins even when this BlackBox is itself unlocked, which
            // would otherwise strip every hideFlag and undo the ancestor's selective reveal.
            bool shownAsRevealedChild = revealingAncestor != null && !revealingAncestor.WillShowContents;

            foreach (Component comp in _go.GetComponents<Component>())
            {
                if(comp == null) continue;

                if (WillShowContents && !shownAsRevealedChild)
                {
                    SetComponentHideFlags(comp, HideFlags.None);
                }
                else if(comp != this)
                {
                    bool isTransform = comp is Transform or RectTransform;
                    bool isAddedComponentOverride = PrefabUtility.IsAddedComponentOverride(comp);
                    bool isAsset = AssetDatabase.Contains(_go);

                    if ((!isAddedComponentOverride || isAsset) &&
                        ((isTransform && _hideTransform) || !isTransform))
                    {
                        // Keep visible only the component(s) the ancestor revealed; hide every other one.
                        SetComponentHideFlags(comp,
                            revealingAncestor != null && revealingAncestor.IsRevealedAsChild(comp)
                                ? HideFlags.None
                                : HideFlags.HideInInspector);
                    }

                    // Reminder: Don't write hideFlags to none for the BlackBox component here,
                    // as that would disrupt the flow of FirstSetup (where hideFlags is set to DontSaveInBuild)
                }

                if (comp == this)
                {
                    // Keep the BlackBox out of builds. Also hide it from the Inspector when this object is
                    // only visible because an ancestor reveals it through a different component, so the
                    // revealed child shows just that component — not its own BlackBox.
                    bool revealedThroughAnotherComponent = shownAsRevealedChild && !revealingAncestor.IsRevealedAsChild(this);
                    comp.hideFlags = revealedThroughAnotherComponent
                        ? HideFlags.DontSaveInBuild | HideFlags.HideInInspector
                        : HideFlags.DontSaveInBuild;
                }

                //Debug.Log($"{_go} changed {comp.GetType().Name} of {comp.gameObject.name} hideFlags to {comp.hideFlags}");
            }
        }

        /// <summary>
        /// Walks up the hierarchy to find the nearest ancestor BlackBox that reveals this GameObject
        /// as a child through one of its components. Returns null when none does.
        /// </summary>
        private BlackBox FindAncestorRevealingThisGameObject()
        {
            Transform parent = _transform.parent;
            while (parent != null)
            {
                if (parent.TryGetComponent(out BlackBox ancestor) &&
                    ancestor.IsGORevealedAsChildBecauseOfAnotherComponent(_transform))
                    return ancestor;
                parent = parent.parent;
            }
            return null;
        }

        /// <summary>
        /// Sets a component's hideFlags, and — for Renderer subclasses — mirrors the flag onto its
        /// sharedMaterials when toggling between None and HideInInspector.
        /// </summary>
        private static void SetComponentHideFlags(Component comp, HideFlags flags)
        {
            comp.hideFlags = flags;

            if (comp is not Renderer renderer) return;

            Material[] mats = renderer.sharedMaterials;
            foreach (Material mat in mats)
            {
                if (mat == null) continue;
                mat.hideFlags = flags;
            }
        }

        /// <summary>
        /// Updates the hideFlags of child GameObjects and Prefab instances, based on <see cref="WillShowContents"/>
        /// </summary>
        public void UpdateChildrenVisibility()
        {
            if(_protectedAncestors == null) _protectedAncestors = new List<Transform>();
            else _protectedAncestors.Clear();
            
            for (int i = 0; i < _transform.childCount; i++)
            {
                Transform child = _transform.GetChild(i);
                bool isAddedGameObjectOverride = PrefabUtility.IsAddedGameObjectOverride(child.gameObject);
                bool isRevealedAsChild = IsGORevealedAsChildBecauseOfAnotherComponent(child);

                bool showObject = WillShowContents || isAddedGameObjectOverride || isRevealedAsChild;

                // Hide or show both Transform and GameObject
                child.gameObject.hideFlags = child.hideFlags =
                    showObject ? HideFlags.None : HideFlags.HideInHierarchy;
                
                // Check if the child object has a BlackBox that's requesting the Transform to be hidden
                bool hasBlackBox = child.TryGetComponent(out BlackBox blackBoxComp);
                if (hasBlackBox)
                {
                    if (blackBoxComp.IsLocked && blackBoxComp.HideTransform)
                    {
                        // Force the Transform to hide as requested by the BlackBox
                        child.hideFlags = HideFlags.HideInHierarchy;
                        // But keep the GameObject visible
                        if (showObject) child.gameObject.hideFlags = HideFlags.None;
                    }
                    
                    // Force children to update, if this BlackBox
                    // is updating children because it was just temporarily unlocked
                    if (_isTempUnlocked) blackBoxComp.FirstSetup();
                }
                else if (_isTempUnlocked && showObject)
                {
                    // Temp unlock reveals the whole subtree of plain GameObjects and
                    // non-BlackBoxed Prefabs, stopping at any nested BlackBoxed Prefab
                    RevealDescendantsForTempUnlock(child);
                }

                if (isRevealedAsChild) HideChildrenOfRevealedGO(child);
            }
            
            // These can be null when component has just been added
            if (!HasRevealedLists) return;
            if(_revealedLists[CurrentListIndex].revealedItems.Length == 0) return;

            // Now post-process deeper children
            RevealedItem[] items = _revealedLists[CurrentListIndex].revealedItems;

            // Collects unique GameObjects that have revealed children
            _revealedGOComponents ??= new Dictionary<GameObject, HashSet<Component>>();
            _revealedGOComponents.Clear();

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].revealType != RevealType.GameObject) continue;
                GameObject go = items[i].gameObject;
                if (go == null) continue;

                if (!_revealedGOComponents.TryGetValue(go, out HashSet<Component> components))
                {
                    components = new HashSet<Component>();
                    _revealedGOComponents[go] = components;
                }
                components.Add(items[i].component);
            }

            foreach ((GameObject goToBeRevealed, HashSet<Component> components) in _revealedGOComponents)
            {
                goToBeRevealed.hideFlags = HideFlags.None;
                bool goIsBlackBoxOwner = goToBeRevealed == _go;

                foreach (Component compOnRevealedChild in goToBeRevealed.GetComponents<Component>())
                {
                    // Skip Transform and BlackBox on the root
                    if (goIsBlackBoxOwner && compOnRevealedChild is BlackBox or Transform) continue;

                    SetComponentHideFlags(compOnRevealedChild,
                        WillShowContents || components.Contains(compOnRevealedChild)
                            ? HideFlags.None
                            : HideFlags.HideInInspector);
                    
                    if (compOnRevealedChild is Transform revealedTransform) ProcessDeepTransform(revealedTransform);
                }
                
                if (!goIsBlackBoxOwner &&
                    goToBeRevealed.TryGetComponent(out BlackBox blackBox))
                {
                    // The revealed child has a BlackBox of its own, so it might be revealing additional children
                    blackBox.ForceUpdateAppearance();
                }
            }
            
            return;

            void ProcessDeepTransform(Transform revealedTransform)
            {
                Transform parentTransform = revealedTransform.parent;
                if (parentTransform == null) return;
                
                HideChildrenOfRevealedGO(revealedTransform);
                
                // Process siblings
                for (int i = 0; i < parentTransform.childCount; i++)
                {
                    Transform sibling = parentTransform.GetChild(i);
                    if (sibling == revealedTransform) continue;
                    if(_protectedAncestors.Contains(sibling)) continue;
                    if (PrefabUtility.IsAddedGameObjectOverride(sibling.gameObject)) continue;
                    if (IsGORevealedAsChildBecauseOfAnotherComponent(sibling)) continue;

                    sibling.hideFlags = sibling.gameObject.hideFlags =
                        WillShowContents ? HideFlags.None : HideFlags.HideInHierarchy;
                }

                // Process the ancestors
                while (parentTransform != _transform && parentTransform != null)
                {
                    if(_protectedAncestors.Contains(parentTransform)) break;
                    
                    if (IsGORevealedAsChildBecauseOfAnotherComponent(parentTransform))
                    {
                        parentTransform = parentTransform.parent;
                        continue;
                    }

                    parentTransform.hideFlags = parentTransform.gameObject.hideFlags =
                        WillShowContents ? HideFlags.None : HideFlags.NotEditable;
                    
                    foreach (Component comp in parentTransform.GetComponents<Component>())
                    {
                        if (comp == parentTransform) continue; // Skip entirely the Transform already processed
                        SetComponentHideFlags(comp, WillShowContents ? HideFlags.None : HideFlags.HideInInspector);
                    }

                    _protectedAncestors.Add(parentTransform); // Ensure it doesn't get overwritten when processing deeper elements, or siblings
                    parentTransform = parentTransform.parent;
                }
            }

            void HideChildrenOfRevealedGO(Transform child)
            {
                // Hide again all children of any revealed child
                for (int j = 0; j < child.childCount; j++)
                {
                    Transform childNTransform = child.GetChild(j);
                    bool isAddedGameObjectOverride = PrefabUtility.IsAddedGameObjectOverride(childNTransform.gameObject);
                    bool revealedAsChildBecauseOfAnotherComponent = IsGORevealedAsChildBecauseOfAnotherComponent(childNTransform);
                    childNTransform.hideFlags = childNTransform.gameObject.hideFlags =
                        isAddedGameObjectOverride || revealedAsChildBecauseOfAnotherComponent || WillShowContents ? HideFlags.None : HideFlags.HideInHierarchy;
                }
            }
        }

        /// <summary>
        /// While temporarily unlocked, reveals every descendant — plain "folder" GameObjects and
        /// non-BlackBoxed Prefabs — recursively, stopping at (but still showing the node of) any
        /// nested BlackBoxed Prefab, which keeps managing its own contents.
        /// </summary>
        private void RevealDescendantsForTempUnlock(Transform parent)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);

                // Reveal the node itself
                child.gameObject.hideFlags = child.hideFlags = HideFlags.None;

                if (child.TryGetComponent(out BlackBox childBlackBox))
                {
                    // Boundary: a nested BlackBoxed Prefab. Show its node but don't descend —
                    // its own (locked) BlackBox keeps its contents hidden.
                    if (childBlackBox.IsLocked && childBlackBox.HideTransform)
                        child.hideFlags = HideFlags.HideInHierarchy; // GameObject stays visible

                    childBlackBox.FirstSetup();
                }
                else
                {
                    // Plain "folder" GameObject or non-BlackBoxed Prefab: keep descending.
                    RevealDescendantsForTempUnlock(child);
                }
            }
        }

        public bool HasRevealedChildren()
        {
            if (!HasRevealedLists) return false;
            RevealedItem[] items = _revealedLists[CurrentListIndex].revealedItems;
            for (int i = 0; i < items.Length; i++)
                if (items[i].revealType == RevealType.GameObject) return true;
            return false;
        }

        /// <summary>
        /// Checks if a specific component is meant to reveal its GameObject as a child.
        /// To check whether a GameObject is revealed through any of its components, use
        /// <see cref="IsGORevealedAsChildBecauseOfAnotherComponent"/> instead.
        /// </summary>
        public bool IsRevealedAsChild(Component comp)
        {
            if (!HasRevealedLists) return false;
            RevealedItem[] items = _revealedLists[CurrentListIndex].revealedItems;
            for (int i = 0; i < items.Length; i++)
                if (items[i].revealType == RevealType.GameObject && items[i].component == comp) return true;
            return false;
        }

        /// <summary>
        /// Checks if a certain Transform's GameObject is revealed as a Child
        /// because another one of its components is revealed.
        /// </summary>
        public bool IsGORevealedAsChildBecauseOfAnotherComponent(Transform transformComponent)
        {
            if (!HasRevealedLists) return false;
            RevealedItem[] items = _revealedLists[CurrentListIndex].revealedItems;
            for (int i = 0; i < items.Length; i++)
                if (items[i].revealType == RevealType.GameObject && items[i].component != null && items[i].component.transform == transformComponent) return true;
            return false;
        }

        /// <summary>
        /// True if <paramref name="t"/>'s GameObject is revealed as a child via at least one
        /// component AND its Transform is not itself in the revealed items.
        /// </summary>
        public bool IsRevealedChildWithLockedTransform(Transform t)
        {
            if (!HasRevealedLists) return false;
            RevealedItem[] items = _revealedLists[CurrentListIndex].revealedItems;
            bool revealsAny = false;
            bool revealsTransform = false;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].revealType != RevealType.GameObject) continue;
                Component c = items[i].component;
                if (c == null || c.transform != t) continue;
                revealsAny = true;
                if (ReferenceEquals(c, t)) { revealsTransform = true; break; }
            }
            return revealsAny && !revealsTransform;
        }

        public bool IsAncestorOfARevealedChild(Transform ancestorTransform)
        {
            if (!HasRevealedLists) return false;
            RevealedItem[] items = _revealedLists[CurrentListIndex].revealedItems;
            for (int i = 0; i < items.Length; i++)
                if (items[i].revealType == RevealType.GameObject && items[i].component != null && items[i].component.transform.IsChildOf(ancestorTransform)) return true;
            return false;
        }

        /// <summary>
        /// Whether any item has been revealed at all.
        /// </summary>
        public bool HasAnyRevealedItem()
        {
            if (!HasRevealedLists) return false;
            return _revealedLists.Any(list => list.revealedItems.Length > 0);
        }

        /// <summary>
        /// Whether any revealed list has any item that is not a revealed child in the Hierarchy.
        /// </summary>
        public bool HasAnyInspectorRevealedItem()
        {
            if (!HasRevealedLists) return false;
            return _revealedLists
                .SelectMany(revealedItemsList => revealedItemsList.revealedItems)
                .Any(revealedItem => revealedItem.revealType != RevealType.GameObject);
        }

        /// <summary>
        /// Can be used to ensure that references are cached before updating the appearance.
        /// </summary>
        public void ForceUpdateAppearance()
        {
            CacheReferences();
            UpdateAppearance();
        }

        internal void Lock() => _locked = true;
        
        /// <summary>
        /// Only used for GameObjects to force it to appear as unlocked.
        /// </summary>
        internal void Unlock() => _locked = false;

        private void OnDisable()
        {
            PrefabStage.prefabStageOpened -= OnPrefabStageOpened;
            PrefabStage.prefabStageClosing -= OnPrefabStageClosing;
        }
        
        // Restore the Prefab on component removal
        private void OnDestroy()
        {
            _canShowContents = true;
            if(_go != null) UpdateAppearance();
        }
        
        public void OnBeforeSerialize()
        {
            
        }

        public void OnAfterDeserialize()
        {
            if (_serializedVersion < 2.0f)
            {
                if (HasRevealedLists)
                {
                    for (int i = 0; i < _revealedLists.Length; i++)
                    {
                        RevealedItemsList revealedList = _revealedLists[i];
                        RevealedItemsList list = revealedList;
                        list.visibility = list.isVisible ? ListShownWhen.AlwaysVisible : ListShownWhen.Hidden;

                        _revealedLists[i] = list;
                    }
                    _serializedVersion = Constants.CurrentSerializedVersion;
                }
            }
        }
    }

    [Serializable]
    public struct RevealedItem
    {
        public string revealedAs;
        public GameObject gameObject;
        public Component component;
        [FormerlySerializedAs("propertyPath")] public string path;
        public RevealType revealType;
    }

    [Serializable]
    public struct RevealedItemsList
    {
        public string listName;
        public ListShownWhen visibility;
        public bool isVisible;
        public bool groupByGameObjects;
        public RevealedItem[] revealedItems;

        public RevealedItemsList(string newListName)
        {
            listName = newListName;
            isVisible = true;
            groupByGameObjects = true;
            revealedItems = new RevealedItem[] { };
            visibility = ListShownWhen.OnSceneInstances | ListShownWhen.OnVariantRoots | ListShownWhen.WhenNested;
        }
    }

    [Serializable, Flags]
    public enum ListShownWhen
    {
        Hidden = 0,
        OnSceneInstances = 1 << 0,
        WhenNested = 1 << 1,
        OnVariantRoots = 1 << 2,
        AlwaysVisible = ~0,
    }

    [Serializable]
    public enum RevealType
    {
        ComponentProperty,
        GameObjectProperty,
        Method,
        EntireComponent,
        ObjectReference,
        GameObject,
        Comment,
        Header,
        Separator,
    }

    [Serializable]
    public enum SelectionType
    {
        [Tooltip("No selection mesh is created. This requires the root object to have some kind of visible graphics, or an icon assigned.")]
        UseRootObject,
        [Tooltip("Use Bounding Boxes simplifies the selection to the bounding boxes containing the sub-meshes. This is the default.")]
        UseBoundingBoxes,
        [Tooltip("Uses a combined mesh of all MeshRenderer children, which is precise but can be expensive for objects with high-detail geometry.")]
        Use3DMeshes,
        [Tooltip("Uses a combined mesh of all SkinnedMeshRenderer children, which is precise but can be expensive for objects with high-detail geometry.")]
        UseSkinnedMeshRenderers,
        UseSpriteRenderers,
    }
#else
    public class BlackBox : MonoBehaviour { }
#endif
}