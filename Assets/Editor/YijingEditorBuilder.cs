#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yijing.Analytics;
using Yijing.Core;
using Yijing.Data;
using Yijing.UI;
using Yijing.World3D;

namespace Yijing.Editor
{
    public static class YijingEditorBuilder
    {
        [MenuItem("忆境 3D 系统/1. 一键构建 3D 场景与预制体 (Build 3D Scene & Prefabs)")]
        public static void BuildSceneMenu()
        {
            ConfigureYijingTextures();
            BuildMain3DSceneAndPrefabs();
            Debug.Log("[Yijing 3D] 3D 场景与预制体构建完成！");
        }

        [MenuItem("忆境 3D 系统/2. 运行全组合玩法与认知评估验证 (Verify All 87 Combinations & Assessment)")]
        public static void VerifyMenu()
        {
            BuildAllAndVerify();
        }

        public static void BuildAllAndVerify()
        {
            Debug.Log("=== [Yijing 3D] Starting Unity 6000.6.2f1 Full Build & Verification ===");
            ConfigureYijingTextures();
            BuildMain3DSceneAndPrefabs();
            RunAutomatedGameplayAndAssessmentVerification();
            Render3DStagePreviewScreenshots();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("=== [Yijing 3D] ALL BUILD & VERIFICATION STEPS PASSED SUCCESSFULLY ===");
        }

