using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace BlackBox.Editor
{
    public static class Utilities
    {
        private const BindingFlags AllBindingFlags = (BindingFlags)(-1);

        /// <summary>
        /// Returns attributes of type <typeparamref name="TAttribute"/> on <paramref name="serializedProperty"/>.
        /// </summary>
        public static TAttribute[] GetAttributes<TAttribute>(this SerializedProperty serializedProperty, bool inherit)
            where TAttribute : Attribute
        {
            if (serializedProperty == null)
            {
                throw new ArgumentNullException(nameof(serializedProperty));
            }

            Type targetObjectType = serializedProperty.serializedObject.targetObject.GetType();

            if (targetObjectType == null)
            {
                throw new ArgumentException($"Could not find the {nameof(targetObjectType)} of {nameof(serializedProperty)}");
            }

            foreach (string pathSegment in serializedProperty.propertyPath.Split('.'))
            {
                FieldInfo fieldInfo = targetObjectType.GetField(pathSegment, AllBindingFlags);
                if (fieldInfo != null)
                {
                    return (TAttribute[])fieldInfo.GetCustomAttributes<TAttribute>(inherit);
                }

                PropertyInfo propertyInfo = targetObjectType.GetProperty(pathSegment, AllBindingFlags);
                if (propertyInfo != null)
                {
                    return (TAttribute[])propertyInfo.GetCustomAttributes<TAttribute>(inherit);
                }
            }

            return Array.Empty<TAttribute>();
        }
        
        public static bool CreateSelectionMesh(GameObject gameObject, SelectionType scriptSelectionType, out Mesh result)
        {
            result = new Mesh();
            List<CombineInstance> combine = new();
            Bounds combinedBounds = new();

            if (scriptSelectionType is SelectionType.UseBoundingBoxes or SelectionType.Use3DMeshes)
            {
                MeshFilter[] meshFilters = gameObject.GetComponentsInChildren<MeshFilter>();
                if (meshFilters.Length == 0) return false;

                foreach (var meshFilter in meshFilters)
                {
                    Mesh mesh = meshFilter.sharedMesh;
                    if (mesh == null) continue;

                    Matrix4x4 transformMatrix = gameObject.transform.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix;

                    if (scriptSelectionType == SelectionType.Use3DMeshes)
                    {
                        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
                        {
                            CombineInstance combineInstance = new()
                            {
                                mesh = mesh,
                                subMeshIndex = subMeshIndex,
                                transform = transformMatrix
                            };
                            combine.Add(combineInstance);
                        }
                    }
                    else
                    {
                        Mesh boundingBoxMesh = ConvertBoundsToMesh(mesh.bounds);
                        CombineInstance combineInstance = new()
                        {
                            mesh = boundingBoxMesh,
                            transform = transformMatrix
                        };
                        combine.Add(combineInstance);
                    }

                    // Update the combined bounds
                    combinedBounds.Encapsulate(GetTransformedBounds(mesh.bounds, transformMatrix));
                }
            }
            else if (scriptSelectionType == SelectionType.UseSkinnedMeshRenderers)
            {
                SkinnedMeshRenderer[] skinnedMeshRenderers = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
                if (skinnedMeshRenderers.Length == 0) return false;

                foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
                {
                    Mesh mesh = new();
                    skinnedMeshRenderer.BakeMesh(mesh, true);
                    if (mesh == null) continue;

                    Matrix4x4 transformMatrix = gameObject.transform.worldToLocalMatrix * skinnedMeshRenderer.transform.localToWorldMatrix;

                    for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
                    {
                        CombineInstance combineInstance = new()
                        {
                            mesh = mesh,
                            subMeshIndex = subMeshIndex,
                            transform = transformMatrix
                        };
                        combine.Add(combineInstance);
                    }

                    // Update the combined bounds
                    combinedBounds.Encapsulate(GetTransformedBounds(mesh.bounds, transformMatrix));
                }
            }
            else if (scriptSelectionType == SelectionType.UseSpriteRenderers)
            {
                SpriteRenderer[] spriteRenderers = gameObject.GetComponentsInChildren<SpriteRenderer>();

                foreach (SpriteRenderer sr in spriteRenderers)
                {
                    if (sr.sprite == null) continue;
    
                    Sprite sprite = sr.sprite;
    
                    Mesh spriteMesh = new();
                    List<Vector3> vertices = new();
                    List<int> triangles = new();
                    List<Vector2> uvs = new();
                    List<Vector3> normals = new();

                    Vector2[] spriteVerts = sprite.vertices;
                    foreach (Vector2 v in spriteVerts)
                    {
                        int sortingLayerDepth = SortingLayer.GetLayerValueFromID(SortingLayer.NameToID(sr.sortingLayerName));
                        Vector3 vertex3d = v;
                        vertex3d.z = -sortingLayerDepth * .1f -sr.sortingOrder * .01f; // Add Order In Layer and Sorting Layer's depth
                        vertices.Add(vertex3d);
                        normals.Add(Vector3.back);
                    }
    
                    ushort[] spriteTris = sprite.triangles;
                    foreach (ushort t in spriteTris)
                    {
                        triangles.Add(t);
                    }
    
                    uvs.AddRange(sprite.uv);
    
                    spriteMesh.SetVertices(vertices);
                    spriteMesh.SetTriangles(triangles, 0);
                    spriteMesh.SetUVs(0, uvs);
                    spriteMesh.SetNormals(normals);
                    
                    Matrix4x4 transformMatrix = gameObject.transform.worldToLocalMatrix * sr.transform.localToWorldMatrix;
    
                    CombineInstance combineInstance = new()
                    {
                        mesh = spriteMesh,
                        subMeshIndex = 0,
                        transform = transformMatrix
                    };
                    combine.Add(combineInstance);
                }
            }
            else
            {
                throw new NotImplementedException("Selection type shouldn't be this type.");
            }

            if (combine.Count > 0)
            {
                try
                {
                    result.CombineMeshes(combine.ToArray(), true, true);
                    result.bounds = combinedBounds; // Set the combined bounds to the resulting mesh
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            return false;
        }

        private static Bounds GetTransformedBounds(Bounds bounds, Matrix4x4 transformMatrix)
        {
            Vector3 center = transformMatrix.MultiplyPoint(bounds.center);
            Vector3 extents = bounds.extents;
            Vector3 axisX = transformMatrix.MultiplyVector(Vector3.right) * extents.x;
            Vector3 axisY = transformMatrix.MultiplyVector(Vector3.up) * extents.y;
            Vector3 axisZ = transformMatrix.MultiplyVector(Vector3.forward) * extents.z;
            extents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z)
            );

            return new Bounds(center, extents * 2);
        }


        public static Mesh ConvertBoundsToMesh(Bounds bounds)
        {
            Vector3[] vertices = new Vector3[8];
            vertices[0] = bounds.center + new Vector3(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            vertices[1] = bounds.center + new Vector3(bounds.extents.x, bounds.extents.y, -bounds.extents.z);
            vertices[2] = bounds.center + new Vector3(bounds.extents.x, -bounds.extents.y, bounds.extents.z);
            vertices[3] = bounds.center + new Vector3(bounds.extents.x, -bounds.extents.y, -bounds.extents.z);
            vertices[4] = bounds.center + new Vector3(-bounds.extents.x, bounds.extents.y, bounds.extents.z);
            vertices[5] = bounds.center + new Vector3(-bounds.extents.x, bounds.extents.y, -bounds.extents.z);
            vertices[6] = bounds.center + new Vector3(-bounds.extents.x, -bounds.extents.y, bounds.extents.z);
            vertices[7] = bounds.center + new Vector3(-bounds.extents.x, -bounds.extents.y, -bounds.extents.z);

            int[] triangles = new int[]
            {
                0, 2, 1, // Front
                1, 2, 3,
                4, 5, 6, // Back
                5, 7, 6,
                0, 1, 4, // Top
                1, 5, 4,
                2, 6, 3, // Bottom
                3, 6, 7,
                0, 4, 2, // Left
                2, 4, 6,
                1, 3, 5, // Right
                3, 7, 5
            };

            Vector3[] normals = new Vector3[vertices.Length];
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int index1 = triangles[i];
                int index2 = triangles[i + 1];
                int index3 = triangles[i + 2];
                Vector3 side1 = vertices[index2] - vertices[index1];
                Vector3 side2 = vertices[index3] - vertices[index1];
                Vector3 normal = Vector3.Cross(side1, side2).normalized;

                normals[index1] += normal;
                normals[index2] += normal;
                normals[index3] += normal;
            }

            for (int i = 0; i < normals.Length; i++) normals[i] = normals[i].normalized;

            Mesh mesh = new();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.normals = normals;

            return mesh;
        }
        
        public static void InvokeDelegatesOnUnityEvent(SerializedProperty serProp)
        {
            SerializedProperty callsArray = serProp.FindPropertyRelative("m_PersistentCalls.m_Calls");
            for (int i = 0; i < callsArray.arraySize; i++)
            {
                SerializedProperty callProp = callsArray.GetArrayElementAtIndex(i);
                InvokeDelegate(callProp);
            }
        }

        public static void InvokeDelegate(SerializedProperty callProp)
        {
            Object targetObject = callProp.FindPropertyRelative("m_Target").objectReferenceValue;
            string targetAssemblyType = callProp.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue;
            string targetMethodName = callProp.FindPropertyRelative("m_MethodName").stringValue;
            PersistentListenerMode mode = (PersistentListenerMode)callProp.FindPropertyRelative("m_Mode").enumValueIndex;
            object[] args;
            Type type;
            switch (mode)
            {
                default:
                case PersistentListenerMode.EventDefined:
                case PersistentListenerMode.Void:
                    args = Array.Empty<object>();
                    type = typeof(Action);
                    break;
                case PersistentListenerMode.Object:
                    args = new object[] { callProp.FindPropertyRelative("m_Arguments.m_ObjectArgument").objectReferenceValue };
                    type = typeof(Action<Object>);
                    break;
                case PersistentListenerMode.Int:
                    args = new object[] { callProp.FindPropertyRelative("m_Arguments.m_IntArgument").intValue };
                    type = typeof(Action<int>);
                    break;
                case PersistentListenerMode.Float:
                    args = new object[] { callProp.FindPropertyRelative("m_Arguments.m_FloatArgument").floatValue };
                    type = typeof(Action<float>);
                    break;
                case PersistentListenerMode.String:
                    args = new object[] { callProp.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue };
                    type = typeof(Action<string>);
                    break;
                case PersistentListenerMode.Bool:
                    args = new object[] { callProp.FindPropertyRelative("m_Arguments.m_BoolArgument").boolValue };
                    type = typeof(Action<bool>);
                    break;
            }

            Type targetType = Type.GetType(targetAssemblyType);
            MethodInfo methodInfo = targetType?.GetMethod(targetMethodName, BindingFlags.Instance | BindingFlags.Public);
            Delegate del = Delegate.CreateDelegate(type, targetObject, methodInfo!);
            
            del.DynamicInvoke(args);
        }
        
        public static void AttemptExpandCollapseObjectInHierarchy(GameObject gameObject, bool expanded)
        {
            try
            {
                Type hierarchyType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
                EditorWindow hierarchyWindow = EditorWindow.GetWindow(hierarchyType);
                MethodInfo setExpandedMethod = hierarchyWindow?.GetType().GetMethod(
                    "SetExpanded",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
#if UNITY_6000_4_OR_NEWER
                setExpandedMethod?.Invoke(hierarchyWindow, new object[] { gameObject.GetEntityId(), expanded });
#else
                setExpandedMethod?.Invoke(hierarchyWindow, new object[] { gameObject.GetInstanceID(), expanded });
#endif
            }
            catch { }
        }
    }
}