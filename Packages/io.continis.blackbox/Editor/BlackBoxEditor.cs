using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BlackBox.CustomEditors;
using BlackBox.Editor.CustomUIControls;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

#if HATS
using Hats.Editor.UIElements;
#endif

#if ODIN_INSPECTOR
using Sirenix.OdinInspector.Editor;
using BaseEditor = Sirenix.OdinInspector.Editor.OdinEditor;
#else
using BaseEditor = UnityEditor.Editor;
#endif

namespace BlackBox.Editor
{
    /// <summary>
    /// Used as "memory" of BlackBox, for settings that need to survive recompilation.
    /// </summary>
    public class BlackBoxMemory : ScriptableSingleton<BlackBoxMemory>
    {
        private BlackBox _unlockedBlackBox;
        public BlackBox UnlockedBlackBox
        {
            get => _unlockedBlackBox;
            set
            {
                if (value == _unlockedBlackBox) return;
                
                if(_unlockedBlackBox != null)
                {
                    // First re-lock the previous instance to avoid overlaps
                    _unlockedBlackBox.IsTempUnlocked = false;
                }
                
                if(value != null)
                {
                    // Temp unlock the new instance
                    value.IsTempUnlocked = true;
                }
                
                _unlockedBlackBox = value;
            }
        }

        private int _currentListIndex;
        public int CurrentListIndex
        {
            get => _currentListIndex;
            set
            {
                _currentListIndex = value;
                BlackBox.LastClickedListIndex = value; // Bring it runtime side
            }
        }

        public bool AddingOverrides;

        /// <summary>
        /// Which BlackBox "type" was last selected. This is not the exact GO that was selected, but the Prefab Asset.
        /// </summary>
        public GameObject LastSelectedBlackBoxAsset { get; set; }

        [InitializeOnLoadMethod]
        private static void SaveSettingsRuntimeSide()
        {
            if (instance.UnlockedBlackBox != null)
                instance.UnlockedBlackBox.IsTempUnlocked = true;
        }

        public void ClearTempUnlock() => UnlockedBlackBox = null;
    }
    
    [CustomEditor(typeof(BlackBox))]
    [CanEditMultipleObjects]
    public class BlackBoxEditor : BaseEditor
    {
#if ODIN_INSPECTOR
        private List<PropertyTree> _trees;
        private List<InspectorProperty> _inspectorProperties;
#endif
        
        [SerializeField] private VisualTreeAsset _internalEditor;
        [SerializeField] private VisualTreeAsset _externalEditor;
        [SerializeField] private VisualTreeAsset _assetMultiSelectionEditor;
        [SerializeField] private VisualTreeAsset _singleProperty;
        [SerializeField] private VisualTreeAsset _singleMethod;
        [SerializeField] private VisualTreeAsset _entireComponent;
        [SerializeField] private VisualTreeAsset _objectReferenceTemplate;
        [SerializeField] private VisualTreeAsset _childGameObjectTemplate;
        [SerializeField] private VisualTreeAsset _allPropertiesTemplate;
        [SerializeField] private StyleSheet _styles;
        
        private SerializedProperty _lockedProp;
        private SerializedProperty _oldRevealedPropertiesProp;
        private SerializedProperty _arrayOfRevealedListsProperty;
        private BlackBox _comp;
        private GameObject _go;

        private readonly float _marginLeft = 13f;
        private readonly int _maxLists = 10;
        
        private bool _isExternalEditor;
        private bool _isMultiInstance; // Editing several scene instances of the same Prefab asset at once
        private SerializedObject[] _targetSerObjects; // Per-instance SerializedObjects, only used in multi-instance mode
        private bool _isDisplayingAvailableProperties;
        private bool _isEditingLists;
        private int _activeListIndex; // Currently selected list of revealed items
        private List<int> _brokenRevealedProperties; // The indexes of broken properties (missing GO or component)
        private Dictionary<int, string> _brokenRevealedPropertyMessages; // Per-index error message for broken items
        private string _previousLockedMsg;

        public static BlackBoxEditor CurrentEditor;
        
        // Visual elements references
        private Button _hideShowBtn;
        private Button _editListsBtn;
        private VisualElement _inspector;
        private VisualElement _availablePropsBlock;
        private VisualElement _revealedPropertiesBlock;
        private VisualElement _componentHeaderOverlay;
        private VisualElement _editModeControls;
        private VisualElement _revealedPropertiesControls;
        private VisualElement _listButtonsGroup;
        private List<Button> _listButtons;
        private Button _addListBtn;
        private HelpBox _propertiesWarningMessage;
        private Button _emptyListBtn;
        private Button _deleteListBtn;
        private Button _moveListLeftBtn;
        private Button _moveListRightBtn;
        private EnumFlagsField _listVisibilityDropdown;
        private TextField _listNameTextField;
        private Toggle _listGroupByGOToggle;
        private ListView _revealedItemsListView;
        private Button _tempUnlockButton;
        private Label _lockedLabel;

        public void Awake()
        {
            CacheReferences();
        }

        private void CacheReferences()
        {
            _comp = (BlackBox)target;
            _go = _comp.gameObject;
            _lockedProp = serializedObject.FindProperty("_locked");
            _arrayOfRevealedListsProperty = serializedObject.FindProperty("_revealedLists");
        }

