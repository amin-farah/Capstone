using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlackBox.Editor
{
    public class WelcomeScreen : EditorWindow
    {
        [SerializeField] private VisualTreeAsset _visualTreeAsset;
        private static WelcomeScreen _window;

        [MenuItem(Constants.MenuItemBaseName + "Welcome screen", false, 0)]
        public static void Init()
        {
            _window = GetWindow<WelcomeScreen>(true);
            _window.titleContent = new GUIContent("BlackBox");
            _window.minSize = new Vector2(300f, 420f);
            _window.maxSize = new Vector2(300f, 420f);
        }

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            VisualElement templateWindow = _visualTreeAsset.Instantiate();
            templateWindow.style.flexGrow = 1f;
            
            templateWindow.Q<Button>("DocsBtn").clicked += () => Application.OpenURL(Constants.DocumentationUrl);
            templateWindow.Q<Button>("EmailBtn").clicked += () => Application.OpenURL(Constants.SupportEmail);
            templateWindow.Q<Button>("DiscordBtn").clicked += () => Application.OpenURL(Constants.DiscordUrl);
            templateWindow.Q<Button>("OtherToolsBtn").clicked += () => Application.OpenURL(Constants.OtherToolsUrl);
            templateWindow.Q<Button>("ReviewBtn").clicked += () => Application.OpenURL(Constants.ReviewUrl);
            
            root.Add(templateWindow);
        }
    }
}