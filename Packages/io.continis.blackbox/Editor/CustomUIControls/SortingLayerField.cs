using System;
using System.Reflection;
using UnityEngine.UIElements;

namespace BlackBox.Editor.CustomUIControls
{
#if UNITY_2023_1_OR_NEWER
    [UxmlElement("SortingLayerField")]
    public partial class SortingLayerField : BaseField<int>
    {
#else
    public class SortingLayerField : BaseField<int>
    {
        public new class UxmlFactory : UxmlFactory<SortingLayerField> { }
#endif
        private PopupField<string> _mPopupField;
        private string[] _mSortingLayerNames;
        private int[] _mSortingLayerIDs;

        private static readonly string USSClassName = "unity-sorting-layer-field";
        private static readonly string LabelUssClassName = USSClassName + "__label";
        private static readonly string InputUssClassName = USSClassName + "__input";

        public SortingLayerField() : this(null) { }

        public SortingLayerField(string label) : base("Sorting Layer", null)
        {
            AddToClassList(USSClassName);
            AddToClassList("unity-base-field__aligned");
            labelElement.AddToClassList(LabelUssClassName);

            RefreshSortingLayers();
            CreatePopupField();
        }

        private void RefreshSortingLayers()
        {
            _mSortingLayerNames = GetSortingLayerNames();
            _mSortingLayerIDs = GetSortingLayerIDs();
        }

        private void CreatePopupField()
        {
            if (_mPopupField != null)
            {
                _mPopupField.RemoveFromHierarchy();
            }

            int currentIndex = GetIndexFromID(value);
            if (currentIndex < 0) currentIndex = 0;

            _mPopupField = new PopupField<string>(
                null,
                new System.Collections.Generic.List<string>(_mSortingLayerNames),
                currentIndex
            );

            _mPopupField.AddToClassList(InputUssClassName);
            _mPopupField.style.marginLeft = _mPopupField.style.marginRight = 0;
            _mPopupField.RegisterValueChangedCallback(OnPopupValueChanged);
            
            this.Q<VisualElement>(className:"unity-base-field__input").Add(_mPopupField);
        }

        private void OnPopupValueChanged(ChangeEvent<string> evt)
        {
            int newIndex = Array.IndexOf(_mSortingLayerNames, evt.newValue);
            if (newIndex >= 0 && newIndex < _mSortingLayerIDs.Length)
            {
                int newID = _mSortingLayerIDs[newIndex];
                
                if (value != newID)
                {
                    value = newID;
                }
            }
        }

        private int GetIndexFromID(int sortingLayerID)
        {
            return Array.IndexOf(_mSortingLayerIDs, sortingLayerID);
        }

        public override void SetValueWithoutNotify(int newValue)
        {
            base.SetValueWithoutNotify(newValue);
            UpdatePopupSelection(newValue);
        }

        private void UpdatePopupSelection(int sortingLayerID)
        {
            if (_mPopupField == null) return;

            int index = GetIndexFromID(sortingLayerID);
            if (index >= 0 && index < _mSortingLayerNames.Length)
            {
                _mPopupField.SetValueWithoutNotify(_mSortingLayerNames[index]);
            }
        }

        private static string[] GetSortingLayerNames()
        {
            Type internalEditorUtilityType = typeof(UnityEditorInternal.InternalEditorUtility);
            PropertyInfo sortingLayersProperty = internalEditorUtilityType.GetProperty(
                "sortingLayerNames",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            return (string[])sortingLayersProperty?.GetValue(null, new object[0]) ?? new string[0];
        }

        private static int[] GetSortingLayerIDs()
        {
            Type internalEditorUtilityType = typeof(UnityEditorInternal.InternalEditorUtility);
            PropertyInfo sortingLayerUniqueIDsProperty = internalEditorUtilityType.GetProperty(
                "sortingLayerUniqueIDs",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            return (int[])sortingLayerUniqueIDsProperty?.GetValue(null, new object[0]) ?? new int[0];
        }
    }
}