        /// <summary>
        /// Creates a default list of revealed items, if needed.
        /// </summary>
        private void PrepareDefaultRevealedList(bool isAsset)
        {
            if (_arrayOfRevealedListsProperty.arraySize > 0) return;
            // Skip writes that would become Prefab instance overrides.
            // Safe targets: an asset selected directly, or the current Prefab Stage's contents root.
            if (!isAsset && !_comp.IsPrefabRoot) return;

            serializedObject.Update();
            serializedObject.FindProperty("_serializedVersion").floatValue = global::BlackBox.Constants.CurrentSerializedVersion;
            AddRevealedList("Default");
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            if (isAsset) EditorApplication.delayCall += AssetDatabase.SaveAssets;
        }

#if ODIN_INSPECTOR
        protected override void OnEnable()
        {
            _trees = new List<PropertyTree>();
            _inspectorProperties = new List<InspectorProperty>();
#else
        private void OnEnable()
        {
#endif
            CurrentEditor = this;
            PrefabUtility.prefabInstanceUpdated += PrefabInstanceUpdated;
        }

#if ODIN_INSPECTOR
        protected override void OnDisable()
        {
            if ( _trees != null )
            {
                foreach (PropertyTree tree in _trees) tree.Dispose();
                _trees = null;
            }

            if (_inspectorProperties != null)
                foreach (InspectorProperty inspectorProperty in _inspectorProperties) inspectorProperty.Dispose();
#else
        private void OnDisable()
        {
#endif
            CurrentEditor = null;
            PrefabUtility.prefabInstanceUpdated -= PrefabInstanceUpdated;

            if (_internalEditor)
            {
                _revealedItemsListView?.Unbind();
                _listNameTextField?.Unbind();
                _listVisibilityDropdown?.Unbind();
                _listGroupByGOToggle?.Unbind();
            }
        }

        private void PrefabInstanceUpdated(GameObject instance)
        {
            // Clean default list created after making a Prefab Variant out of a scene instance
            if (PrefabUtility.HasPrefabInstanceAnyOverrides(instance, false))
            {
                if (instance.TryGetComponent(out BlackBox blackBox))
                {
                    SerializedObject serObj = new(blackBox);
                    SerializedProperty listsProp = serObj.FindProperty("_revealedLists");
                    bool areListsOverridden = listsProp.prefabOverride;
                    if(areListsOverridden && !AnimationMode.InAnimationMode())
                    {
                        PrefabUtility.RevertPropertyOverride(listsProp, InteractionMode.AutomatedAction);
                        serObj.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
            }
            
            // Update the Inspector in case object was selected
            if (_isExternalEditor && instance == _go && PrefabStageUtility.GetPrefabStage(_go) == PrefabStageUtility.GetCurrentPrefabStage()) DisplayRevealedPropertiesExternal();
        }

        internal enum InspectorType
        {
            Internal,
            External,
            OverrideError, // Only in the scene
            GameObjectError,
            Unknown,
        }

        public override VisualElement CreateInspectorGUI()
        {
            _inspector = new();
            _isMultiInstance = false;

            // Handling multiple selection
            if (targets.Length > 1)
            {
                ClassifyMultiSelection(out int assetCount, out int instanceCount, out int otherCount, out bool instancesShareSource);
                int total = assetCount + instanceCount + otherCount;

                // All selected are Prefab assets: mass-edit their settings
                if (total > 0 && assetCount == total)
                {
                    _assetMultiSelectionEditor.CloneTree(_inspector);
                    return _inspector;
                }

                // All selected are scene instances of the same Prefab: edit their revealed items together
                if (total > 0 && instanceCount == total && instancesShareSource)
                {
                    SetupMultiInstanceInspector();
                    return _inspector;
                }

                // Otherwise, explain precisely why this particular combination can't be edited together
                _inspector.Add(BuildMultiSelectionUnsupportedHelpBox(assetCount, instanceCount, otherCount, instancesShareSource));
                return _inspector;
            }

            _isDisplayingAvailableProperties = false;
            if(_lockedProp == null) CacheReferences();
            
            // If the Prefab has been dragged and dropped into the scene,
            // Unity forces the hideFlags of the GameObject (and its components) to None on release,
            // which reveals all components.
            // This if is meant to catch that case, and force the object to update itself
            if(_comp.hideFlags == HideFlags.None) _comp.FirstSetup();
            
            // Determine which Inspector to show
            _comp.Evaluate(PrefabStageUtility.GetCurrentPrefabStage());
            InspectorType inspectorType = DetermineInspectorType(_comp);

            PrepareDefaultRevealedList(_comp.IsAsset);

            GameObject prefabAsset = _comp.IsAsset ? _go : PrefabUtility.GetCorrespondingObjectFromSource(_go);
            
            // Attempt to retrieve last selected list number, if the source asset is the same
            if (BlackBoxMemory.instance.LastSelectedBlackBoxAsset == prefabAsset)
                _activeListIndex = BlackBoxMemory.instance.CurrentListIndex;
            else
                BlackBoxMemory.instance.CurrentListIndex = 0;

            BlackBoxMemory.instance.LastSelectedBlackBoxAsset = prefabAsset;
            
            // Display inspector
            switch (inspectorType)
            {
                case InspectorType.Internal:
                {
                    // Prefab Mode Inspector ("Internal")
                    // - The root of a regular Prefab when in Prefab mode
                    // - The root of a Prefab Variant with Override BlackBox on
                    // - A Nested Prefab with Override BlackBox on
                    // - Added as an override to a Variant Root
                
                    _internalEditor.CloneTree(_inspector);
                    bool showOverrideHeader = (_comp.IsVariantRoot || _comp.IsNested) && !_comp.IsAddedAsOverride;
                    _inspector.Q<VisualElement>("OverrideHeader").ShowOrHide(showOverrideHeader);
                    if (showOverrideHeader)
                    {
                        Button goBackButton = _inspector.Q<Button>("OverridesBackButton");
                        goBackButton.clicked += OnOverridesBackBtnClicked;
                        EditorApplication.delayCall += () =>
                        {
                            goBackButton.Focus();
                        };
                    }
                    _inspector.AddToClassList("internal");
                    _isExternalEditor = false;
#if HATS
                    DisplayPersonaSelector();
#endif
                    DisplayDisabledMessage();
                    DisplayRevealedPropertiesInternal();

                    return _inspector;
                    
                    void OnOverridesBackBtnClicked()
                    {
                        BlackBoxMemory.instance.AddingOverrides = false;
                        ActiveEditorTracker.sharedTracker.ForceRebuild();
                    }
                }

                case InspectorType.External:
                {
                    // Instance Inspector ("External")
                    // All the rest:
                    // - A Prefab asset
                    // - A Prefab instance in the scene
                    // - A Variant Root with Override BlackBox off
                    // - A Nested Prefab with Override BlackBox off
                
                    if(_comp.IsAsset) _comp.ForceUpdateAppearance(); // So components are made invisible
                
                    _externalEditor.CloneTree(_inspector);
                    _inspector.AddToClassList("external");
                    _isExternalEditor = true;

                    ReactToPlayMode();
                    if(!Application.isPlaying)
                    {
                        DisplayStatusMessages();
                        SetupTempUnlockedButton();
                        SetupOverridesButton();
                    }
                    DisplayDisabledMessage();
                    DisplayButtonsExternal();
                    DisplayRevealedPropertiesExternal();
                
                    _inspector.RegisterCallback<AttachToPanelEvent>(ParentReady);
#if UNITY_2022_1_OR_NEWER
                    if (!_comp.IsAddedAsOverride)
                    {
                        _inspector.RegisterCallback<AttachToPanelEvent>(ObscureHeader);
                        _inspector.RegisterCallback<DetachFromPanelEvent>(RestoreHeader);
                    }
#endif
                    
                    return _inspector;
                }
                
                case InspectorType.OverrideError:
                {
                    // - Added as an override in the scene
                    _inspector.Add(new HelpBox("BlackBox can't be used as an override. Either remove the component, or Apply the override.", HelpBoxMessageType.Error));
                    
                    return _inspector;
                }
                
                case InspectorType.GameObjectError:
                {
                    // - A regular GameObject (outside of Play Mode)
                    _inspector.Add(new HelpBox("BlackBox can't be used on GameObjects, only on Prefabs.",
                        HelpBoxMessageType.Error));
                    
                    return _inspector;
                }
                
                default:
                case InspectorType.Unknown:
                {
                    throw new NotImplementedException();
                }
            }
        }

        internal static InspectorType DetermineInspectorType(BlackBox comp)
        {
            if (comp.IsNotAPrefab && comp.InScene && !comp.IsAsset && !Application.isPlaying) return InspectorType.GameObjectError;
            if (comp.IsAsset) return InspectorType.External;
            if (comp.IsAddedAsOverride) return comp.IsVariantRoot ? InspectorType.Internal : InspectorType.OverrideError;
            if (comp.IsPrefabRoot && !comp.IsVariantRoot) return InspectorType.Internal;
            if (comp.IsVariantRoot || comp.IsNested) return BlackBoxMemory.instance.AddingOverrides ? InspectorType.Internal : InspectorType.External;
            return InspectorType.External;
        }

        #region Internal Inspector

        private void DisplayRevealedPropertiesInternal()
        {
            _propertiesWarningMessage = _inspector.Q<HelpBox>("RevealedPropertiesWarning");
            _listButtonsGroup = _inspector.Q<VisualElement>("ListButtonsGroup");
            _addListBtn = _inspector.Q<Button>("AddListButton");
            _hideShowBtn = _inspector.Q<Button>("HideShowButton");
            _editListsBtn = _inspector.Q<Button>("EditListsButton");
            _editModeControls = _inspector.Q<VisualElement>("EditModeControls");
            _revealedPropertiesControls = _inspector.Q<VisualElement>("RevealedPropertiesControls");
            _emptyListBtn = _editModeControls.Q<Button>("EmptyListBtn");
            _deleteListBtn = _editModeControls.Q<Button>("DeleteListBtn");
            _moveListLeftBtn = _editModeControls.Q<Button>("MoveListLeftBtn");
            _moveListRightBtn = _editModeControls.Q<Button>("MoveListRightBtn");
            _listNameTextField = _editModeControls.Q<TextField>("ListName");
            _listGroupByGOToggle = _editModeControls.Q<Toggle>("ListGroupByGO");
            _listVisibilityDropdown = _editModeControls.Q<EnumFlagsField>("ListVisibilityDropdown");
            _revealedItemsListView = _inspector.Q<ListView>("RevealedItemsListView");

            _addListBtn.clicked += AddNewListViaButton;
            _hideShowBtn.clicked += HideShowAvailableProperties;
            _editListsBtn.clicked += () => ToggleEditMode();
            _emptyListBtn.clicked += EmptyList;
            _deleteListBtn.clicked += DeleteList;
            _moveListLeftBtn.clicked += () => MoveList(-1);
            _moveListRightBtn.clicked += () => MoveList(1);

            _revealedItemsListView.makeItem = RevealedItem_MakeItem;
            _revealedItemsListView.bindItem = RevealedItem_BindItem;
            _revealedItemsListView.unbindItem = RevealedItem_UnbindItem;
            _revealedItemsListView.itemIndexChanged += (_, _) =>
            {
                serializedObject.ApplyModifiedProperties();
                Undo.SetCurrentGroupName("BlackBox Reorder Revealed Item");
            };
            _revealedItemsListView.itemsRemoved += OnItemsRemovedFromListView;
            SetupAddDropdown();

            ToggleEditMode(false); // Initial setup (also calls RefreshRevealedItemsListView)
        }

        /// <summary>
        /// Refreshes the always-visible ListView that displays the active Reveal list's items in the
        /// Internal Inspector. Hides the ListView (and shows a warning) when no list is available or empty.
        /// </summary>
        private void RefreshRevealedItemsListView()
        {
            serializedObject.UpdateIfRequiredOrScript();

            _brokenRevealedProperties = new List<int>();
            _brokenRevealedPropertyMessages = null;

            bool listAvailable = TryFindVisibleList();

            _hideShowBtn.SetEnabled(listAvailable);
            _revealedItemsListView.SetEnabled(listAvailable);

            SerializedProperty revealedItemsArrayProp = listAvailable ? GetActiveListsRevealedItemsArrayProp() : null;

            if (listAvailable)
            {
                _propertiesWarningMessage.Hide();
                PopulateBrokenRevealedProperties(null);
                _revealedItemsListView.BindProperty(revealedItemsArrayProp);
            }
            else
            {
                _propertiesWarningMessage.Show();
                _propertiesWarningMessage.text =
                    "No visible reveal list. If you want to reveal items, go into Edit Lists mode and ensure at least one reveal list is visible. <a href=\"https://tools.continis.io/black-box/main-features/reveal\">Help</a>";
                _propertiesWarningMessage.messageType = HelpBoxMessageType.Warning;

                _revealedItemsListView.Unbind();
                _revealedItemsListView.itemsSource = Array.Empty<object>();
            }

            _revealedItemsListView.Rebuild();
        }

        private VisualElement RevealedItem_MakeItem() => new() { name = "RevealedItemRow" };

        private void RevealedItem_BindItem(VisualElement container, int index)
        {
            container.Clear();

            SerializedProperty revealedItemsArrayProp = GetActiveListsRevealedItemsArrayProp();
            if (revealedItemsArrayProp == null || index >= revealedItemsArrayProp.arraySize) return;

            SerializedProperty prop = revealedItemsArrayProp.GetArrayElementAtIndex(index);
            VisualElement line = BuildRowForRevealedItem(prop, index);
            if (line == null) return;

            // The per-row [-] button is no longer used; ListView's footer handles deletion.
            line.Q<Button>("AddRemoveButton")?.Hide();
            container.Add(line);
        }

        private void RevealedItem_UnbindItem(VisualElement container, int index)
        {
            container.Unbind();
            container.Clear();
        }

        /// <summary>
        /// Builds the rich row UI for the revealed item at <paramref name="index"/>. Returns null
        /// for items that are flagged as broken (so the ListView row stays empty).
        /// </summary>
        private VisualElement BuildRowForRevealedItem(SerializedProperty prop, int index)
        {
            if (_brokenRevealedProperties != null && _brokenRevealedProperties.Contains(index))
            {
                string msg = _brokenRevealedPropertyMessages != null && _brokenRevealedPropertyMessages.TryGetValue(index, out string m)
                    ? m
                    : "Broken revealed item.";
                HelpBox errorBox = new(msg, HelpBoxMessageType.Warning);
                errorBox.AddToClassList("HelpBox");
                return errorBox;
            }

            SerializedProperty componentProp = prop.FindPropertyRelative(nameof(RevealedItem.component));
            SerializedProperty pathProp = prop.FindPropertyRelative(nameof(RevealedItem.path));
            SerializedProperty gameObjectProp = prop.FindPropertyRelative(nameof(RevealedItem.gameObject));
            SerializedProperty revealedNameProp = prop.FindPropertyRelative(nameof(RevealedItem.revealedAs));
            SerializedProperty revealTypeProp = prop.FindPropertyRelative(nameof(RevealedItem.revealType));

            RevealType revealType = (RevealType)revealTypeProp.enumValueIndex;

            if (IsAnnotationType(revealType))
                return BuildAnnotationConfigLine(revealType, revealedNameProp);

            Object targetObject = componentProp.objectReferenceValue == null
                ? gameObjectProp.objectReferenceValue
                : componentProp.objectReferenceValue;
            if (targetObject == null) return null;

            SerializedObject serObj = new(targetObject);

            VisualElement line;
            bool hasRevealedName = true;

            switch (revealType)
            {
                case RevealType.ComponentProperty or RevealType.GameObjectProperty:
                {
                    SerializedProperty actualProperty = serObj.FindProperty(pathProp.stringValue);
                    line = BuildPropertyLine(actualProperty, serObj, actualProperty.displayName, false);

                    Label revealTypeLabel = line.Q<Label>("TypeLabel");
                    Component compForRow = componentProp.objectReferenceValue as Component;
                    GameObject goForRow = gameObjectProp.objectReferenceValue as GameObject;
                    string details = revealType == RevealType.GameObjectProperty
                        ? DescribeGameObject(goForRow)
                        : DescribeComponent(compForRow);
                    SetupTypeLabel(revealTypeLabel,
                        revealType == RevealType.GameObjectProperty ? (Object)goForRow : compForRow,
                        $"Property of {details}.");
                    break;
                }

                case RevealType.Method:
                {
                    line = BuildMethodLine(pathProp.stringValue);

                    Label revealTypeLabel = line.Q<Label>("TypeLabel");
                    Component compForRow = componentProp.objectReferenceValue as Component;
                    SetupTypeLabel(revealTypeLabel, compForRow, $"Method of {DescribeComponent(compForRow)}.");
                    break;
                }

                case RevealType.EntireComponent:
                {
                    line = _entireComponent.Instantiate();

                    Label revealTypeLabel = line.Q<Label>("TypeLabel");
                    Component compForRow = componentProp.objectReferenceValue as Component;
                    SetupTypeLabel(revealTypeLabel, compForRow, $"Entire {DescribeComponent(compForRow)}.");

                    Foldout inspectorFoldout = new()
                    {
                        value = false,
                        text = $"Entire {componentProp.objectReferenceValue.GetType().Name} component"
                    };
                    inspectorFoldout.AddToClassList("EntireInspectorFoldout");
                    InspectorElement inspectorElement = new(componentProp.objectReferenceValue);
                    inspectorElement.SetEnabled(false);
                    inspectorFoldout.Add(inspectorElement);
                    line.Q<VisualElement>("RevealedComponent").Add(inspectorFoldout);

                    hasRevealedName = false;
                    break;
                }

                case RevealType.ObjectReference:
                {
                    line = _objectReferenceTemplate.Instantiate();
                    string defaultDisplayedName = componentProp.objectReferenceValue == null ?
                        gameObjectProp.objectReferenceValue.name :
                        componentProp.objectReferenceValue.GetType().Name;

                    Label revealTypeLabel = line.Q<Label>("TypeLabel");
                    Component compForRow = componentProp.objectReferenceValue as Component;
                    GameObject goForRow = gameObjectProp.objectReferenceValue as GameObject;
                    string nameForLabel = compForRow == null
                        ? DescribeGameObject(goForRow)
                        : DescribeComponent(compForRow);
                    SetupTypeLabel(revealTypeLabel,
                        compForRow != null ? (Object)compForRow : goForRow,
                        $"Reference to {nameForLabel}.");

                    ObjectField field = line.Q<ObjectField>("ActualProp");
                    field.value = serObj.targetObject;
                    break;
                }

                case RevealType.GameObject:
                {
                    line = _childGameObjectTemplate.Instantiate();
                    hasRevealedName = false;

                    GameObject goForRow = gameObjectProp.objectReferenceValue as GameObject;
                    SetupTypeLabel(line.Q<Label>("TypeLabel"), goForRow, $"Child {DescribeGameObject(goForRow)}.");

                    ObjectField field = line.Q<ObjectField>("ActualProp");
                    field.value = serObj.targetObject;
                    break;
                }

                default:
                {
                    Debug.LogError($"{Constants.LogPrefix} A revealed item has an unexpected RevealType. Please remove it using the Debug Inspector.");
                    return new VisualElement();
                }
            }

            if (hasRevealedName)
            {
                TextField namePropertyField = line.Q<TextField>("RevealedName");
                namePropertyField.BindProperty(revealedNameProp);
                namePropertyField.RegisterCallback<GeometryChangedEvent>(evt =>
                {
                    TextField el = evt.target as TextField;
                    TextField textField = el.Q<TextField>();
                    if (textField != null) textField.isDelayed = true;
                });
            }

            return line;
        }

        private void OnItemsRemovedFromListView(IEnumerable<int> indices)
        {
            serializedObject.ApplyModifiedProperties();
            Undo.SetCurrentGroupName("BlackBox Remove Revealed Item");

            EditorApplication.delayCall += RefreshRevealedItemsListView;
        }

        private void SetupAddDropdown()
        {
            // Wait until the footer is built before swapping the add button's click behaviour.
            _revealedItemsListView.RegisterCallback<GeometryChangedEvent>(OnListViewGeometryChangedSetupAdd);
        }

        private void OnListViewGeometryChangedSetupAdd(GeometryChangedEvent _)
        {
            Button addBtn = _revealedItemsListView.Q<Button>("unity-list-view__add-button")
                            ?? _revealedItemsListView.Q<Button>(className: "unity-list-view__add-button");
            if (addBtn == null) return;

            _revealedItemsListView.UnregisterCallback<GeometryChangedEvent>(OnListViewGeometryChangedSetupAdd);
            addBtn.clickable = new Clickable(() => ShowAddMenu(addBtn));
        }

        private void ShowAddMenu(VisualElement anchor)
        {
            GenericMenu menu = new();
            menu.AddItem(new GUIContent("Header"), false, () => OnClickAddToRevealed(null, null, "", "New header", RevealType.Header));
            menu.AddItem(new GUIContent("Comment"), false, () => OnClickAddToRevealed(null, null, "", "New comment", RevealType.Comment));
            menu.AddItem(new GUIContent("Separator"), false, () => OnClickAddToRevealed(null, null, "", "", RevealType.Separator));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Properties, Methods…"), false, ShowAvailableProperties);
            menu.DropDown(anchor.worldBound);
        }

        /// <summary>
        /// Always-open variant of <see cref="HideShowAvailableProperties"/>: opens the Available
        /// Properties panel without toggling. If already open, it's rebuilt to keep state fresh.
        /// </summary>
        private void ShowAvailableProperties()
        {
            if (_isDisplayingAvailableProperties) HideAvailableProperties();
            _isDisplayingAvailableProperties = true;
            _hideShowBtn.text = "Hide Revealable Items";
            DisplayAvailableProperties();
            FlashHideShowButton();
        }

        /// <summary>
        /// Briefly highlights the "Show/Hide Revealable Items" button in blue, so the user notices
        /// that the action they triggered remotely (via the ListView footer dropdown) is the same
        /// one this button performs.
        /// </summary>
        private void FlashHideShowButton()
        {
            const string flashClass = "flash-highlight";

            _hideShowBtn.RemoveFromClassList(flashClass);

            _hideShowBtn.schedule.Execute(() => _hideShowBtn.AddToClassList(flashClass)).StartingIn(0);
            _hideShowBtn.schedule.Execute(() => _hideShowBtn.RemoveFromClassList(flashClass)).StartingIn(350);
        }

        /// <summary>
        /// Callback when pressing the little [+] button, while editing revealed lists. It also updates the list buttons.
        /// </summary>
        private void AddNewListViaButton()
        {
            serializedObject.Update();
            AddRevealedList($"List {_arrayOfRevealedListsProperty.arraySize + 1}");
            serializedObject.ApplyModifiedProperties();
            Undo.SetCurrentGroupName("BlackBox Add List");
            
            _activeListIndex = _arrayOfRevealedListsProperty.arraySize -1;
            
            DrawEditMode();
            ClearPreviousListButtons();
            SetupListButtons();
        }

        /// <summary>
        /// Adds a new list of revealed items to the BlackBox. Doesn't save the serializedObject.
        /// </summary>
        private void AddRevealedList(string newListName)
        {
            int newListIndex = _arrayOfRevealedListsProperty.arraySize;
            _arrayOfRevealedListsProperty.InsertArrayElementAtIndex(newListIndex);
            SerializedProperty newListProp = _arrayOfRevealedListsProperty.GetArrayElementAtIndex(newListIndex);
            newListProp.FindPropertyRelative(nameof(RevealedItemsList.listName)).stringValue = newListName;
            newListProp.FindPropertyRelative(nameof(RevealedItemsList.groupByGameObjects)).boolValue = true;
            newListProp.FindPropertyRelative(nameof(RevealedItemsList.visibility)).enumValueFlag = (int)ListShownWhen.AlwaysVisible;
            newListProp.FindPropertyRelative(nameof(RevealedItemsList.revealedItems)).arraySize = 0;
        }

        /// <summary>
        /// Removes all list Buttons from the lists button bar, and empties the array with their references.
        /// </summary>
        private void ClearPreviousListButtons()
        {
            if (_listButtons == null) return;
            foreach (Button listButton in _listButtons) listButton.RemoveFromHierarchy();
            _listButtons = new List<Button>();
        }

        /// <summary>
        /// Creates Button visual elements for as many revealed lists the BlackBox has.
        /// </summary>
        private void SetupListButtons()
        {
            int nOfLists = _arrayOfRevealedListsProperty.arraySize;
            if (nOfLists == 1 && !_isEditingLists)
            {
                // No buttons to display
                _listButtonsGroup.Hide();
                return;
            }

            bool reachedMaxLists = nOfLists == _maxLists;

            _listButtonsGroup.Show();
            _listButtons = new List<Button>();
            List<Button> renderedButtons = new List<Button>();

            for (int i = 0; i < nOfLists; i++)
            {
                int index = i;
                SerializedProperty listProp = _arrayOfRevealedListsProperty.GetArrayElementAtIndex(index);
                bool isListVisible = ComputeListVisibility(listProp);
                bool isListHidden = listProp.FindPropertyRelative(nameof(RevealedItemsList.visibility)).enumValueFlag == 0;

                _listButtons.Add(new Button());
                Button currentButton = _listButtons[index];

                // Skip buttons that shouldn't be rendered in the current mode
                if (_isExternalEditor && !isListVisible) continue;
                if (!_isEditingLists && isListHidden) continue;

                currentButton.text = listProp.FindPropertyRelative(nameof(RevealedItemsList.listName)).stringValue;
                currentButton.clicked += () => SwitchActiveList(index);

                currentButton.AddToClassList("ListButton");
                if (isListHidden) currentButton.AddToClassList("DimmedBtn");
                if(_activeListIndex == index) currentButton.AddToClassList("ButtonSelected");

                renderedButtons.Add(currentButton);
                _listButtonsGroup.Add(currentButton);
            }

            // Hide the whole row when nothing meaningful is rendered (only the + button would be left)
            if (!_isEditingLists && renderedButtons.Count <= 1)
            {
                _listButtonsGroup.Hide();
                return;
            }

            // Apply corner classes based on each button's position in the rendered order, not its array
            // index, so the corners stay rounded when leading/trailing lists are hidden.
            for (int i = 0; i < renderedButtons.Count; i++)
            {
                Button btn = renderedButtons[i];
                bool isFirst = i == 0;
                bool isLast = i == renderedButtons.Count - 1;
                if (isFirst && (renderedButtons.Count > 1 || _isEditingLists))
                    btn.AddToClassList("GroupBtnLeft");
                else if (isLast && !_isEditingLists)
                    btn.AddToClassList("GroupBtnRight");
                else
                    btn.AddToClassList("GroupBtnCenter");
            }

            if (_isEditingLists)
            {
                _addListBtn.Show();
                _addListBtn.BringToFront();
                _addListBtn.SetEnabled(!reachedMaxLists);
            }
            else _addListBtn.Hide();
        }
        
        private bool ComputeListVisibility(SerializedProperty listProp)
        {
            ListShownWhen shownWhen = (ListShownWhen)listProp.FindPropertyRelative(nameof(RevealedItemsList.visibility)).enumValueFlag;
            bool isSceneInstance = !_comp.IsAsset && PrefabStageUtility.GetCurrentPrefabStage() == null;
            
            if(shownWhen == ListShownWhen.Hidden) return false;
            if(isSceneInstance && !shownWhen.HasFlag(ListShownWhen.OnSceneInstances)) return false;
            if(_comp.IsNested && !shownWhen.HasFlag(ListShownWhen.WhenNested)) return false;
            if(_comp.IsVariantRoot && !shownWhen.HasFlag(ListShownWhen.OnVariantRoots)) return false;
                    
            return true;
        }

        private void SwitchActiveList(int newListIndex)
        {
            bool oldListHasRevealedChildren = _comp.HasRevealedChildren();
            
            _activeListIndex = newListIndex;
            BlackBoxMemory.instance.CurrentListIndex = _activeListIndex;
            
            if (_isExternalEditor)
            {
                DisplayRevealedPropertiesExternal();
                bool newListHasRevealedChildren = _comp.HasRevealedChildren();
                if (oldListHasRevealedChildren || newListHasRevealedChildren)
                {
                    if (_isMultiInstance)
                        foreach (Object t in targets) ((BlackBox)t).UpdateChildrenVisibility();
                    else
                        _comp.UpdateChildrenVisibility();
                }
            }
            else
            {
                if (_isEditingLists) DrawEditMode();
                RefreshRevealedItemsListView();
            }

            ClearPreviousListButtons();
            SetupListButtons();
        }

        /// <summary>
        /// Toggles between edit lists mode. Hides relevant elements, and recreates list Button visual elements as needed.
        /// </summary>
        /// <param name="switchMode">Whether to switch mode to the opposite, or leave it to the current value.</param>
        private void ToggleEditMode(bool switchMode = true)
        {
            if(switchMode) _isEditingLists = !_isEditingLists;

            if (_isEditingLists) _editListsBtn.AddToClassList("ButtonToggledOn"); else _editListsBtn.RemoveFromClassList("ButtonToggledOn");
            _editListsBtn.text = _isEditingLists ? "Back" : "Edit Lists";
            _editModeControls.style.display = _isEditingLists ? DisplayStyle.Flex : DisplayStyle.None;
            _revealedPropertiesControls.style.display = _isEditingLists ? DisplayStyle.None : DisplayStyle.Flex;
            if(_isDisplayingAvailableProperties) HideShowAvailableProperties();

            // Catch when going back from editing with a list that is now invisible
            if (!_isEditingLists && _arrayOfRevealedListsProperty.arraySize > 0 && !ComputeListVisibility(GetActiveListProp())) _activeListIndex = 0;

            if(_isEditingLists) DrawEditMode();
            RefreshRevealedItemsListView();

            ClearPreviousListButtons();
            SetupListButtons();
        }

        private void DrawEditMode()
        {
            // Unregister before re-binding so the next BindProperty's sync event can't run our handlers
            // against a stale active list; we re-register them AFTER BindProperty so they fire after the
            // binding's internal handler writes the user's input back to the SerializedProperty.
            _listNameTextField.UnregisterValueChangedCallback(ListNameChanged);
            _listVisibilityDropdown.UnregisterValueChangedCallback(ListVisibilityChanged);

            _listNameTextField.BindProperty(GetActiveListsNameProp());
            _listGroupByGOToggle.BindProperty(GetActiveListsGroupByGOProp());
            _listVisibilityDropdown.BindProperty(GetActiveListsVisibilityProp());

            _listNameTextField.RegisterValueChangedCallback(ListNameChanged);
            _listVisibilityDropdown.RegisterValueChangedCallback(ListVisibilityChanged);

            // Special treatment for default list
            _deleteListBtn.SetEnabled(_activeListIndex != 0);
            _moveListLeftBtn.SetEnabled(_activeListIndex != 0);
            _moveListRightBtn.SetEnabled(_activeListIndex != _arrayOfRevealedListsProperty.arraySize -1);
        }

        private void ListNameChanged(ChangeEvent<string> evt)
        {
            if (_activeListIndex < 0 || _activeListIndex >= _listButtons.Count) return;
            _listButtons[_activeListIndex].text = GetActiveListsNameProp().stringValue;
        }

        private void ListVisibilityChanged(ChangeEvent<Enum> changeEvent)
        {
            if (_activeListIndex < 0 || _activeListIndex >= _listButtons.Count) return;
            bool isListHidden = (ListShownWhen)GetActiveListsVisibilityProp().enumValueFlag == ListShownWhen.Hidden;
            _listButtons[_activeListIndex].EnableInClassList("DimmedBtn", isListHidden);
        }

        private void MoveList(int shift)
        {
            serializedObject.Update();
            _arrayOfRevealedListsProperty.MoveArrayElement(_activeListIndex, _activeListIndex + shift);
            serializedObject.ApplyModifiedProperties();
            Undo.SetCurrentGroupName("BlackBox Move List");
            
            _activeListIndex += shift;
            DrawEditMode();
            ClearPreviousListButtons();
            SetupListButtons();
        }

        private void DeleteList()
        {
            bool removeConfirmed;
            if (GetActiveListsRevealedItemsArrayProp().arraySize == 0) removeConfirmed = true;
            else
                removeConfirmed = EditorUtility.DisplayDialog("Delete List?",
                $"Are you sure you want to delete {GetActiveListsNameProp().stringValue}?",
                "Delete List", "Cancel");

            if (!removeConfirmed) return;
            
            serializedObject.Update();
            _arrayOfRevealedListsProperty.DeleteArrayElementAtIndex(_activeListIndex);
            serializedObject.ApplyModifiedProperties();
            Undo.SetCurrentGroupName("BlackBox Delete List");

            _activeListIndex--;
            DrawEditMode();
            ClearPreviousListButtons();
            SetupListButtons();
        }

        private void EmptyList()
        {
            bool removeAll = EditorUtility.DisplayDialog("Remove All Revealed?",
                $"Are you sure you want to remove all revealed properties and methods in {GetActiveListsNameProp().stringValue}?",
                "Remove All", "Cancel");

            if (!removeAll) return;
            
            serializedObject.Update();
            GetActiveListsRevealedItemsArrayProp().ClearArray();
            serializedObject.ApplyModifiedProperties();
            Undo.SetCurrentGroupName("BlackBox Empty List");

            RefreshRevealedItemsListView();
        }

        private void OnClickRemoveFromRevealed(int i)
        {
            serializedObject.Update();
            GetActiveListsRevealedItemsArrayProp().DeleteArrayElementAtIndex(i);
            serializedObject.ApplyModifiedProperties();
            Undo.SetCurrentGroupName("BlackBox Remove Revealed Item");

            serializedObject.Update();
            if (_isEditingLists) DrawEditMode();
            RefreshRevealedItemsListView();
        }

        private void HideShowAvailableProperties()
        {
            _isDisplayingAvailableProperties = !_isDisplayingAvailableProperties;
            _hideShowBtn.text = _isDisplayingAvailableProperties
                ? "Hide Revealable Items"
                : "Show Revealable Items";
            if (_isDisplayingAvailableProperties) DisplayAvailableProperties();
            else HideAvailableProperties();
        }

        private void DisplayAvailableProperties()
        {
            _availablePropsBlock = new VisualElement
            {
                name = "AvailableProperties",
                style = { marginLeft = _marginLeft }
            };

            Foldout foldout = new() { text = "This object", value = false };
            VisualElement innerElement = foldout.Q<VisualElement>(className: "unity-foldout__input");
            innerElement.AddToClassList("GOName");
            innerElement.AddToClassList("FoldoutGO");
            Component[] components = _go.GetComponents<Component>();
            foldout.contentContainer.Add(IterateComponents(components, true));
            _availablePropsBlock.Add(foldout);
    
            IterateChildren(_go.transform, 0);
            
            _inspector.Add(_availablePropsBlock);
        }
        
        private void IterateChildren(Transform parentTransform, int depth)
        {
            for (int i = 0; i < parentTransform.childCount; i++)
            {
                GameObject childGo = parentTransform.GetChild(i).gameObject;
                Foldout foldout = new() { text = childGo.name, value = false };
                VisualElement innerElement = foldout.Q<VisualElement>(className: "unity-foldout__input");
                innerElement.AddToClassList("GOName");
                innerElement.AddToClassList("FoldoutGO");
                Component[] components = childGo.GetComponents<Component>();
                foldout.contentContainer.Add(RevealChildGOLine(childGo));
                foldout.contentContainer.Add(BaseProperties(childGo));
                foldout.contentContainer.Add(IterateComponents(components, false));
                foldout.style.marginLeft = _marginLeft * depth;
                _availablePropsBlock.Add(foldout);

                IterateChildren(childGo.transform, depth + 1);
            }
        }

        private VisualElement RevealChildGOLine(GameObject go)
        { 
            Transform comp = go.transform;
            VisualElement childGOLine = _childGameObjectTemplate.Instantiate();
            Button button = childGOLine.Q<Button>("AddRemoveButton");
            button.clicked += () => OnClickAddToRevealed(go, comp, "", go.name, RevealType.GameObject);
            button.tooltip = $"Click to reveal child GameObject {go.name}.";
            Label revealTypeLabel = childGOLine.Q<Label>("TypeLabel");
            SetupTypeLabel(revealTypeLabel, go, $"Child {DescribeGameObject(go)}.");
            childGOLine.Q("RevealedName").Hide();
            ObjectField actualPropField = childGOLine.Q<ObjectField>("ActualProp");
            actualPropField.value = comp;
            actualPropField.Q<Label>().AddToClassList("unity-property-field__label");
            
            return childGOLine;
        }

        private void HideAvailableProperties()
        {
            _availablePropsBlock.RemoveFromHierarchy();
        }

        private VisualElement BaseProperties(GameObject childGo)
        {
            Foldout goFoldout = new()
            {
                text = "GameObject properties",
                value = false
            };
            goFoldout.RegisterValueChangedCallback(evt => OnFoldoutOpen(evt, goFoldout, childGo, null));
            goFoldout.style.marginLeft = _marginLeft;
            goFoldout.Add(new VisualElement { name = "Properties" });

            return goFoldout;
        }

        private VisualElement IterateComponents(Component[] components, bool isRoot)
        {
            VisualElement gameObjectBlock = new()
            {
                style = { marginLeft = _marginLeft }
            };
            bool addedAnything = false;
            
            foreach (Component component in components)
            {
                if(component == null
                   || (component.GetType() == typeof(Transform) && isRoot)
                   || component.GetType() == typeof(BlackBox))
                {
                    continue;
                }

                addedAnything = true;
                
                // Properties foldout
                Foldout propertiesFoldout = new()
                {
                    text = component.GetType().Name,
                    value = false
                };
                propertiesFoldout.RegisterValueChangedCallback(evt => OnFoldoutOpen(evt,
                    propertiesFoldout,
                    component.gameObject,
                    component));
                propertiesFoldout.Add(new VisualElement { name = "Properties" });

                gameObjectBlock.Add(propertiesFoldout);
            }
            
            if(!addedAnything)
            {
                Label nopeLabel = new("Nothing available to reveal");
                nopeLabel.AddToClassList("PropertyLine");
                nopeLabel.SetEnabled(false);
                gameObjectBlock.Add(nopeLabel);
            }

            return gameObjectBlock;
        }

        private void OnMethodsFoldoutOpen(ChangeEvent<bool> evt, Foldout methodsFoldout, Component component)
        {
            if (!evt.newValue || methodsFoldout.contentContainer.childCount != 0) return;
            
            Type type = component.GetType();
            Type monoBehaviourType = typeof(MonoBehaviour);
            Type behaviourType = typeof(Behaviour);
                
            MethodInfo[] methodInfos = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            bool addedAnything = false;
            foreach (MethodInfo methodInfo in methodInfos)
            {
                if(methodInfo.GetParameters().Length > 0) continue;
                if (methodInfo.DeclaringType == monoBehaviourType || !methodInfo.DeclaringType!.IsSubclassOf(monoBehaviourType)) continue;
                    
                // Add button
                VisualElement methodLine = BuildMethodLine(methodInfo.Name);
                methodLine.Q<Button>("AddRemoveButton").clicked += () => OnClickAddToRevealed(
                    component.gameObject,
                    component,
                    methodInfo.Name,
                    methodInfo.Name,
                    RevealType.Method
                );
                    
                // Revealed as
                TextField revealedAsField = methodLine.Q<TextField>("RevealedName");
                revealedAsField.Hide();
                
                // Reveal type
                Label revealTypeLabel = methodLine.Q<Label>("TypeLabel");
                SetupTypeLabel(revealTypeLabel, component, $"Method of {DescribeComponent(component)}.");
                    
                methodsFoldout.contentContainer.Add(methodLine);
                addedAnything = true;
            }

            if (addedAnything) return;
            Label nopeLabel = new("No public methods available to reveal");
            nopeLabel.AddToClassList("PropertyLine");
            nopeLabel.SetEnabled(false);
            methodsFoldout.contentContainer.Add(nopeLabel);
        }

        private void OnFoldoutOpen(ChangeEvent<bool> evt, Foldout foldout, GameObject go, Component comp)
        {
            VisualElement propsVisualElement = foldout.contentContainer.Q<VisualElement>("Properties");
            if (evt.newValue && propsVisualElement.childCount == 0)
            {
                SerializedObject targetSerObject = new(comp == null ? go : comp);
                
                // GameObject-only properties
                if (comp == null)
                {
                    // GameObject reference
                    VisualElement objectReferenceLine = _objectReferenceTemplate.Instantiate();
                    Button button = objectReferenceLine.Q<Button>("AddRemoveButton");
                    button.clicked += () => OnClickAddToRevealed(go, null, "", go.name, RevealType.ObjectReference);
                    button.tooltip = $"Click to reveal a reference to gameObject {go.name}, that can be used to copy/paste a reference to it.";
                    Label revealTypeLabel = objectReferenceLine.Q<Label>("TypeLabel");
                    SetupTypeLabel(revealTypeLabel, go, $"Reference to {DescribeGameObject(go)}.");
                    objectReferenceLine.Q<TextField>("RevealedName").Hide();
                    ObjectField actualPropField = objectReferenceLine.Q<ObjectField>("ActualProp");
                    actualPropField.value = go;
                    actualPropField.Q<Label>().AddToClassList("unity-property-field__label");
                    propsVisualElement.Add(objectReferenceLine);
                    
                    // Active flag
                    SerializedProperty isActiveProp = targetSerObject.FindProperty("m_IsActive");
                    ShowProperty(isActiveProp, targetSerObject);
                }
                
                // Component-only properties
                if (comp != null)
                {
                    // Methods foldout
                    Foldout methodsFoldout = new()
                    {
                        text = "Public methods",
                        value = false,
                        style = { marginLeft = _marginLeft }
                    };
                    methodsFoldout.RegisterValueChangedCallback(
                        changeEvent => OnMethodsFoldoutOpen(changeEvent, methodsFoldout, comp));
                    propsVisualElement.Add(methodsFoldout);
                    
                    string compName = comp.GetType().Name;
                    
                    // Component reference
                    VisualElement objectReferenceLine = _objectReferenceTemplate.Instantiate();
                    Button button = objectReferenceLine.Q<Button>("AddRemoveButton");
                    button.clicked += () => OnClickAddToRevealed(go, comp, "", compName, RevealType.ObjectReference);
                    button.tooltip = $"Click to reveal a reference to {compName}, that can be used to copy/paste a reference to it.";
                    Label revealTypeLabel = objectReferenceLine.Q<Label>("TypeLabel");
                    SetupTypeLabel(revealTypeLabel, comp, $"Reference to {DescribeComponent(comp)}.");
                    objectReferenceLine.Q<TextField>("RevealedName").Hide();
                    ObjectField actualPropField = objectReferenceLine.Q<ObjectField>("ActualProp");
                    actualPropField.value = comp;
                    actualPropField.Q<Label>().AddToClassList("unity-property-field__label");
                    propsVisualElement.Add(objectReferenceLine);
                    
                    // Entire component
                    VisualElement entireCompLine = _entireComponent.Instantiate();
                    Label label = new("Entire component's Inspector");
                    label.AddToClassList("EntireComponentLabel");
                    label.AddToClassList("SubtleLabel");
                    entireCompLine.Q<VisualElement>("RevealedComponent").Add(label);
                    button = entireCompLine.Q<Button>("AddRemoveButton");
                    button.clicked += () => OnClickAddToRevealed(go, comp, compName, compName, RevealType.EntireComponent);
                    button.tooltip = $"Click to reveal the entire {compName} component.";
                    revealTypeLabel = entireCompLine.Q<Label>("TypeLabel");
                    SetupTypeLabel(revealTypeLabel, comp, $"Entire {DescribeComponent(comp)}.");
                    propsVisualElement.Add(entireCompLine);
                    
                    // All properties
                    VisualElement allPropsLine = _allPropertiesTemplate.Instantiate();
                    button = allPropsLine.Q<Button>("AddRemoveButton");
                    button.clicked += () => OnClickRevealAllProperties(go, comp);
                    button.tooltip = $"Click to reveal all of {compName}'s properties individually. This is the equivalent of going through all properties below and reveal them one by one.";
                    revealTypeLabel = allPropsLine.Q<Label>("TypeLabel");
                    SetupTypeLabel(revealTypeLabel, comp, $"All properties of {DescribeComponent(comp)}.");
                    propsVisualElement.Add(allPropsLine);
                    
                    // Enabled prop
                    SerializedProperty enabledProp = targetSerObject.FindProperty("m_Enabled");
                    if(enabledProp != null) ShowProperty(enabledProp, targetSerObject); // Some components don't have Enabled
                }
                
                // Iterate the object's properties
                SerializedProperty currentProp = targetSerObject.GetIterator();
                bool next = currentProp.NextVisible(true);
                while (next)
                {
                    if (currentProp.name is "m_Script"
                        or "m_ConstrainProportionsScale")
                    {
                        next = currentProp.NextVisible(true);
                        continue;
                    }

                    ShowProperty(currentProp, targetSerObject);
                    next = currentProp.NextVisible(false);
                }
            }

            return;

            void ShowProperty(SerializedProperty currentProp, SerializedObject targetSerObject)
            {
                // Add and modify the individual property line
                VisualElement line = BuildPropertyLine(currentProp, targetSerObject, currentProp.displayName, false);
                
                // Revealed name
                TextField namePropertyField = line.Q<TextField>("RevealedName");
                namePropertyField.Hide();
                
                // Add button
                Button button = line.Q<Button>();
                string propertyPath = currentProp.propertyPath;
                string displayName = currentProp.displayName;
                RevealType revealType = comp == null ? RevealType.GameObjectProperty : RevealType.ComponentProperty;
                button.clicked += () =>
                {
                    OnClickAddToRevealed(go, comp, propertyPath, displayName, revealType);
                };
                button.text = "+";
                
                // Reveal type
                Label revealTypeLabel = line.Q<Label>("TypeLabel");
                string details = revealType == RevealType.GameObjectProperty
                    ? DescribeGameObject(go)
                    : DescribeComponent(comp);
                SetupTypeLabel(revealTypeLabel,
                    revealType == RevealType.GameObjectProperty ? (Object)go : comp,
                    $"Property of {details}.");

                propsVisualElement.Add(line);
            }
        }

        private void OnClickAddToRevealed(GameObject go, Component comp, string propPath, string propName, RevealType revealedType)
        {
            AddToRevealed(serializedObject, GetActiveListsRevealedItemsArrayProp(), go, comp, propPath, propName, revealedType);
            RefreshRevealedItemsListView();
        }
        
        private void OnClickRevealAllProperties(GameObject go, Component comp)
        {
            SerializedProperty revealList = GetActiveListsRevealedItemsArrayProp();
            SerializedObject componentSerObj = new(comp);
            SerializedProperty property = componentSerObj.GetIterator();
            if (property.NextVisible(true))
            {
                do
                {
                    if (property.name is "m_Script"
                        or "m_ConstrainProportionsScale") continue;

                    if (property.depth == 0)
                    {
                        AddToRevealed(serializedObject, revealList, go, comp,
                            property.propertyPath, property.displayName, RevealType.ComponentProperty);
                    }

                } while (property.NextVisible(false));
            }

            RefreshRevealedItemsListView();
        }

#if HATS
        // Integration with Hats. This should only compile if the Hats package is installed.
        private void DisplayPersonaSelector()
        {
#if HATS_2
            if (!Hats.Editor.Teams.Enabled)
                return;
#endif
            TeamPickerDropdown teamPickerDropdown = new(false)
            {
                label = "Allow Editing From",
                tooltip = "Select the Teams that can edit this Prefab.",
            };
            teamPickerDropdown.AddToClassList("unity-base-field__inspector-field");
            teamPickerDropdown.AddToClassList("unity-base-field__aligned");
            teamPickerDropdown.SetValueWithoutNotify(serializedObject.FindProperty("_teamsAllowedToEdit").intValue);
            teamPickerDropdown.RegisterValueChangedCallback(OnTeamChanged);
            
            _inspector.Add(teamPickerDropdown);
            teamPickerDropdown.PlaceBehind(_inspector.Q<VisualElement>("RevealedPropertiesHeader"));
        }

        private void OnTeamChanged(ChangeEvent<int> evt)
        {
            int newValue = evt.newValue;
            if (newValue == 0)
            {
                newValue = -1;
                ((TeamPickerDropdown)evt.target).SetValueWithoutNotify(newValue);
                Debug.Log("(BlackBox) At least one Team must be allowed to edit the Prefab.");
            }
            
            serializedObject.FindProperty("_teamsAllowedToEdit").intValue = newValue;
            serializedObject.ApplyModifiedProperties();
        }
#endif
        
        #endregion
        
        #region External Inspector

        /// <summary>
        /// Categorises the edited BlackBoxes for multi-selection routing: how many are Prefab assets,
        /// scene instances, or neither (e.g. plain GameObjects), and whether all instances come from the
        /// same Prefab source — the condition under which their revealed items can be multi-edited together.
        /// </summary>
        private void ClassifyMultiSelection(out int assetCount, out int instanceCount, out int otherCount, out bool instancesShareSource)
        {
            assetCount = 0;
            instanceCount = 0;
            otherCount = 0;
            instancesShareSource = true;

            GameObject sharedSource = null;
            foreach (Object t in targets)
            {
                GameObject go = ((BlackBox)t).gameObject;
                if (AssetDatabase.Contains(go))
                {
                    assetCount++;
                }
                else if (PrefabUtility.IsPartOfPrefabInstance(go))
                {
                    instanceCount++;
                    GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(go);
                    if (source == null) instancesShareSource = false;
                    else if (sharedSource == null) sharedSource = source;
                    else if (source != sharedSource) instancesShareSource = false;
                }
                else
                {
                    otherCount++;
                }
            }
        }

        /// <summary>
        /// Builds the explanatory message shown when a multi-selection of BlackBoxes can't be edited
        /// together, tailored to the specific reason rather than a single catch-all.
        /// </summary>
        private static HelpBox BuildMultiSelectionUnsupportedHelpBox(int assetCount, int instanceCount, int otherCount, bool instancesShareSource)
        {
            // A BlackBox sitting on something that is neither a Prefab asset nor a Prefab instance
            if (otherCount > 0)
                return new HelpBox("BlackBox can only be used on Prefabs. One or more of the selected GameObjects is not a Prefab.", HelpBoxMessageType.Warning);

            // Prefab assets (in the Project) selected alongside scene instances
            if (assetCount > 0 && instanceCount > 0)
                return new HelpBox("You've selected a mix of Prefab assets and scene instances. Select either only assets, or only scene instances of the same Prefab.", HelpBoxMessageType.Info);

            // Scene instances that don't all come from the same Prefab
            if (instanceCount > 0 && !instancesShareSource)
                return new HelpBox("The selected objects are instances of different Prefabs. Select instances of the same Prefab to edit their revealed items together.", HelpBoxMessageType.Info);

            return new HelpBox("Working with this combination of BlackBox components is not supported.", HelpBoxMessageType.Info);
        }

        /// <summary>
        /// Builds the External Inspector for several scene instances of the same Prefab. It mirrors the
        /// single-instance external view (status messages, list switcher, revealed items) but binds the
        /// revealed items across every selected instance, so edits apply to all of them at once.
        /// Per-instance controls (temporary unlock, overrides) are omitted as they only act on one object.
        /// </summary>
        private void SetupMultiInstanceInspector()
        {
            _isMultiInstance = true;
            _isExternalEditor = true;

            CacheReferences();
            _targetSerObjects = targets.Select(t => new SerializedObject(t)).ToArray();

            _comp.Evaluate(PrefabStageUtility.GetCurrentPrefabStage());

            // Restore the last selected list for this asset, mirroring the single-selection behaviour
            GameObject prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(_go);
            if (BlackBoxMemory.instance.LastSelectedBlackBoxAsset == prefabAsset)
                _activeListIndex = BlackBoxMemory.instance.CurrentListIndex;
            else
                BlackBoxMemory.instance.CurrentListIndex = 0;
            BlackBoxMemory.instance.LastSelectedBlackBoxAsset = prefabAsset;

            _externalEditor.CloneTree(_inspector);
            _inspector.AddToClassList("external");

            ReactToPlayMode();
            if (!Application.isPlaying)
            {
                DisplayStatusMessages();
                // Show the lock state as a non-interactive icon (temp unlocking acts on a single instance)
                _tempUnlockButton = _inspector.Q<Button>("TempUnlockButton");
                StyleButtonAsIcon(_comp.IsLocked && !BlackBoxSettings.DisableLocking);
                // Overrides act on a single instance, so the button is not applicable here
                _inspector.Q<Button>("OverridesButton")?.RemoveFromHierarchy();
            }
            DisplayDisabledMessage();
            DisplayButtonsExternal();
            DisplayRevealedPropertiesExternal();

            // Same header protection and material-inspector handling as the single-instance external view
            _inspector.RegisterCallback<AttachToPanelEvent>(ParentReady);
#if UNITY_2022_1_OR_NEWER
            if (!_comp.IsAddedAsOverride)
            {
                _inspector.RegisterCallback<AttachToPanelEvent>(ObscureHeader);
                _inspector.RegisterCallback<DetachFromPanelEvent>(RestoreHeader);
            }
#endif
        }

        /// <summary>
        /// Returns the referenced objects for the revealed item at <paramref name="itemIndex"/>. For a single
        /// selection this is the one component/GameObject; for a multi-instance selection it is the matching
        /// object on every selected instance, so the resulting SerializedObject multi-edits them all at once.
        /// </summary>
        private Object[] GetRevealedItemTargets(int itemIndex, bool useComponent)
        {
            string relativeName = useComponent ? nameof(RevealedItem.component) : nameof(RevealedItem.gameObject);

            if (!_isMultiInstance)
            {
                Object single = GetActiveListsRevealedItemsArrayProp()
                    .GetArrayElementAtIndex(itemIndex)
                    .FindPropertyRelative(relativeName)
                    .objectReferenceValue;
                return single == null ? Array.Empty<Object>() : new[] { single };
            }

            List<Object> objects = new();
            foreach (SerializedObject targetSerObj in _targetSerObjects)
            {
                targetSerObj.UpdateIfRequiredOrScript();
                SerializedProperty lists = targetSerObj.FindProperty("_revealedLists");
                if (_activeListIndex >= lists.arraySize) continue;
                SerializedProperty items = lists.GetArrayElementAtIndex(_activeListIndex)
                    .FindPropertyRelative(nameof(RevealedItemsList.revealedItems));
                if (itemIndex >= items.arraySize) continue;
                Object obj = items.GetArrayElementAtIndex(itemIndex).FindPropertyRelative(relativeName).objectReferenceValue;
                if (obj != null) objects.Add(obj);
            }

            return objects.ToArray();
        }

        private void ReactToPlayMode()
        {
            _inspector.Q<VisualElement>("Messages").style.display = Application.isPlaying ? DisplayStyle.None :  DisplayStyle.Flex;

            if (!Application.isPlaying) return;
            
            if(!_comp.HasAnyRevealedItem()) _inspector.Add(new Label("No items revealed"));
            else if(!_comp.HasAnyInspectorRevealedItem()) _inspector.Add(new Label("Some children revealed in the Hierarchy"));
        }

        private void DisplayStatusMessages()
        {
            _lockedLabel = _inspector.Q<Label>("LockedMessage");
            UpdateLockMessage();
            
            Label applyLabel = _inspector.Q<Label>("ApplyDisabledMessage");
            applyLabel.style.display = _comp.IsApplyDisabled ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateLockMessage()
        {
            if(BlackBoxSettings.DisableLocking.value) _lockedLabel.text = "Locking is globally disabled";
            else if(_comp.IsTempUnlocked) _lockedLabel.text = "Temporarily unlocked";
            else if (!_comp.IsLocked) _lockedLabel.text = "Unlocked";
            else
            {
                if(_comp.IsNested && _comp.UnlockIfNested) _lockedLabel.text = "Unlocked due to being nested";
                else if (_comp.IsVariantRoot && _comp.UnlockIfVariantRoot) _lockedLabel.text = "Unlocked due to being a Variant root";
                else _lockedLabel.text = "Locked";
                
            }
        }

        private void SetupTempUnlockedButton()
        {
            _tempUnlockButton = _inspector.Q<Button>("TempUnlockButton");
            bool isTempUnlocked = BlackBoxMemory.instance.UnlockedBlackBox == _comp;

            if (!_comp.IsAsset &&
                !BlackBoxSettings.DisableLocking && BlackBoxSettings.EnableTempUnlocking &&
                !_comp.IsAlreadyUnlocked)
            {
                bool isNestedInTempUnlocked = _comp.IsNested && _comp.NestedInTempUnlockedRoot;
                bool canTempUnlock = !isNestedInTempUnlocked && (!_comp.WillShowContents || isTempUnlocked);

                _tempUnlockButton.clicked += UnlockTemporarily;
                _tempUnlockButton.SetEnabled(canTempUnlock);
                StyleButton(isTempUnlocked);
            }
            else
            {
                StyleButtonAsIcon(_comp.IsLocked && !BlackBoxSettings.DisableLocking);
            }
        }

        private void SetupOverridesButton()
        {
            Button overridesButton = _inspector.Q<Button>("OverridesButton");
            bool inScene = PrefabStageUtility.GetCurrentPrefabStage() == null;
            if((_comp.IsVariantRoot || (_comp.IsNested && !inScene)) && _comp.AllowBlackBoxOverrides) 
                overridesButton.clicked += OnOverridesBtnClicked;
            else overridesButton.RemoveFromHierarchy();
            
            return;

            void OnOverridesBtnClicked()
            {
                BlackBoxMemory.instance.AddingOverrides = true;
                ActiveEditorTracker.sharedTracker.ForceRebuild();
            }
        }

        private void StyleButtonAsIcon(bool lockedIcon)
        {
            _tempUnlockButton.RemoveFromClassList("unity-button");
            _tempUnlockButton.RemoveFromClassList("temp-unlock-button");
            _tempUnlockButton.AddToClassList(lockedIcon ? "temp-unlock-button__locked" : "temp-unlock-button__unlocked");
        }

        private void TryUnlockTemporarilyFromShortcut()
        {
            if (!BlackBoxSettings.EnableTempUnlocking) return;
            if (_comp.IsAsset) return;
            
            if (!_comp.IsAlreadyUnlocked && !BlackBoxSettings.DisableLocking)
            {
                UnlockTemporarily();
            }
            else Debug.Log("(BlackBox) This Prefab cannot be unlocked temporarily.");
        }
        
        private void UnlockTemporarily()
        {
            bool isTempUnlocked = BlackBoxMemory.instance.UnlockedBlackBox == _comp;
            BlackBoxMemory.instance.UnlockedBlackBox = isTempUnlocked ? null : _comp;
                
            StyleButton(!isTempUnlocked);
            UpdateLockMessage();
            
            // Attempt to refresh the Inspector
            Repaint();
            _inspector.parent.parent.parent.MarkDirtyRepaint();
            
            Utilities.AttemptExpandCollapseObjectInHierarchy(_go, true);
        }

        private void StyleButton(bool asTempUnlocked)
        {
            _tempUnlockButton.tooltip = asTempUnlocked ?
                "Click to lock the Prefab again." :
                "Click to temporarily unlock the Prefab.\n\nTemporarily unlocked Prefabs will be locked again when the selection changes to an object that is not part of the Prefab, you change scenes, or enter Prefab Mode.";
            
            _tempUnlockButton.AddToClassList(asTempUnlocked ? "temp-unlock-button__toggled" : "temp-unlock-button");
            _tempUnlockButton.RemoveFromClassList(asTempUnlocked ? "temp-unlock-button" : "temp-unlock-button__toggled");
        }

        private void DisplayRevealedPropertiesExternal()
        {
            _revealedPropertiesBlock = _inspector.Q<VisualElement>("RevealedProperties");

            _brokenRevealedProperties = new List<int>();

            if (_revealedPropertiesBlock.childCount > 0) _revealedPropertiesBlock.RemoveAt(0);
            
            // Figure if the currently selected list is invisible
            bool listAvailable = TryFindVisibleList();

            if (listAvailable
                && GetActiveListsRevealedItemsArrayProp().arraySize != 0)
            {
                _revealedPropertiesBlock.Show();

                VisualElement revealedPropertiesList = new();

                bool groupThisListByGameObject = GetActiveListsGroupByGOProp().boolValue;
                PopulateBrokenRevealedProperties(null);

                Dictionary<GameObject, VisualElement> goElements = groupThisListByGameObject ? new() : null;

                bool showWarningOfBrokenProperties = false;

                for (int i = 0; i < GetActiveListsRevealedItemsArrayProp().arraySize; i++)
                {
                    if (_brokenRevealedProperties.Contains(i))
                    {
                        showWarningOfBrokenProperties = true;
                        continue;
                    }

                    SerializedProperty prop = GetActiveListsRevealedItemsArrayProp().GetArrayElementAtIndex(i);
                    SerializedProperty componentProp = prop.FindPropertyRelative(nameof(RevealedItem.component));
                    SerializedProperty pathProp = prop.FindPropertyRelative(nameof(RevealedItem.path));
                    SerializedProperty gameObjectProp = prop.FindPropertyRelative(nameof(RevealedItem.gameObject));
                    SerializedProperty revealedAsProp = prop.FindPropertyRelative(nameof(RevealedItem.revealedAs));
                    SerializedProperty revealTypeProp = prop.FindPropertyRelative(nameof(RevealedItem.revealType));

                    RevealType revealType = (RevealType)revealTypeProp.enumValueIndex;

                    if (IsAnnotationType(revealType))
                    {
                        revealedPropertiesList.Add(BuildAnnotationDisplayLine(revealType, revealedAsProp.stringValue));
                        continue;
                    }

                    bool useComponentRef = componentProp.objectReferenceValue != null;
                    Object[] revealedItemTargets = GetRevealedItemTargets(i, useComponentRef);
                    if (revealedItemTargets.Length == 0) continue;
                    SerializedObject serObj = new(revealedItemTargets);

                    VisualElement line;
                    switch (revealType)
                    {
                        case RevealType.ComponentProperty or RevealType.GameObjectProperty:
                        {
                            SerializedProperty actualProperty = serObj.FindProperty(pathProp.stringValue);

                            // Add and modify the individual property line
                            line = BuildPropertyLine(actualProperty, serObj, revealedAsProp.stringValue, true);
                        
                            // Type label
                            line.Q<VisualElement>("TypeLabel").Hide();
                    
                            // Revealed name
                            TextField namePropertyField = line.Q<TextField>("RevealedName");
                            namePropertyField.Hide();

                            // Hide button
                            Button addRemoveButton = line.Q<Button>("AddRemoveButton");
                            addRemoveButton.Hide();
                            break;
                        }
                        
                        case RevealType.Method:
                        {
                            line = new VisualElement();
                            Button button = new() { text = revealedAsProp.stringValue };
                            button.AddToClassList("UnityEventButton");
                            string methodPath = pathProp.stringValue;
                            Object[] methodTargets = serObj.targetObjects;
                            button.clicked += () =>
                            {
                                foreach (Object methodTarget in methodTargets)
                                    OnMethodButtonClicked(methodTarget as Component, methodPath);
                            };
                            line.Add(button);
                            break;
                        }

                        case RevealType.EntireComponent:
                        {
                            line = new VisualElement();
                            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(_go);
                            string prefabName = Path.GetFileNameWithoutExtension(path);
                            string compName = componentProp.objectReferenceValue.GetType().Name;
                            Foldout compFoldout = new()
                            {
                                text = revealedAsProp.stringValue,
                                viewDataKey = $"{prefabName}_{compName}"
                            };
                            compFoldout.AddToClassList("entire-component");
                            InspectorElement inspectorElement = _isMultiInstance
                                ? new InspectorElement(serObj)
                                : new InspectorElement(componentProp.objectReferenceValue);
                            compFoldout.contentContainer.Add(inspectorElement);
                            line.Add(compFoldout);
                            
                            break;
                        }

                        case RevealType.ObjectReference:
                        {
                            ObjectField objectField = new()
                            {
                                value = serObj.targetObject,
                                label = revealedAsProp.stringValue
                            };
                            
                            objectField.AddToClassList("object-field_no-picker");
                            objectField.AddToClassList("unity-base-field__aligned");
                            
                            objectField.RegisterValueChangedCallback(evt =>
                                objectField.SetValueWithoutNotify(evt.previousValue));
                            
                            objectField.AddManipulator(new ContextualMenuManipulator(evt =>
                            {
                                evt.menu.AppendAction("Copy", _ =>
                                {
#if UNITY_6000_4_OR_NEWER
                                    string instanceId = objectField.value.GetEntityId().ToString();
#else
                                    string instanceId = objectField.value.GetInstanceID().ToString();
#endif
                                    string referenceString = $"UnityEditor.ObjectWrapperJSON:{{\"guid\":\"\",\"localId\":0,\"type\":0,\"instanceID\":{instanceId}}}";
                                    EditorGUIUtility.systemCopyBuffer = referenceString;
                                });
                            }));

                            line = objectField;
                            break;
                        }

                        case RevealType.GameObject:
                        {
                            line = new VisualElement(); // No visuals needed in the Inspector
                            break;
                        }

                        default:
                        {
                            Debug.LogError($"{Constants.LogPrefix} A revealed item has an unexpected RevealType. Please remove it using the Debug Inspector.");
                            line = new VisualElement();
                            break;
                        }
                    }

                    if (revealType == RevealType.GameObject) continue; // Skip adding visuals

                    if (groupThisListByGameObject)
                        GetOrCreateGOGroup((GameObject)gameObjectProp.objectReferenceValue, revealedPropertiesList, goElements).Add(line);
                    else revealedPropertiesList.Add(line);
                }

                if (showWarningOfBrokenProperties)
                {
                    HelpBox helpBox = new("This BlackBox is referencing one or more property or method belonging to a GameObject or Component that has been deleted." +
                                          "\nTo fix it, enter Prefab mode and delete the revealed item.", HelpBoxMessageType.Warning);
                    helpBox.AddToClassList("HelpBox");
                    revealedPropertiesList.Add(helpBox);
                }

                _revealedPropertiesBlock.Add(revealedPropertiesList);
            }
            else
            {
                _revealedPropertiesBlock.Hide();
            }
            
#if !UNITY_2022_1_OR_NEWER
            VisualElement extraPadding = new VisualElement();
            extraPadding.style.height = 10;
            extraPadding.style.flexGrow = 0;
            _inspector.Add(extraPadding);
#endif
        }

        /// <summary>
        /// Checks if the current list can be shown. If not, it loops through the lists to try and find one that can be shown.
        /// If it finds one, changes the value of <see cref="_activeListIndex"/> to it.
        /// </summary>
        /// <returns>True if a list was found.</returns>
        private bool TryFindVisibleList()
        {
            if (_arrayOfRevealedListsProperty.arraySize == 0) return false;
            if (ComputeListVisibility(GetActiveListProp())) return true;

            // In edit mode the user can intentionally select a hidden list to inspect or edit it;
            // don't silently reassign _activeListIndex out from under their selection.
            if (_isEditingLists) return true;

            // Loop all lists
            for (int i = 0; i < _arrayOfRevealedListsProperty.arraySize; i++)
            {
                if(i == _activeListIndex) continue; // Skip already checked list

                SerializedProperty listProp = _arrayOfRevealedListsProperty.GetArrayElementAtIndex(i);
                if (!ComputeListVisibility(listProp)) continue;

                // Found one!
                _activeListIndex = i;
                return true;
            }

            return false;
        }

        private void OnMethodButtonClicked(Component component, string methodName)
        {
            Type type = component.GetType();
            MethodInfo[] methodInfos = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (MethodInfo methodInfo in methodInfos)
            {
                if (methodInfo.GetParameters().Length > 0) continue;
                if(methodName != methodInfo.Name) continue;
                
                methodInfo!.Invoke(component, Array.Empty<object>());
                break;
            }

        }

        private void DisplayButtonsExternal()
        {
            _listButtonsGroup = _inspector.Q<VisualElement>("ListButtonsGroup");
            _addListBtn = _inspector.Q<Button>("AddListButton"); // Not shown externally, but the ref is needed in SetupListButtons
            // Resolve the visible-list fallback BEFORE styling, otherwise SetupListButtons would stamp
            // ButtonSelected based on a remembered hidden index and no button would end up bold.
            TryFindVisibleList();
            SetupListButtons();
        }

        /// <summary> The parent Inspector of the BlackBox Inspector is ready for tweaks. </summary>
        private void ParentReady(AttachToPanelEvent evt)
        {
            ((VisualElement)evt.target).UnregisterCallback<AttachToPanelEvent>(ParentReady);
            
            // Happens on unselecting the BlackBox
            if (this == null || _go == null || target == null) return;
            
            _inspector?.parent?.parent?.parent?.RegisterCallback<GeometryChangedEvent>(HideMaterialInspectors);
        }

        /// <summary> Tries to hide the Material Inspectors for BlackBoxes that have MeshRenderers on the root. </summary>
        private void HideMaterialInspectors(GeometryChangedEvent evt)
        {
            ((VisualElement)evt.target).UnregisterCallback<GeometryChangedEvent>(HideMaterialInspectors);
            
            VisualElement inspector = _inspector?.parent?.parent?.parent;
            if (inspector == null) return;
            
            try
            {
                List<VisualElement> materialInspectors = inspector.Children().ToList();
                if (materialInspectors!.Count <= 0) return;
                
                foreach (VisualElement element in materialInspectors)
                {
                    if (_comp.WillShowContents ||
                        string.IsNullOrEmpty(element.name) ||
                        !element.name.StartsWith("MaterialEditor_Material_"))
                    {
                        element.Show();
                    }
                    else
                    {
                        element.Hide();
                    }
                }
            }
            catch
            {
                Debug.Log(_go.name);
            }
        }

        /// <summary> Makes the BlackBox Inspector header not clickable, so the user can't remove it. </summary>
        private void ObscureHeader(AttachToPanelEvent evt)
        {
            _componentHeaderOverlay = new Button(OnHeaderClick);
            _componentHeaderOverlay.name = "HeaderConcealer";
            _componentHeaderOverlay.tooltip = "BlackBox component cannot be removed in the scene. Enter Prefab mode to edit it.";
            _componentHeaderOverlay.styleSheets.Add(_styles);
            
            Color col = EditorGUIUtility.isProSkin
                ? new Color(0.24f, 0.24f, 0.24f, .7f)
                : new Color(0.8f, 0.8f, 0.8f, .7f);

            Button helpButton = new (OnHelpClicked);
            helpButton.name = "FakeHelpButton";
            _componentHeaderOverlay.Add(helpButton);

            VisualElement enableButtonFader = new()
            {
                name = "EnableButtonFader",
                style = { backgroundColor = col }
            };
            enableButtonFader.AddToClassList("component-header-fader");
            _componentHeaderOverlay.Add(enableButtonFader);
            
            VisualElement lastButtonsFaders = new()
            {
                name = "LastButtonsFader",
                style = { backgroundColor = col }
            };
            lastButtonsFaders.AddToClassList("component-header-fader");
            _componentHeaderOverlay.Add(lastButtonsFaders);

            _inspector.parent.parent.Add(_componentHeaderOverlay);
            return;

            void OnHeaderClick()
            {
#if UNITY_6000_0_OR_NEWER
                foreach (Object obj in targets)
                    InternalEditorUtility.SetIsInspectorExpanded(obj, !InternalEditorUtility.GetIsInspectorExpanded(obj));
                
                ActiveEditorTracker.sharedTracker.ForceRebuild();
#elif UNITY_2022_1_OR_NEWER
                foreach (GizmoInfo info in GizmoUtility.GetGizmoInfo())
                {
                    if (info.name != nameof(BlackBox)) continue;
                    
                    info.gizmoEnabled = !info.gizmoEnabled;
                    GizmoUtility.ApplyGizmoInfo(info);
                    break;
                }
#endif
            }
            
            void OnHelpClicked()
            {
                Application.OpenURL(Constants.DocumentationUrl);
            }
        }

        private void RestoreHeader(DetachFromPanelEvent evt)
        {
            _componentHeaderOverlay?.parent.Remove(_componentHeaderOverlay);
            _componentHeaderOverlay = null;
        }

        #endregion

        #region Internal and External

        private void DisplayDisabledMessage()
        {
            if (BlackBoxSettings.DisableLocking.value)
            {
                HelpBox helpBox = new("Prefab locking has been disabled globally. " +
                                      "Change it in Project Settings > BlackBox.", HelpBoxMessageType.Info);
                helpBox.AddToClassList("HelpBox");
                _inspector.Insert(0, helpBox);
            }
        }
        
        /// <summary>
        /// Validates all revealed properties and populates _brokenRevealedProperties with the indexes of broken ones.
        /// Called unconditionally before the main display loop, regardless of grouping setting.
        /// </summary>
        private void PopulateBrokenRevealedProperties(VisualElement errorContainer)
        {
            SerializedProperty revealedListProp = GetActiveListsRevealedItemsArrayProp();

            for (int i = 0; i < revealedListProp.arraySize; i++)
            {
                SerializedProperty prop = revealedListProp.GetArrayElementAtIndex(i);
                SerializedProperty gameObjectProp = prop.FindPropertyRelative(nameof(RevealedItem.gameObject));
                SerializedProperty componentProp = prop.FindPropertyRelative(nameof(RevealedItem.component));
                SerializedProperty revealTypeProp = prop.FindPropertyRelative(nameof(RevealedItem.revealType));
                SerializedProperty pathProp = prop.FindPropertyRelative(nameof(RevealedItem.path));
                GameObject targetedGO = (GameObject)gameObjectProp.objectReferenceValue;
                Component targetedComponent = (Component)componentProp.objectReferenceValue;

                int localIndex = i;

                RevealType revealType = (RevealType)revealTypeProp.enumValueIndex;
                switch (revealType)
                {
                    case RevealType.Method when targetedComponent == null:
                    {
                        _brokenRevealedProperties.Add(i);
                        DisplayErrorBox($"Component of method {pathProp} has been deleted.");
                        continue;
                    }

                    case RevealType.ComponentProperty when targetedComponent == null:
                    {
                        _brokenRevealedProperties.Add(i);
                        DisplayErrorBox($"Component has been deleted for property.");
                        continue;
                    }

                    case RevealType.GameObjectProperty when targetedGO == null:
                    {
                        _brokenRevealedProperties.Add(i);
                        DisplayErrorBox($"GameObject has been deleted for property.");
                        continue;
                    }

                    case RevealType.ComponentProperty when targetedComponent != null:
                    {
                        SerializedObject serObj = new(targetedComponent);
                        if (serObj.FindProperty(pathProp.stringValue) == null)
                        {
                            _brokenRevealedProperties.Add(i);
                            DisplayErrorBox($"Property has been removed from Component: {targetedComponent.GetType().Name}.");
                            continue;
                        }
                        break;
                    }

                    case RevealType.Method:
                    {
                        Type type = targetedComponent.GetType();
                        string methodName = pathProp.stringValue;

                        MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
                        if (method == null || method.GetParameters().Length != 0)
                        {
                            _brokenRevealedProperties.Add(i);
                            DisplayErrorBox($"Method not found on Component: {type.Name}.");
                        }

                        break;
                    }

                    case RevealType.EntireComponent when targetedComponent == null || targetedGO == null:
                    {
                        _brokenRevealedProperties.Add(i);
                        DisplayErrorBox("Entire Component is missing (or its GameObject).");
                        continue;
                    }

                    case RevealType.ObjectReference when targetedComponent == null && targetedGO == null:
                    case RevealType.GameObject when targetedGO == null || targetedComponent == null:
                    {
                        _brokenRevealedProperties.Add(i);
                        DisplayErrorBox("Object reference is missing (deleted Component or GameObject).");
                        continue;
                    }

                    // Patch in case a RevealedItem has no GameObject (to account for BlackBox before 1.2.0, where only component properties existed
                    case RevealType.ComponentProperty when targetedGO == null:
                    {
                        targetedGO = ((Component)componentProp.objectReferenceValue).gameObject;
                        if (targetedGO != null)
                        {
                            gameObjectProp.objectReferenceValue = targetedGO;
                            serializedObject.ApplyModifiedProperties();
                        }

                        break;
                    }
                }

                void DisplayErrorBox(string msg)
                {
                    string fullMsg = $"{msg} <b>{pathProp.stringValue}</b>";
                    _brokenRevealedPropertyMessages ??= new Dictionary<int, string>();
                    _brokenRevealedPropertyMessages[localIndex] = fullMsg;

                    if (errorContainer == null) return;
                    VisualElement errorLine = new();
                    HelpBox helpBox = new(fullMsg, HelpBoxMessageType.Warning);
                    helpBox.AddToClassList("HelpBox");
                    errorLine.Add(helpBox);
                    helpBox.Add(new Button(() =>
                    {
                        RemoveRevealedProperty(localIndex);
                        DisplayRevealedPropertiesInternal();
                    }) { text = "Remove" });
                    errorContainer.Add(errorLine);
                }
            }
        }

        /// <summary>
        /// Returns the group container for <paramref name="targetedGO"/>, creating and appending
        /// it to <paramref name="container"/> on first encounter. Called lazily during the rendering
        /// loop so that annotation items (which have no GameObject) can interleave with group containers
        /// in array order rather than being pushed below all groups.
        /// </summary>
        private VisualElement GetOrCreateGOGroup(GameObject targetedGO, VisualElement container,
            Dictionary<GameObject, VisualElement> goElements)
        {
            if (goElements.TryGetValue(targetedGO, out VisualElement existing)) return existing;

            VisualElement group = new();
            goElements[targetedGO] = group;

            string path;
            if (targetedGO == _go)
            {
                path = "This object";
            }
            else
            {
                path = $"{targetedGO.name}";
                Transform currentTransform = targetedGO.transform.parent;
                while (true)
                {
                    string currentObjectName = currentTransform == _go.transform
                        ? "Prefab root"
                        : currentTransform.gameObject.name;
                    path = $"{currentObjectName} > {path}";
                    if (currentTransform == _go.transform) break; // Reached Prefab root

                    currentTransform = currentTransform.parent;
                }
            }

            Button goNameLine = new() { tooltip = path };
            goNameLine.ClearClassList();
            goNameLine.AddToClassList("GOName");
            goNameLine.AddToClassList("RevealedGO");
            Label label = new(targetedGO.name);
            Texture texture = EditorGUIUtility.IconContent((EditorGUIUtility.isProSkin ? "d_" : "") + "GameObject Icon").image;
            VisualElement icon = new Image() { image = texture };
            icon.AddToClassList("go-icon");
            goNameLine.Add(icon);
            goNameLine.Add(label);
            goNameLine.clicked += () => EditorGUIUtility.PingObject(targetedGO);

            group.Add(goNameLine);
            container.Add(group);

            return group;
        }

        private static bool IsAnnotationType(RevealType revealType) =>
            revealType is RevealType.Comment or RevealType.Header or RevealType.Separator;

        private static void SetupTypeLabel(Label typeLabel, Object pingTarget, string description)
        {
            if (typeLabel == null) return;
            typeLabel.tooltip = $"{description} Click to ping in the Hierarchy.";
            typeLabel.AddToClassList("Clickable");
            typeLabel.RegisterCallback<ClickEvent>(_ =>
            {
                Object resolved = ResolvePingTarget(pingTarget);
                if (resolved != null) EditorGUIUtility.PingObject(resolved);
            });
        }

        private static Object ResolvePingTarget(Object target)
        {
            GameObject go = target switch
            {
                GameObject g => g,
                Component c => c != null ? c.gameObject : null,
                _ => null
            };
            if (go == null) return target;
            if ((go.hideFlags & HideFlags.HideInHierarchy) == 0) return target;

            Transform t = go.transform.parent;
            while (t != null)
            {
                if ((t.gameObject.hideFlags & HideFlags.HideInHierarchy) == 0 && t.GetComponent<BlackBox>() != null)
                    return t.gameObject;
                t = t.parent;
            }
            return target;
        }

        private static string DescribeComponent(Component comp) =>
            comp == null ? "component" : $"the {comp.GetType().Name} component on '{comp.gameObject.name}'";

        private static string DescribeGameObject(GameObject go) =>
            go == null ? "GameObject" : $"GameObject '{go.name}'";

        /// <summary>
        /// Builds the editable line for an annotation item in the Internal (prefab-mode) Inspector:
        /// type label and editable text field(s). The ListView footer handles deletion.
        /// </summary>
        private VisualElement BuildAnnotationConfigLine(RevealType revealType, SerializedProperty revealedAsProp)
        {
            VisualElement line = new();
            line.AddToClassList("annotation-edit-line");
            line.AddToClassList("PropertyLine");

            Label typeLabel = new();
            typeLabel.AddToClassList("TypeLabel");

            line.Add(typeLabel);

            switch (revealType)
            {
                case RevealType.Comment:
                {
                    typeLabel.text = "//";
                    typeLabel.tooltip = "A Comment. Shows up as an info box on the instance Inspector.";

                    TextField textField = new() { multiline = true, isDelayed = true };
                    textField.AddToClassList("annotation-edit-comment");
                    textField.BindProperty(revealedAsProp);
                    line.Add(textField);
                    break;
                }
                case RevealType.Header:
                {
                    typeLabel.text = "H";
                    typeLabel.tooltip = "A Header. Shows up as a bold label on the instance Inspector.";

                    TextField textField = new() { isDelayed = true };
                    textField.AddToClassList("annotation-edit-text");
                    textField.BindProperty(revealedAsProp);
                    line.Add(textField);
                    break;
                }
                case RevealType.Separator:
                {
                    typeLabel.text = "–";
                    typeLabel.tooltip = "A Separator. Draws a thin rule on the instance Inspector.";

                    VisualElement separatorLine = new();
                    separatorLine.AddToClassList("annotation-edit-separator");
                    line.Add(separatorLine);
                    break;
                }
            }

            return line;
        }

        /// <summary>
        /// Builds the read-only visual for an annotation item in the External (instance/asset) Inspector.
        /// </summary>
        private static VisualElement BuildAnnotationDisplayLine(RevealType revealType, string revealedAsText)
        {
            switch (revealType)
            {
                case RevealType.Comment:
                {
                    // The HelpBox renders rich-text links (e.g. <a href="...">text</a>) thanks to the
                    // "links-no-underline" class shared with the other HelpBoxes in the inspector.
                    HelpBox helpBox = new(revealedAsText, HelpBoxMessageType.Info);
                    helpBox.AddToClassList("HelpBox");
                    helpBox.AddToClassList("links-no-underline");
                    return helpBox;
                }
                case RevealType.Header:
                {
                    Label label = new(revealedAsText);
                    label.AddToClassList("annotation-header");
                    return label;
                }
                case RevealType.Separator:
                {
                    VisualElement sep = new();
                    sep.AddToClassList("annotation-separator");
                    return sep;
                }
                default:
                    return new VisualElement();
            }
        }

        /// <summary>
        /// Tweaks and binds the PropertyField line.
        /// </summary>
        private VisualElement BuildPropertyLine(SerializedProperty serProp, SerializedObject serObj,
            string propName, bool propertyIsUsable)
        {
            VisualElement propertyLine = _singleProperty.Instantiate();

            PropertyField propertyField = propertyLine.Q<PropertyField>("ActualProp");

            // Make string fields delayed
            if (serProp.propertyType is SerializedPropertyType.String)
            {
                propertyField.RegisterCallback<GeometryChangedEvent>(evt =>
                {
                    PropertyField el = evt.target as PropertyField;
                    TextField textField = el.Q<TextField>();
                    if (textField != null) textField.isDelayed = true;
                });
            }
            
            switch (serProp.propertyPath)
            {
                // Treat layers, tags, Quaternions differently
                case "m_Layer":
                {
                    LayerField layerField = new(propName);
                    layerField.Bind(serObj);
                    layerField.bindingPath = serProp.propertyPath;
                    propertyField.Add(layerField);
                    break;
                }

                case "m_SortingLayerID":
                {
                    SortingLayerField sortingLayerField = new(propName);
                    sortingLayerField.Bind(serObj);
                    sortingLayerField.bindingPath = serProp.propertyPath;
                    propertyField.Add(sortingLayerField);
                    break;
                }

                case "m_TagString":
                {
                    TagField tagField = new(propName);
                    tagField.Bind(serObj);
                    tagField.bindingPath = serProp.propertyPath;
                    propertyField.Add(tagField);
                    break;
                }

                case "m_StaticEditorFlags":
                {
                    EnumFlagsField editorFlagsField = new(propName, (StaticEditorFlags)serProp.enumValueFlag);
                    editorFlagsField.Bind(serObj);
                    editorFlagsField.bindingPath = serProp.propertyPath;
                    editorFlagsField.RegisterValueChangedCallback(evt =>
                        OnLayerFlagsChanged(evt, (GameObject)serObj.targetObject));
                    propertyField.Add(editorFlagsField);
                    break;
                }

                case "m_LocalRotation":
                {
                    IMGUIContainer imguiContainer = new(() => RotationFieldOnGUI(serObj, propName));
                    imguiContainer.AddToClassList("RotationIMGUIContainer");
                    propertyField.Add(imguiContainer);
                    propertyField.style.marginLeft = _marginLeft; // Extra margin for rotation
                    break;
                }

                default:
                    TextField revealAsField = propertyLine.Q<TextField>("RevealedName");

                    if (propertyIsUsable)
                    {
                        bool isButton = serProp.GetAttributes<RevealEventAsButtonAttribute>(false).Length > 0;
                        if (isButton)
                        {
                            // UnityEvent displayed as button
                            Button button = new(() => Utilities.InvokeDelegatesOnUnityEvent(serProp));
                            int calls = serProp.FindPropertyRelative("m_PersistentCalls.m_Calls").arraySize;
                            if(calls == 0)
                            {
                                button.tooltip = "The UnityEvent linked to this button has no callbacks.";
                                button.SetEnabled(false);
                            }
                            button.text = $"{propName}";
                            button.AddToClassList("UnityEventButton");
                            revealAsField.tooltip = "Optional: Change the name visualised on the button used to invoke this UnityEvent.";
                            propertyLine.Add(button);
                            
                            break;
                        }
                    }
                    
#if ODIN_INSPECTOR
                    bool drawWithOdin = serProp.GetAttributes<RevealWithOdinAttribute>(false).Length > 0;

                    if (!drawWithOdin)
                    {
#endif
                        // Regular property
                        propertyField.label = propName;

#if !UNITY_2022_1_OR_NEWER
                    // Catching issues with 2021.3 and Enum serialized properties,
                    // by drawing an IMGUI Inspector
                    if (serProp.propertyType != SerializedPropertyType.Enum &&
                        !serProp.hasChildren)
                    {
#endif
                            propertyField.BindProperty(serProp);
                            
#if !UNITY_2022_1_OR_NEWER
                    }
                    else
                    {
                        IMGUIContainer imguiProp = new IMGUIContainer();

                        string propertyPath = serProp.propertyPath;
                        imguiProp.onGUIHandler += () => OnIMGUIPropOnGUIHandler(serObj.targetObject, propertyPath, propertyIsUsable, propName);
                        imguiProp.style.marginLeft = 3;
                        propertyField.Add(imguiProp);
                    }
#endif
                    
#if ODIN_INSPECTOR
                    }
                    else
                    {
                        // Odin-drawn property
                        PropertyTree newTree = PropertyTree.Create(serProp.serializedObject.targetObject);
                        _trees.Add(newTree);
                        InspectorProperty inspectorProperty = newTree.RootProperty.FindChild(property => property.Name == serProp.name, true);
                        _inspectorProperties.Add(inspectorProperty);
                        IMGUIContainer imgui = new IMGUIContainer();
                        imgui.userData = _trees.Count - 1;
                        imgui.onGUIHandler = () =>
                        {
                            int treeIndex = (int)imgui.userData;
                            PropertyTree tree = _trees[treeIndex];
                            tree.BeginDraw(true);
                            InspectorProperty inspectorProp = _inspectorProperties[treeIndex];
                            inspectorProp.Draw();
                            tree.UpdateTree();
                            tree.EndDraw();
                        };
                        propertyField.Add(imgui);
                        
                        // Disable renaming
                        revealAsField.SetEnabled(false);
                        revealAsField.tooltip = "Properties revealed with Odin cannot be renamed.";
                    }
#endif
                    break;
            }
            
            propertyField.SetEnabled(propertyIsUsable);

            return propertyLine;
        }
 
#if !UNITY_2022_1_OR_NEWER
        private void OnIMGUIPropOnGUIHandler(Object targetObject, string serPropPropertyPath, bool propertyIsUsable, string propName)
        {
            SerializedObject o = new(targetObject);
            SerializedProperty property = o.FindProperty(serPropPropertyPath);
            if (!propertyIsUsable) GUI.enabled = false;
            EditorGUILayout.PropertyField(property, new GUIContent(propName), true);
            if (GUI.changed) o.ApplyModifiedProperties();
            GUI.enabled = true;
        }
#endif

        private VisualElement BuildMethodLine(string methodName)
        {
            VisualElement methodLine = _singleMethod.Instantiate();
            Label methodNameLabel = methodLine.Q<Label>("MethodName");
            methodNameLabel.text = $"{methodName}()";
            
            return methodLine;
        }

        private void OnLayerFlagsChanged(ChangeEvent<Enum> evt, GameObject targetObject)
        {
            GameObjectUtility.SetStaticEditorFlags(targetObject, (StaticEditorFlags)evt.newValue);
        }

        private void RotationFieldOnGUI(SerializedObject serObj, string label)
        {
            SerializedProperty serProp = serObj.FindProperty("m_LocalRotation");
            serObj.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginVertical(new GUIStyle() { padding = new RectOffset(18, 0, 0, 0) });
            TransformRotationGUI rotGUI = new();
            rotGUI.Initialize(serProp, new GUIContent(label));
            rotGUI.Draw();
            EditorGUILayout.EndVertical();
            if (EditorGUI.EndChangeCheck())
            {
                serProp.serializedObject.ApplyModifiedProperties();
            }
        }
        
        private void RemoveRevealedProperty(int indexInArray)
        {
            serializedObject.Update();
            GetActiveListsRevealedItemsArrayProp().DeleteArrayElementAtIndex(indexInArray);
            serializedObject.ApplyModifiedProperties();
            Undo.SetCurrentGroupName("BlackBox Remove Revealed Item");
        }

        #endregion

        /// <summary>
        /// Adds either a property or a method to the list of revealed items.
        /// Can be invoked internally, or by the right click -> Reveal on BlackBox context menu.
        /// </summary>
        public static void AddToRevealed(SerializedObject serObj, SerializedProperty revealListProp,
            GameObject targetGo, Component targetComponent,
            string propPath,
            string propName,
            RevealType revealedType)
        {
            
            int newIndex = revealListProp.arraySize;
            revealListProp.InsertArrayElementAtIndex(newIndex);
            SerializedProperty newElementProperty = revealListProp.GetArrayElementAtIndex(newIndex);
            newElementProperty.FindPropertyRelative(nameof(RevealedItem.component)).objectReferenceValue = targetComponent;
            newElementProperty.FindPropertyRelative(nameof(RevealedItem.gameObject)).objectReferenceValue = targetGo;
            newElementProperty.FindPropertyRelative(nameof(RevealedItem.path)).stringValue = propPath;
            newElementProperty.FindPropertyRelative(nameof(RevealedItem.revealedAs)).stringValue = propName;
            newElementProperty.FindPropertyRelative(nameof(RevealedItem.revealType)).enumValueIndex = (int)revealedType;

            serObj.ApplyModifiedProperties();
            Undo.SetCurrentGroupName("BlackBox Reveal Item");
        }

        public void RefreshRevealedProperties()
        {
            if (_inspector == null) return;
            
            if(_isExternalEditor) ActiveEditorTracker.sharedTracker.ForceRebuild();
            else RefreshRevealedItemsListView();
        }
        
        /// <summary> Gets the Serialized property of the active list. </summary>
        private SerializedProperty GetActiveListProp() => _arrayOfRevealedListsProperty.GetArrayElementAtIndex(_activeListIndex);
        
        /// <summary> Gets the Serialized property of the array of revealed items contained in the active list. </summary>
        private SerializedProperty GetActiveListsRevealedItemsArrayProp() => GetActiveListProp().FindPropertyRelative(nameof(RevealedItemsList.revealedItems));
        /// <summary> Gets the Serialized property of the name of the active list. </summary>
        private SerializedProperty GetActiveListsNameProp() => GetActiveListProp().FindPropertyRelative(nameof(RevealedItemsList.listName));
        /// <summary> Gets the Serialized property of the visibility of the active list. </summary>
        private SerializedProperty GetActiveListsVisibilityProp() => GetActiveListProp().FindPropertyRelative(nameof(RevealedItemsList.visibility));
        /// <summary> Gets the Serialized property of the ordering preference of the active list. </summary>
        private SerializedProperty GetActiveListsGroupByGOProp() => GetActiveListProp().FindPropertyRelative(nameof(RevealedItemsList.groupByGameObjects));

        [Shortcut("BlackBox/Unlock Temporarily", KeyCode.U)]
        private static void UnlockTemporarilyShortcutListener()
        {
            if (BlackBoxMemory.instance.UnlockedBlackBox == null)
            {
                if (CurrentEditor != null)
                {
                    // Temp unlock currently selected BlackBox
                    CurrentEditor.TryUnlockTemporarilyFromShortcut();
                }
            }
            else
            {
                // Re-lock whatever BB was temporarily unlocked
                Selection.activeGameObject = BlackBoxMemory.instance.UnlockedBlackBox.gameObject;

                if (CurrentEditor != null) CurrentEditor.UnlockTemporarily(); // Also updates the button
                else BlackBoxMemory.instance.ClearTempUnlock();
            }
        }
    }
}