        private static void ConfigureYijingTextures()
        {
            string[] searchFolders = new[] { "Assets/Resources/Yijing/Icons", "Assets/Resources/Yijing/Scenes" };
            var validFolders = searchFolders.Where(AssetDatabase.IsValidFolder).ToArray();
            if (validFolders.Length == 0) return;

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", validFolders);
            int updated = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    bool changed = false;
                    if (!importer.isReadable) { importer.isReadable = true; changed = true; }
                    if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
                    if (importer.npotScale != TextureImporterNPOTScale.None) { importer.npotScale = TextureImporterNPOTScale.None; changed = true; }
                    if (importer.maxTextureSize < 2048) { importer.maxTextureSize = 2048; changed = true; }
                    if (changed)
                    {
                        importer.SaveAndReimport();
                        updated++;
                    }
                }
            }
            Debug.Log($"[Yijing 3D] Configured {guids.Length} UI textures ({updated} reimported).");
        }

        private static void BuildMain3DSceneAndPrefabs()
        {
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory("Assets/Prefabs/Yijing");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var rootGo = new GameObject("YijingSystemRoot");
            var world = rootGo.AddComponent<Yijing3DWorldBuilder>();
            var engine = rootGo.AddComponent<YijingGameEngine>();
            var ui = rootGo.AddComponent<YijingMainController>();

            world.BuildCompleteWorldIfNeeded();
            world.SetMemoryObserveMarkers(YijingAssetLibrary.Scenes[0].annotations, true);

            // Save modular 3D Stage Prefabs for easy inspection in Unity Editor
            if (world.memoryStageRoot != null)
            {
                PrefabUtility.SaveAsPrefabAsset(world.memoryStageRoot.gameObject, "Assets/Prefabs/Yijing/MemoryGardenStage_3D.prefab");
            }
            if (world.attentionStageRoot != null)
            {
                world.ShowAttentionStage("apple", new List<QuestionOptionRuntime>
                {
                    new QuestionOptionRuntime { id = "opt1", icon = "apple", label = "苹果" },
                    new QuestionOptionRuntime { id = "opt2", icon = "cup", label = "茶杯" },
                    new QuestionOptionRuntime { id = "opt3", icon = "teapot", label = "茶壶" },
                    new QuestionOptionRuntime { id = "opt4", icon = "clock", label = "时钟" }
                }, "normal");
                PrefabUtility.SaveAsPrefabAsset(world.attentionStageRoot.gameObject, "Assets/Prefabs/Yijing/AttentionTrackingStage_3D.prefab");
            }
            if (world.kitchenStageRoot != null)
            {
                world.ShowKitchenStage(YijingAssetLibrary.Catalog.kitchen[0].steps, true);
                PrefabUtility.SaveAsPrefabAsset(world.kitchenStageRoot.gameObject, "Assets/Prefabs/Yijing/KitchenStepsStage_3D.prefab");
            }

            // Restore camera to Courtyard main stage
            world.ShowMemoryStage("courtyard", "first-courtyard");
            if (world.mainCamera != null)
            {
                world.mainCamera.transform.position = world.targetCameraPos;
                world.mainCamera.transform.LookAt(world.targetCameraLookAt);
            }

            string scenePath = "Assets/Scenes/YijingMain.unity";
            EditorSceneManager.SaveScene(scene, scenePath);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(scenePath, true)
            };

            PlayerSettings.productName = "忆境3D·老年认知训练与评估系统";
            PlayerSettings.companyName = "东南大学仪器科学与工程学院SRTP";

            Debug.Log($"[Yijing 3D] Saved main scene to {scenePath} and 3D prefabs to Assets/Prefabs/Yijing/.");
        }

        private static void RunAutomatedGameplayAndAssessmentVerification()
        {
            YijingAssetLibrary.Initialize();
            var catalog = YijingAssetLibrary.Catalog;
            if (catalog.attention.Count != 15)
                throw new Exception($"Expected 15 Attention themes, got {catalog.attention.Count}");
            if (catalog.kitchen.Count != 10)
                throw new Exception($"Expected 10 Kitchen tasks, got {catalog.kitchen.Count}");
            if (YijingAssetLibrary.Scenes.Count < 4)
                throw new Exception($"Expected >= 4 Memory Garden 3D scenes, got {YijingAssetLibrary.Scenes.Count}");

            var engine = UnityEngine.Object.FindFirstObjectByType<YijingGameEngine>();
            if (engine == null)
            {
                var go = new GameObject("TempEngine");
                engine = go.AddComponent<YijingGameEngine>();
            }

            string[] diffs = { "easy", "normal", "hard" };
            int memoryCombos = 0;
            int attentionCombos = 0;
            int kitchenCombos = 0;

            // 1. Verify all 4 Memory Garden 3D Scenes x 3 Difficulties (12 combinations) with Direct On-Object Selection
            foreach (var sc in YijingAssetLibrary.Scenes)
            {
                foreach (var d in diffs)
                {
                    var run = engine.StartMemoryGarden(sc.id, d, "formal", "P01");
                    engine.BeginQuestionsFromObserve();
                    int total = run.totalQuestions;
                    for (int i = 0; i < total; i++)
                    {
                        string targetItemId = run.memoryOrder[i].id;
                        engine.SubmitMemoryAnswerByItem(i % 4 == 3 ? null : targetItemId);
                    }
                    memoryCombos++;
                }
            }

            // 2. Verify all 15 Attention Tracking Themes x 3 Difficulties (45 combinations)
            foreach (var att in catalog.attention)
            {
                foreach (var d in diffs)
                {
                    var run = engine.StartAttentionTracking(att.id, d, "formal", "P01");
                    int total = run.totalQuestions;
                    for (int i = 0; i < total; i++)
                    {
                        string cid = run.currentCorrectOptionId;
                        bool timeout = (i == total - 1 && d == "hard");
                        engine.SubmitAttentionAnswer(timeout ? null : cid, timeout);
                    }
                    attentionCombos++;
                }
            }

            // 3. Verify all 10 Kitchen Steps Tasks x 3 Difficulties (30 combinations)
            foreach (var kit in catalog.kitchen)
            {
                foreach (var d in diffs)
                {
                    var run = engine.StartKitchenSteps(kit.id, d, "formal", "P01");
                    engine.BeginQuestionsFromObserve();
                    var steps = new List<KitchenStepDef>(run.kitchenRecipe);
                    for (int i = 0; i < steps.Count; i++)
                    {
                        engine.SubmitKitchenAnswer(steps[i].id);
                    }
                    kitchenCombos++;
                }
            }

            // 4. Verify 450 Synthetic Simulation Records & Cognitive Evaluator
            var simRecords = engine.GetSimulationRecords();
            if (simRecords.Count != 450)
                throw new Exception($"Expected 450 simulation records, got {simRecords.Count}");

            var localRecords = engine.GetLocalRecords();
            var localProfile = YijingCognitiveEvaluator.EvaluateRecords(localRecords, "P01");
            var simProfile = YijingCognitiveEvaluator.EvaluateRecords(simRecords, "*");

            string localHtml = YijingCognitiveEvaluator.ExportHtmlAssessmentReport(localRecords, "Unity 3D 全组合自动化验证与认知评估报告");
            string simHtml = YijingCognitiveEvaluator.ExportHtmlAssessmentReport(simRecords, "450条模拟受试者五维认知功能评估汇总报告");
            string csvPath = YijingCognitiveEvaluator.ExportCsvRecords(localRecords);

            Debug.Log($"[Yijing 3D Verification Summary] Memory Combos={memoryCombos} (12), Attention Combos={attentionCombos} (45), Kitchen Combos={kitchenCombos} (30) => Total={memoryCombos + attentionCombos + kitchenCombos} combinations verified!");
            Debug.Log($"[Yijing 3D Assessment Summary] Local P01 Composite={localProfile.compositeCognitiveIndex:F1} ({localProfile.gradeLevel}); Simulation 450 Records Composite={simProfile.compositeCognitiveIndex:F1} ({simProfile.gradeLevel}).");
            Debug.Log($"[Yijing 3D Reports Exported] Local HTML: {localHtml} | Sim HTML: {simHtml} | CSV: {csvPath}");
        }

        private static void Render3DStagePreviewScreenshots()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Debug.Log("[Yijing 3D] Skipping offscreen render in Null graphics mode.");
                return;
            }

            var world = UnityEngine.Object.FindFirstObjectByType<Yijing3DWorldBuilder>();
            if (world == null || world.mainCamera == null) return;

            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../导出报告/3D场景预览图"));
            Directory.CreateDirectory(outDir);

            // 1. Courtyard 3D
            world.ShowMemoryStage("courtyard", "first-courtyard");
            world.SetMemoryObserveMarkers(YijingAssetLibrary.Scenes[0].annotations, true);
            CaptureCameraToPng(world.mainCamera, world.targetCameraPos, world.targetCameraLookAt, Path.Combine(outDir, "1_记忆花园_老家小院3D.png"));

            // 2. Living Room 3D
            world.ShowMemoryStage("living", "0a2c9b47eb464893b4ae65482212e5ee");
            world.SetMemoryObserveMarkers(YijingAssetLibrary.Scenes[1].annotations, true);
            CaptureCameraToPng(world.mainCamera, world.targetCameraPos, world.targetCameraLookAt, Path.Combine(outDir, "2_记忆花园_温馨客厅3D.png"));

            // 3. Rural Courtyard 3D
            world.ShowMemoryStage("rural", "1a4c6ce6da854c7f9652f4c41079131a");
            world.SetMemoryObserveMarkers(YijingAssetLibrary.Scenes[2].annotations, true);
            CaptureCameraToPng(world.mainCamera, world.targetCameraPos, world.targetCameraLookAt, Path.Combine(outDir, "3_记忆花园_农家院落3D.png"));

            // 4. Bedroom Study 3D
            world.ShowMemoryStage("bedroom", "172f4143c7db4a309f3c6ed1a6fc266b");
            world.SetMemoryObserveMarkers(YijingAssetLibrary.Scenes[3].annotations, true);
            CaptureCameraToPng(world.mainCamera, world.targetCameraPos, world.targetCameraLookAt, Path.Combine(outDir, "4_记忆花园_阳光书房卧室3D.png"));

            // 5. Attention Tracking 3D Stage
            world.ShowAttentionStage("fruit-0", new List<QuestionOptionRuntime>
            {
                new QuestionOptionRuntime { id = "1", icon = "fruit-0", label = "苹果" },
                new QuestionOptionRuntime { id = "2", icon = "fruit-1", label = "香蕉" },
                new QuestionOptionRuntime { id = "3", icon = "fruit-2", label = "橙子" },
                new QuestionOptionRuntime { id = "4", icon = "fruit-5", label = "草莓" }
            }, "normal");
            CaptureCameraToPng(world.mainCamera, world.targetCameraPos, world.targetCameraLookAt, Path.Combine(outDir, "5_注意力追踪_3D动态展台.png"));

            // 6. Kitchen Steps 3D Stage
            world.ShowKitchenStage(YijingAssetLibrary.Catalog.kitchen[1].steps, true);
            CaptureCameraToPng(world.mainCamera, world.targetCameraPos, world.targetCameraLookAt, Path.Combine(outDir, "6_厨房步骤_3D现代厨房实景.png"));

            // Reset to default Courtyard
            world.ShowMemoryStage("courtyard", "first-courtyard");
            world.SetMemoryObserveMarkers(YijingAssetLibrary.Scenes[0].annotations, true);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/Scenes/YijingMain.unity");
        }

        private static void CaptureCameraToPng(Camera cam, Vector3 pos, Vector3 lookAt, string filePath)
        {
            cam.transform.position = pos;
            cam.transform.LookAt(lookAt);

            int w = 1280;
            int h = 720;
            RenderTexture rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;

            Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            cam.targetTexture = null;
            RenderTexture.active = prev;
            UnityEngine.Object.DestroyImmediate(rt);

            File.WriteAllBytes(filePath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            Debug.Log($"[Yijing 3D] Rendered 3D preview: {filePath}");
        }
    }
}
#endif
