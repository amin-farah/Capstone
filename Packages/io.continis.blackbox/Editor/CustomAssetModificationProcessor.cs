using UnityEditor;
using UnityEngine;

namespace BlackBox.Editor
{
    /// <summary>
    /// An AssetModificationProcessor that checks for newly-created Prefabs,
    /// and auto-adds the BlackBox component if specified in Project Settings.
    /// </summary>
    public class CustomAssetModificationProcessor : AssetModificationProcessor
    {
        static void OnWillCreateAsset(string assetName)
        {
            if (!BlackBoxSettings.AutoAddToPrefabs.value &&
                !BlackBoxSettings.AutoAddToVariants.value)
                return;
            
            if (!assetName.EndsWith(".prefab"))
                return;

            // A duplicate must mirror its source: Unity routes both new prefabs and
            // duplicates through OnWillCreateAsset, so skip auto-add for duplicates.
            // Detection must run here (not in DelayCall) while the source is still selected.
            if (IsDuplicateOfExistingPrefab(assetName))
                return;

            EditorApplication.delayCall += () => DelayCall(assetName);
        }

        private static bool IsDuplicateOfExistingPrefab(string newAssetPath)
        {
            foreach (var obj in Selection.objects)
            {
                if (obj is not GameObject go || !EditorUtility.IsPersistent(go))
                    continue;

                string sourcePath = AssetDatabase.GetAssetPath(go);
                if (!sourcePath.EndsWith(".prefab"))
                    continue;

                // Unity derives a duplicate's name via GenerateUniqueAssetPath. If the new
                // asset path is exactly what duplicating this selected prefab would produce,
                // this OnWillCreateAsset call is that duplication.
                if (AssetDatabase.GenerateUniqueAssetPath(sourcePath) == newAssetPath)
                    return true;
            }

            return false;
        }

        private static void DelayCall(string assetName)
        {
            GameObject prefabRoot = AssetDatabase.LoadMainAssetAtPath(assetName) as GameObject;
            PrefabAssetType prefabAssetType = PrefabUtility.GetPrefabAssetType(prefabRoot);

            switch (prefabAssetType)
            {
                case PrefabAssetType.Variant:
                {
                    if (!BlackBoxSettings.AutoAddToVariants.value) return;
                    break;
                }
                case PrefabAssetType.Regular:
                {
                    if (!BlackBoxSettings.AutoAddToPrefabs.value) return;
                    break;
                }
            }

            if(!prefabRoot!.TryGetComponent(out BlackBox _))
            {
                BlackBox addComponent = prefabRoot.AddComponent<BlackBox>();
                while (UnityEditorInternal.ComponentUtility.MoveComponentUp(addComponent))
                {
                    
                }
                PrefabUtility.SavePrefabAsset(prefabRoot);
            }
        }
    }
}