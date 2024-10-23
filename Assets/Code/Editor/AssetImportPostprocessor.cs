using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Code.Editor
{
    public class AssetImportPostprocessor : AssetPostprocessor
    {
        #region Importer
        private void OnPreprocessModel()
        {
            ModelImporter modelImporter = assetImporter as ModelImporter;
        
            modelImporter.importBlendShapes = false;
            modelImporter.importCameras = false;
            modelImporter.importVisibility = false;
            modelImporter.importLights = false;
            
            modelImporter.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            modelImporter.materialLocation = ModelImporterMaterialLocation.External;
            modelImporter.materialName = ModelImporterMaterialName.BasedOnMaterialName;
            modelImporter.materialSearch = ModelImporterMaterialSearch.Everywhere;

            modelImporter.importTangents = ModelImporterTangents.CalculateMikk;
            modelImporter.isReadable = false;
            modelImporter.animationType = ModelImporterAnimationType.None;
            modelImporter.importAnimation = false;
        }
        
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            Debug.Log("Log");
            
            foreach (var assetPath in importedAssets)
            {
                string ext = String.Empty;
                ext = Path.GetExtension(assetPath);
            
                switch (ext.ToLower())
                {
                    case ".fbx":
                        // CreateSimplePrefab(assetPath);
                        ConvertMesh(assetPath);
                        break;
                }
            }
        }
        
        #endregion

        #region Methods

        public static void CreateSimplePrefab(string sourceAssetPath)
        {
            var sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(sourceAssetPath);
           
            // get paths
            var modelPath = sourceAssetPath.Substring(0, sourceAssetPath.LastIndexOf("/"));
            var assetsRootPath = modelPath.Substring(0, modelPath.LastIndexOf("/"));
            
            // simulate path
            var pathVisualPrefabsFolder = assetsRootPath + "/Prefabs/VisualPrefabs";
            var pathPrefabsFolder = assetsRootPath + "/Prefabs";
            
            // VISUAL PREFAB
            // recursively create folders
            Directory.CreateDirectory(pathVisualPrefabsFolder);
            
            var pathToSaveVisualPrefab = pathVisualPrefabsFolder + "/" + sourceModel.name + "_!VisualPrefab.prefab";
            
            sourceModel.name += "_!Visual";
            sourceModel.isStatic = true;
            
            PrefabUtility.SaveAsPrefabAsset(sourceModel, pathToSaveVisualPrefab);
        }
        
        public static void ConvertMesh(string sourceAssetPath)
        {
            var sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(sourceAssetPath);
             
            var sourceMeshFilters = sourceModel.GetComponentsInChildren<MeshFilter>();
            
            // PRODUCE CONTAINER
            string containerPath = GetFullPathWithoutExtension(sourceAssetPath) + ".asset";
            FbxContainer container = AssetDatabase.LoadAssetAtPath<FbxContainer>(containerPath);
            
            // if there is no container, create one and fill it
            if (!container)
            {
                ScriptableObject containerObj = ScriptableObject.CreateInstance<FbxContainer>();
                AssetDatabase.CreateAsset(containerObj, containerPath);

                foreach (var smf in sourceMeshFilters)
                {
                    var sourceSubAsset = smf.sharedMesh;
                    var artifactSubAsset = AttachSubAsset(sourceSubAsset, containerPath);
                    // assign a mesh to the source model meshFilter so that when the model is saved to the prefab, the link will already be thrown through
                    smf.sharedMesh = artifactSubAsset;
                }
            }
            // if the container exists
            else
            {
                var existingContainerSubAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(containerPath).OfType<Mesh>().ToList();
                
                foreach (var smf in sourceMeshFilters)
                {
                    var sourceSubAsset = smf.sharedMesh;
                    // check if a particular subAsset needs to be updated
                    var artifactSubAsset = UpdateSubAsset(existingContainerSubAssets, sourceSubAsset, containerPath);
                    smf.sharedMesh = artifactSubAsset;
                }
                
                // destroy because the Artist removed them in the original model
                foreach (var subAsset in existingContainerSubAssets)
                {
                    Object.DestroyImmediate(subAsset, true);
                }
            }
            
            AssetDatabase.SaveAssets();
            
            // PRODUCE PREFAB, VISUAL PREFAB, MECHANIC PREFABS
            GameObject tempPrefab = null;
            
            // get paths
            var modelPath = sourceAssetPath.Substring(0, sourceAssetPath.LastIndexOf("/"));
            var assetsRootPath = modelPath.Substring(0, modelPath.LastIndexOf("/"));
            
            // simulate path
            var pathVisualPrefabsFolder = assetsRootPath + "/Prefabs/VisualPrefabs";
            var pathPrefabsFolder = assetsRootPath + "/Prefabs";
            
            var pathToSaveVisualPrefab = pathVisualPrefabsFolder + "/" + sourceModel.name + "_!VisualPrefab.prefab";
            var pathToSaveMechanicPrefab = pathPrefabsFolder + "/" + sourceModel.name + ".prefab";
            
            // VISUAL PREFAB
            // recursively create folders
            Directory.CreateDirectory(pathVisualPrefabsFolder);

            tempPrefab = StaticVisualPrefabCreate(sourceModel, pathToSaveVisualPrefab);
            
            // MECHANIC PREFAB
            if (!AssetDatabase.AssetPathExists(pathToSaveMechanicPrefab))
            {
                if (tempPrefab)
                {
                    StaticMechanicPrefabCreate(tempPrefab, pathToSaveMechanicPrefab);
                }
            }
            
            // AssetDatabase.ForceReserializeAssets();
            // AssetDatabase.ForceReserializeAssets(new []{sourceAssetPath});
            
            DeleteAsset(sourceAssetPath);
        }
        
        private static Mesh AttachSubAsset(Mesh sourceSubAsset, string containerPath)
        {
            Mesh artifactSubAsset = Object.Instantiate(sourceSubAsset);
            artifactSubAsset.name = sourceSubAsset.name + "_!Artifact";
                    
            AssetDatabase.AddObjectToAsset(artifactSubAsset, containerPath);

            return artifactSubAsset;
        }
        
        private static Mesh UpdateSubAsset(List<Mesh> existingContainerSubAssets, Mesh sourceSubAsset, string containerPath)
        {
            // are there any subAssets that in theory need to be updated?
            var artifactToUpdate = existingContainerSubAssets.Find(x => x.name.Equals(sourceSubAsset.name + "_!Artifact"));
            if (artifactToUpdate)
            {
                Vector3[] normals = sourceSubAsset.normals;
                
                sourceSubAsset.name += "_!Artifact";
                EditorUtility.CopySerialized(sourceSubAsset, artifactToUpdate);
                
                artifactToUpdate.normals = normals;
                
                // all this recalculation crap doesn't seem to be necessary
                // artifactToUpdate.RecalculateBounds();
                // RecalculateNormals always leaves a UV seam trail
                // artifactToUpdate.RecalculateNormals(); 
                // artifactToUpdate.RecalculateTangents();
                        
                // remove from the list the subAssets that have been processed, the remaining subAssets in
                // the list are candidates for destroy because the Artist removed them in the original model
                existingContainerSubAssets.Remove(artifactToUpdate);
                            
                // assign a mesh to the source model meshFilter so that when the model is saved to the prefab, the link will already be thrown through
                return artifactToUpdate;
            }
            
            // if a subAsset is present in the source model, but it is not present in the container subAssets,
            // then this subAsset must be created and attached to the container 
            var artifactSubAsset = AttachSubAsset(sourceSubAsset, containerPath);
            // assign a mesh to the source model meshFilter so that when the model is saved to the prefab, the link will already be thrown through
            return artifactSubAsset;
        }
        
        private static GameObject StaticVisualPrefabCreate(GameObject sourceModel, string pathToSaveVisualPrefab)
        {
            GameObject objInst = PrefabUtility.InstantiatePrefab(sourceModel) as GameObject;
            PrefabUtility.UnpackPrefabInstance(objInst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            
            objInst.name += "_!Visual";
            objInst.isStatic = true;
            
            var visualPrefab = PrefabUtility.SaveAsPrefabAsset(objInst, pathToSaveVisualPrefab);
            
            Object.DestroyImmediate(objInst);
            AssetDatabase.SaveAssets();
            
            return visualPrefab;
        }
        
        private static void StaticMechanicPrefabCreate(GameObject visualPrefab, string pathToSavePrefab)
        {
            GameObject rootObj = new GameObject();

            GameObject visualRootObj = new GameObject();
            visualRootObj.transform.parent = rootObj.transform;
            visualRootObj.name = "Visual";
            
            rootObj.name = visualPrefab.name.Replace("_!Visual", "");

            GameObject visualPrefabInst = PrefabUtility.InstantiatePrefab(visualPrefab) as GameObject;
            visualPrefabInst.transform.parent = visualRootObj.transform;
            
            PrefabUtility.SaveAsPrefabAsset(rootObj, pathToSavePrefab);

            Object.DestroyImmediate(visualPrefabInst);
            Object.DestroyImmediate(rootObj);
            
            AssetDatabase.SaveAssets();
        }
        
        #endregion

        #region Helpers
        
        private static String GetFullPathWithoutExtension(String path)
        {
            return Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
        }
        private static void DeleteAsset(string path)
        {
            AssetDatabase.DeleteAsset(path);
        }
        
        #endregion
    }
}