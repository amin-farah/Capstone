using UnityEngine.UIElements;

namespace BlackBox.Editor
{
    public static class ExtensionMethods
    {
        public static void ShowOrHide(this VisualElement visualElement, bool show)
        {
            visualElement.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public static void Show(this VisualElement visualElement)
        {
            visualElement.style.display = DisplayStyle.Flex;
        }
        
        public static void Hide(this VisualElement visualElement)
        {
            visualElement.style.display = DisplayStyle.None;
        }
    }
}