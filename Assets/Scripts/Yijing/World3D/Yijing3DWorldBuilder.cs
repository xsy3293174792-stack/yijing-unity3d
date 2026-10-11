using System;
using System.Collections.Generic;
using UnityEngine;
using Yijing.Core;
using Yijing.Data;

namespace Yijing.World3D
{
    public class Yijing3DInteractable : MonoBehaviour
    {
        public string kind;     // "memory_item", "attention_token", "kitchen_step"
        public int zoneIndex;   // 0..7 item slot index for memory garden
        public string optionId; // item id or option id
        public string label;
        public int laneIndex;
        public float cycleSpeed = 4.5f;
        public float phaseOffset = 0f;
        public float laneMinX = -3.6f;
        public float laneMaxX = 3.6f;
        public bool isMovingToken = false;
        public Vector3 baseLocalPos;
        public Renderer highlightRenderer;
        public List<Renderer> groupRenderers = new List<Renderer>();
        public Color baseColor = Color.white;

        public void SetHighlight(bool hovered)
        {
            if (highlightRenderer != null && highlightRenderer.sharedMaterial != null)
            {
                highlightRenderer.material.color = hovered
                    ? new Color(1.0f, 0.88f, 0.22f, 1f)
                    : baseColor;
            }
        }
    }

    public class Yijing3DWorldBuilder : MonoBehaviour
    {
        public static Yijing3DWorldBuilder Instance { get; private set; }

        [Header("Stage Roots")]
        public Transform memoryStageRoot;
        public Transform attentionStageRoot;
        public Transform kitchenStageRoot;

        [Header("Memory Theme Sub-Stages")]
        public GameObject courtyardGroup;
        public GameObject livingGroup;
        public GameObject bedroomGroup;
        public GameObject ruralGroup;
        public Transform memoryMarkersRoot;

        [Header("Attention Dynamic Elements")]
        public Renderer attentionTargetPlaque;
        public Transform attentionLanesRoot;
        private readonly List<Yijing3DInteractable> _activeAttentionTokens = new List<Yijing3DInteractable>();

        [Header("Kitchen Dynamic Elements")]
        public Transform kitchenEaselsRoot;
        public Transform kitchenChefTransform;

        [Header("Camera Control")]
        public Camera mainCamera;
        public Vector3 targetCameraPos = new Vector3(0f, 2.55f, -6.2f);
        public Vector3 targetCameraLookAt = new Vector3(0f, 1.05f, 0.8f);
        public bool enableGentleCameraSway = true;
        public bool reduceMotion = false;

        private Yijing3DInteractable _hoveredInteractable;
        public string HoveredMemoryItemId => (_hoveredInteractable != null && _hoveredInteractable.kind == "memory_item") ? _hoveredInteractable.optionId : null;

        // Events for 3D clicks
        public Action<int> OnMemoryZoneClicked3D;
        public Action<string> OnMemoryItemClicked3D;
        public Action<string> OnAttentionTokenClicked3D;
        public Action<string> OnKitchenStepClicked3D;

        private readonly Dictionary<string, GameObject> _memoryItemObjects = new Dictionary<string, GameObject>();

        private void Awake()
        {
            Instance = this;
        }

        private static void SafeDestroyChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }
        }

        public void BuildCompleteWorldIfNeeded()
        {
            YijingAssetLibrary.Initialize();
            _memoryItemObjects.Clear();

            if (mainCamera == null)
            {
                mainCamera = Camera.main;
                if (mainCamera == null)
                {
                    var camGo = new GameObject("Main Camera");
                    camGo.tag = "MainCamera";
                    mainCamera = camGo.AddComponent<Camera>();
                    camGo.AddComponent<AudioListener>();
                }
            }
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0.76f, 0.87f, 0.94f, 1f);
            mainCamera.fieldOfView = 44f;
            mainCamera.nearClipPlane = 0.1f;
            mainCamera.farClipPlane = 150f;

            SetupLighting();

            var existingMem = transform.Find("MemoryGardenStage_3D");
            if (existingMem != null) DestroyImmediate(existingMem.gameObject);
            memoryStageRoot = new GameObject("MemoryGardenStage_3D").transform;
            memoryStageRoot.SetParent(transform, false);
            memoryStageRoot.localPosition = Vector3.zero;
            BuildMemoryGardenStage(memoryStageRoot);

            var existingAtt = transform.Find("AttentionTrackingStage_3D");
            if (existingAtt != null) DestroyImmediate(existingAtt.gameObject);
            attentionStageRoot = new GameObject("AttentionTrackingStage_3D").transform;
            attentionStageRoot.SetParent(transform, false);
            attentionStageRoot.localPosition = new Vector3(35f, 0f, 0f);
            BuildAttentionTrackingStage(attentionStageRoot);

            var existingKit = transform.Find("KitchenStepsStage_3D");
            if (existingKit != null) DestroyImmediate(existingKit.gameObject);
            kitchenStageRoot = new GameObject("KitchenStepsStage_3D").transform;
            kitchenStageRoot.SetParent(transform, false);
            kitchenStageRoot.localPosition = new Vector3(70f, 0f, 0f);
            BuildKitchenStepsStage(kitchenStageRoot);

            ShowMemoryStage("courtyard", "first-courtyard");
        }

        private void SetupLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.60f, 0.65f);
            RenderSettings.ambientEquatorColor = new Color(0.54f, 0.51f, 0.47f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.33f, 0.29f);

            var lightTr = transform.Find("SunDirectionalLight");
            Light dirLight;
            if (lightTr == null)
            {
                var lightGo = new GameObject("SunDirectionalLight");
                lightGo.transform.SetParent(transform, false);
                dirLight = lightGo.AddComponent<Light>();
            }
            else
            {
                dirLight = lightTr.GetComponent<Light>();
            }
            dirLight.type = LightType.Directional;
            dirLight.color = new Color(1.0f, 0.95f, 0.86f);
            dirLight.intensity = 0.84f;
            dirLight.shadows = LightShadows.Soft;
            dirLight.shadowStrength = 0.68f;
            dirLight.transform.rotation = Quaternion.Euler(40f, -28f, 0f);
        }

        private static void AddWarmIndoorFillLight(Transform parent, string name, Vector3 localPos, float intensity = 0.72f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var pt = go.AddComponent<Light>();
            pt.type = LightType.Point;
            pt.range = 18f;
            pt.intensity = intensity;
            pt.color = new Color(1.0f, 0.95f, 0.86f);
            pt.shadows = LightShadows.None;
        }

        private void Update()
        {
            if (mainCamera != null)
            {
                Vector3 desiredPos = targetCameraPos;
                if (enableGentleCameraSway && !reduceMotion)
                {
                    desiredPos.x += Mathf.Sin(Time.time * 0.4f) * 0.05f;
                    desiredPos.y += Mathf.Cos(Time.time * 0.3f) * 0.025f;
                }
                mainCamera.transform.position = Vector3.Lerp(mainCamera.transform.position, desiredPos, Time.deltaTime * 6f);

                Quaternion desiredRot = Quaternion.LookRotation(targetCameraLookAt - mainCamera.transform.position, Vector3.up);
                mainCamera.transform.rotation = Quaternion.Slerp(mainCamera.transform.rotation, desiredRot, Time.deltaTime * 7f);
            }

            if (!reduceMotion && _activeAttentionTokens.Count > 0)
            {
                for (int i = 0; i < _activeAttentionTokens.Count; i++)
                {
                    var tok = _activeAttentionTokens[i];
                    if (tok == null || !tok.isMovingToken) continue;
                    float freq = (Mathf.PI * 2f) / Mathf.Max(1.5f, tok.cycleSpeed);
                    float t = (Mathf.Sin(Time.time * freq + tok.phaseOffset) + 1f) * 0.5f;
                    float x = Mathf.Lerp(tok.laneMinX, tok.laneMaxX, t);
                    float bob = Mathf.Sin(Time.time * 2.6f + tok.phaseOffset) * 0.04f;
                    tok.transform.localPosition = new Vector3(x, tok.baseLocalPos.y + bob, tok.baseLocalPos.z);
                }
            }

            if (kitchenChefTransform != null && !reduceMotion)
            {
                kitchenChefTransform.localRotation = Quaternion.Euler(0f, 180f + Mathf.Sin(Time.time * 1.4f) * 8f, 0f);
            }

            Handle3DPointerInteraction();
        }

        private void Handle3DPointerInteraction()
        {
            if (mainCamera == null) return;
            Vector3 mp = Input.mousePosition;
            if (mp.x < 0 || mp.y < 0 || mp.x > Screen.width || mp.y > Screen.height) return;

            Ray ray = mainCamera.ScreenPointToRay(mp);
            if (Physics.Raycast(ray, out RaycastHit hit, 60f))
            {
                var interactable = hit.collider.GetComponentInParent<Yijing3DInteractable>();
                if (_hoveredInteractable != interactable)
                {
                    if (_hoveredInteractable != null) _hoveredInteractable.SetHighlight(false);
                    _hoveredInteractable = interactable;
                    if (_hoveredInteractable != null) _hoveredInteractable.SetHighlight(true);
                }

                if (interactable != null && Input.GetMouseButtonDown(0))
                {
                    float normY = mp.y / Mathf.Max(1f, Screen.height);
                    if (normY > 0.12f && normY < 0.88f)
                    {
                        if (interactable.kind == "memory_item")
                        {
                            OnMemoryItemClicked3D?.Invoke(interactable.optionId);
                            OnMemoryZoneClicked3D?.Invoke(interactable.zoneIndex);
                        }
                        else if (interactable.kind == "attention_token")
                        {
                            OnAttentionTokenClicked3D?.Invoke(interactable.optionId);
                        }
                        else if (interactable.kind == "kitchen_step")
                        {
                            OnKitchenStepClicked3D?.Invoke(interactable.optionId);
                        }
                    }
                }
            }
            else if (_hoveredInteractable != null)
            {
                _hoveredInteractable.SetHighlight(false);
                _hoveredInteractable = null;
            }
        }

        public Vector3 GetMemoryItemWorldPosition(MemoryAnnotationDef item)
        {
            if (item == null) return Vector3.zero;
            if (memoryStageRoot != null)
                return memoryStageRoot.TransformPoint(item.worldLocalPos);
            return item.worldLocalPos;
        }

        public Vector3 GetMemoryMarkerWorldPosition(MemoryAnnotationDef item)
        {
            if (item == null) return Vector3.zero;
            Vector3 pinOffset = item.markerOffset.sqrMagnitude > 0.01f ? item.markerOffset : new Vector3(0f, 0.72f, 0f);
            Vector3 local = item.worldLocalPos + pinOffset;
            if (memoryStageRoot != null)
                return memoryStageRoot.TransformPoint(local);
            return local;
        }

        // ====================================================================
        // STAGE 1: 记忆花园 纯 3D 场景建模 (无背景图、无九宫格、开源 FBX 丰富细节)
        // ====================================================================
        private void BuildMemoryGardenStage(Transform root)
        {
            // 1. 老家小院 · 朱檐庭院 (Courtyard)
            courtyardGroup = new GameObject("Theme_Courtyard_3D");
            courtyardGroup.transform.SetParent(root, false);
            BuildCourtyard3DModels(courtyardGroup.transform);

            // 2. 家中就餐 · 温馨客厅 (Living Room)
            livingGroup = new GameObject("Theme_LivingRoom_3D");
            livingGroup.transform.SetParent(root, false);
            BuildLivingRoom3DModels(livingGroup.transform);

            // 3. 阳光书房与卧室 (Bedroom & Study)
            bedroomGroup = new GameObject("Theme_Bedroom_3D");
            bedroomGroup.transform.SetParent(root, false);
            BuildBedroom3DModels(bedroomGroup.transform);

            // 4. 午后的农村庭院 (Rural Harvest Courtyard)
            ruralGroup = new GameObject("Theme_RuralCourtyard_3D");
            ruralGroup.transform.SetParent(root, false);
            BuildRuralCourtyard3DModels(ruralGroup.transform);

            // 3D On-Object Selection Markers Root (Direct pins & base rings on items)
            memoryMarkersRoot = new GameObject("MemoryOnObjectMarkers_3D").transform;
            memoryMarkersRoot.SetParent(root, false);
        }

        private void BuildOutdoorSkyAndHills3D(Transform parent, Color hillTint1, Color hillTint2)
        {
            var envRoot = new GameObject("OutdoorHorizonEnvironment_3D").transform;
            envRoot.SetParent(parent, false);

            // Subtle natural distant horizon ridge (no plastic spheres or floating pill clouds)
            Texture2D grassTex = YijingAssetLibrary.LoadModelTexture("RealGrassGround") ?? YijingAssetLibrary.GetProceduralTexture("GrassLawn");
            var farMeadow = CreateTexturedBox(envRoot, "FarHorizonMeadow", new Vector3(0f, -0.22f, 9.5f), new Vector3(28f, 0.12f, 12f), grassTex, hillTint1);
            farMeadow.GetComponent<Renderer>().material.mainTextureScale = new Vector2(12f, 6f);
        }

        // --------------------------------------------------------------------
        // SCENE 1: 老家小院 · 朱檐庭院 (Courtyard - Photorealistic PolyHaven Scanned 3D Models & 1K Textures)
        // --------------------------------------------------------------------
        private void BuildCourtyard3DModels(Transform parent)
        {
            Texture2D woodTex = YijingAssetLibrary.LoadModelTexture("RealWoodFloor") ?? YijingAssetLibrary.GetProceduralTexture("WoodPlanks");
            Texture2D stoneTex = YijingAssetLibrary.LoadModelTexture("RealStoneFloor") ?? YijingAssetLibrary.GetProceduralTexture("StonePaving");
            Texture2D grassTex = YijingAssetLibrary.LoadModelTexture("RealGrassGround") ?? YijingAssetLibrary.GetProceduralTexture("GrassLawn");
            Texture2D roofTex = YijingAssetLibrary.LoadModelTexture("RealRoofTile") ?? YijingAssetLibrary.GetProceduralTexture("RedRoofTile");
            Texture2D greyRoofTex = YijingAssetLibrary.LoadModelTexture("RealRoofTile") ?? YijingAssetLibrary.GetProceduralTexture("GreyTileRoof");
            Texture2D wallTex = YijingAssetLibrary.LoadModelTexture("RealCourtyardWall") ?? YijingAssetLibrary.GetProceduralTexture("WhiteCourtyardWall");

            BuildOutdoorSkyAndHills3D(parent, new Color(0.74f, 0.80f, 0.68f), new Color(0.66f, 0.74f, 0.62f));

            // Courtyard Grounds: Photorealistic Grass + Slate Stone Courtyard + Timber Veranda Deck
            var lawn = CreateTexturedBox(parent, "OuterGardenLawn", new Vector3(0f, -0.16f, 1.5f), new Vector3(16f, 0.1f, 14f), grassTex, new Color(0.82f, 0.88f, 0.76f));
            lawn.GetComponent<Renderer>().material.mainTextureScale = new Vector2(8f, 7f);

            var stoneCourt = CreateTexturedBox(parent, "CourtyardStoneFloor", new Vector3(0f, -0.09f, 0.3f), new Vector3(10.6f, 0.14f, 7.6f), stoneTex, new Color(0.88f, 0.86f, 0.82f));
            stoneCourt.GetComponent<Renderer>().material.mainTextureScale = new Vector2(5f, 4f);

            var deck = CreateTexturedBox(parent, "VerandaWoodDeck", new Vector3(0f, -0.025f, 0.45f), new Vector3(6.4f, 0.05f, 3.8f), woodTex, new Color(0.86f, 0.72f, 0.56f));
            deck.GetComponent<Renderer>().material.mainTextureScale = new Vector2(4f, 2f);

            // Huizhou Courtyard Enclosure Walls with natural plaster texture (non-overlapping corners, zero Z-fighting)
            Color wallTint = new Color(0.86f, 0.84f, 0.79f);
            CreateTexturedBox(parent, "BackWallLeft", new Vector3(-3.0f, 1.45f, 3.5f), new Vector3(4.2f, 2.9f, 0.28f), wallTex, wallTint);
            CreateTexturedBox(parent, "BackWallRight", new Vector3(3.0f, 1.45f, 3.5f), new Vector3(4.2f, 2.9f, 0.28f), wallTex, wallTint);
            CreateTexturedBox(parent, "BackWallTopArch", new Vector3(0f, 2.55f, 3.5f), new Vector3(1.8f, 0.7f, 0.28f), wallTex, wallTint);
            CreateTexturedBox(parent, "BackWallGreyEave", new Vector3(0f, 3.01f, 3.5f), new Vector3(10.6f, 0.22f, 0.52f), greyRoofTex, new Color(0.45f, 0.48f, 0.52f));

            CreateTexturedBox(parent, "LeftCourtyardWall", new Vector3(-5.1f, 1.35f, 0.20f), new Vector3(0.28f, 2.7f, 6.3f), wallTex, wallTint);
            CreateTexturedBox(parent, "LeftWallGreyEave", new Vector3(-5.1f, 2.81f, 0.20f), new Vector3(0.50f, 0.20f, 6.5f), greyRoofTex, new Color(0.45f, 0.48f, 0.52f));

            CreateTexturedBox(parent, "RightPavilionWall", new Vector3(4.2f, 1.55f, 0.65f), new Vector3(0.26f, 3.1f, 5.4f), wallTex, wallTint);
            CreateTexturedBox(parent, "RightWallEave", new Vector3(4.2f, 3.21f, 0.65f), new Vector3(0.52f, 0.24f, 5.6f), roofTex, new Color(0.68f, 0.32f, 0.24f));

            // Red Pavilion Columns & Top Beam (朱红廊柱)
            float[] colX = { -3.8f, 3.8f };
            for (int i = 0; i < 2; i++)
            {
                CreateTexturedBox(parent, "PavilionColumn_" + i, new Vector3(colX[i], 1.72f, 2.6f), new Vector3(0.24f, 3.44f, 0.24f), woodTex, new Color(0.62f, 0.22f, 0.16f));
            }
            CreateTexturedBox(parent, "PavilionTopBeam", new Vector3(0f, 3.56f, 2.6f), new Vector3(8.4f, 0.24f, 0.28f), woodTex, new Color(0.58f, 0.20f, 0.15f));

            // Background Photorealistic Scanned Trees & Shrubs
            SpawnOpenSourceFBX(parent, "ph_tree_small_02", new Vector3(-0.55f, 0f, 4.2f), 3.1f, 15f);
            SpawnOpenSourceFBX(parent, "ph_pine_tree_01", new Vector3(0.65f, 0f, 4.4f), 3.3f, -20f);
            SpawnOpenSourceFBX(parent, "ph_tree_small_02", new Vector3(-4.5f, 0f, 4.3f), 3.5f, 0f);
            SpawnOpenSourceFBX(parent, "ph_pine_tree_01", new Vector3(4.4f, 0f, 4.2f), 3.5f, 35f);
            SpawnOpenSourceFBX(parent, "ph_tree_stump_01", new Vector3(-4.1f, 0f, 0.5f), 0.55f, 25f);
            SpawnOpenSourceFBX(parent, "ph_shrub_01", new Vector3(3.3f, 0f, 2.5f), 0.78f, 0f);

            // === 8 Photorealistic Target Items for Courtyard ===
            var scene = YijingAssetLibrary.Scenes[0];

            // 1. cy-0: 青松翠柏 (Photorealistic Pine Tree + Shrub + Tree Stump)
            var item0 = CreateInteractiveTargetRoot(parent, scene.annotations[0], new Vector3(1.5f, 2.2f, 1.5f));
            float g0 = -scene.annotations[0].worldLocalPos.y;
            SpawnOpenSourceFBX(item0.transform, "ph_pine_tree_01", new Vector3(-0.12f, g0, 0.05f), 2.35f, 0f);
            SpawnOpenSourceFBX(item0.transform, "ph_shrub_02", new Vector3(0.32f, g0, -0.15f), 0.85f, 30f);
            SpawnOpenSourceFBX(item0.transform, "ph_fern_02", new Vector3(0.18f, g0, 0.22f), 0.52f, 10f);

            // 2. cy-1: 石灯古塔 (Photorealistic Carved Stool Pedestal + Vintage Brass Lantern)
            var item1 = CreateInteractiveTargetRoot(parent, scene.annotations[1], new Vector3(1.1f, 1.9f, 1.1f));
            float g1 = -scene.annotations[1].worldLocalPos.y;
            SpawnOpenSourceFBX(item1.transform, "ph_chinese_stool", new Vector3(0f, g1, 0f), 0.62f, 0f);
            SpawnOpenSourceFBX(item1.transform, "ph_Lantern_01", new Vector3(0f, g1 + 0.62f, 0f), 0.76f, 0f);

            // 3. cy-2: 雕花木窗 (Photorealistic Chinese Carved Console Table + 3D Hollow Lattice Window with distinct depth layers, zero Z-fighting)
            var item2 = CreateInteractiveTargetRoot(parent, scene.annotations[2], new Vector3(1.2f, 1.6f, 1.1f));
            float g2 = -scene.annotations[2].worldLocalPos.y;
            SpawnOpenSourceFBX(item2.transform, "ph_chinese_console_table", new Vector3(-0.22f, g2, 0f), 0.82f, -90f, 1.15f);
            SpawnOpenSourceFBX(item2.transform, "ph_antique_ceramic_vase_01", new Vector3(-0.22f, g2 + 0.52f, 0f), 0.44f, 0f);
            Color latticeWoodCol = new Color(0.50f, 0.24f, 0.14f);
            // Layer 1 (Back, x = 0.22): Recessed Rice Paper Backing
            CreatePrimitiveBox(item2.transform, "RicePaperPane", new Vector3(0.22f, -0.05f, 0.0f), new Vector3(0.03f, 0.98f, 0.98f), new Color(0.93f, 0.88f, 0.76f));
            // Layer 2 (Middle, x = 0.17): 4 Hollow Outer Wooden Frame Bars (non-overlapping with center pane)
            CreateTexturedBox(item2.transform, "LatticeFrameTop", new Vector3(0.17f, 0.49f, 0.0f), new Vector3(0.06f, 0.10f, 1.16f), woodTex, latticeWoodCol);
            CreateTexturedBox(item2.transform, "LatticeFrameBottom", new Vector3(0.17f, -0.59f, 0.0f), new Vector3(0.06f, 0.10f, 1.16f), woodTex, latticeWoodCol);
            CreateTexturedBox(item2.transform, "LatticeFrameFront", new Vector3(0.17f, -0.05f, -0.53f), new Vector3(0.06f, 0.98f, 0.10f), woodTex, latticeWoodCol);
            CreateTexturedBox(item2.transform, "LatticeFrameBack", new Vector3(0.17f, -0.05f, 0.53f), new Vector3(0.06f, 0.98f, 0.10f), woodTex, latticeWoodCol);
            // Layer 3 (Front-A, x = 0.15): Horizontal Mullion Bars
            CreateTexturedBox(item2.transform, "LatticeBarH1", new Vector3(0.15f, 0.13f, 0.0f), new Vector3(0.025f, 0.04f, 0.96f), woodTex, latticeWoodCol);
            CreateTexturedBox(item2.transform, "LatticeBarH2", new Vector3(0.15f, -0.23f, 0.0f), new Vector3(0.025f, 0.04f, 0.96f), woodTex, latticeWoodCol);
            // Layer 4 (Front-B, x = 0.125): Vertical Mullion Bars (separated in X so H and V bars never Z-fight)
            CreateTexturedBox(item2.transform, "LatticeBarV1", new Vector3(0.125f, -0.05f, -0.18f), new Vector3(0.025f, 0.96f, 0.04f), woodTex, latticeWoodCol);
            CreateTexturedBox(item2.transform, "LatticeBarV2", new Vector3(0.125f, -0.05f, 0.18f), new Vector3(0.025f, 0.96f, 0.04f), woodTex, latticeWoodCol);

            // 4. cy-3: 迎客盆景 (Photorealistic Chinese Stool + Scanned Potted Plant)
            var item3 = CreateInteractiveTargetRoot(parent, scene.annotations[3], new Vector3(1.1f, 1.3f, 1.1f));
            float g3 = -scene.annotations[3].worldLocalPos.y;
            SpawnOpenSourceFBX(item3.transform, "ph_chinese_stool", new Vector3(0f, g3, 0f), 0.50f, 0f);
            SpawnOpenSourceFBX(item3.transform, "ph_potted_plant_01", new Vector3(0f, g3 + 0.48f, 0f), 0.78f, 20f);

            // 5. cy-4: 紫砂茶席 (Photorealistic Chinese Tea Table + Tea Set on top + Two Chinese Armchairs!)
            var item4 = CreateInteractiveTargetRoot(parent, scene.annotations[4], new Vector3(1.8f, 1.3f, 1.4f));
            float g4 = -scene.annotations[4].worldLocalPos.y;
            SpawnOpenSourceFBX(item4.transform, "ph_chinese_tea_table", new Vector3(0f, g4, 0f), 0.68f, 0f, 1.12f);
            SpawnOpenSourceFBX(item4.transform, "ph_chinese_armchair", new Vector3(-0.92f, g4, 0f), 0.88f, 90f);
            SpawnOpenSourceFBX(item4.transform, "ph_chinese_armchair", new Vector3(0.92f, g4, 0f), 0.88f, -90f);
            SpawnOpenSourceFBX(item4.transform, "ph_tea_set_01", new Vector3(0f, g4 + 0.72f, 0f), 0.24f, 0f, 0.56f);

            // 6. cy-5: 紫兰花钵 (Photorealistic Clay Planter Pot + Potted Plant & Flowers)
            var item5 = CreateInteractiveTargetRoot(parent, scene.annotations[5], new Vector3(1.1f, 1.2f, 1.1f));
            float g5 = -scene.annotations[5].worldLocalPos.y;
            SpawnOpenSourceFBX(item5.transform, "ph_potted_plant_02", new Vector3(0f, g5, 0f), 1.05f, 0f);
            SpawnOpenSourceFBX(item5.transform, "ph_flower_gazania", new Vector3(0.24f, g5, 0.15f), 0.44f, 35f);

            // 7. cy-6: 金菊石坛 (Photorealistic Planter Box + Gazania Flowers + Fern)
            var item6 = CreateInteractiveTargetRoot(parent, scene.annotations[6], new Vector3(1.3f, 1.1f, 1.3f));
            float g6 = -scene.annotations[6].worldLocalPos.y;
            SpawnOpenSourceFBX(item6.transform, "ph_planter_box_01", new Vector3(0f, g6, 0f), 0.54f, 0f, 0.96f);
            SpawnOpenSourceFBX(item6.transform, "ph_flower_gazania", new Vector3(-0.18f, g6 + 0.46f, 0f), 0.46f, 0f);
            SpawnOpenSourceFBX(item6.transform, "ph_flower_gazania", new Vector3(0.18f, g6 + 0.46f, 0.08f), 0.48f, 60f);
            SpawnOpenSourceFBX(item6.transform, "ph_fern_02", new Vector3(0f, g6 + 0.44f, -0.10f), 0.45f, -20f);

            // 8. cy-7: 庭院藤椅 (Photorealistic Wooden Rocking Chair)
            var item7 = CreateInteractiveTargetRoot(parent, scene.annotations[7], new Vector3(1.2f, 1.2f, 1.2f));
            float g7 = -scene.annotations[7].worldLocalPos.y;
            SpawnOpenSourceFBX(item7.transform, "ph_Rockingchair_01", new Vector3(0f, g7, 0f), 0.98f, -35f);
        }

        // --------------------------------------------------------------------
        // SCENE 2: 家中就餐 · 温馨客厅 (Realistic Home Living Room with Built-in Left Window, Curtains, Wall Painting & Cozy Seating)
        // --------------------------------------------------------------------
        private void BuildLivingRoom3DModels(Transform parent)
        {
            Texture2D woodTex = YijingAssetLibrary.LoadModelTexture("RealWoodFloor") ?? YijingAssetLibrary.GetProceduralTexture("WoodPlanks");

            // 1. Realistic Indoor Living Room Architecture: Warm Oak Floor, Soft Blue-Grey Back Wall, Cream Side Walls, Baseboards & Crown Molding
            var floor = CreateTexturedBox(parent, "HardwoodFloor", new Vector3(0f, -0.08f, 0.3f), new Vector3(11.6f, 0.16f, 8.2f), woodTex, new Color(0.86f, 0.72f, 0.56f));
            floor.GetComponent<Renderer>().material.mainTextureScale = new Vector2(6f, 4f);

            Color backWallColor = new Color(0.79f, 0.84f, 0.89f); // Soft light blue-grey wall matching reference photo
            Color sideWallColor = new Color(0.90f, 0.88f, 0.84f); // Warm cream side walls
            Color baseboardWood = new Color(0.50f, 0.32f, 0.18f);

            CreatePrimitiveBox(parent, "LivingBackWall", new Vector3(0f, 2.05f, 3.20f), new Vector3(10.6f, 4.2f, 0.24f), backWallColor);
            CreatePrimitiveBox(parent, "LivingLeftWall", new Vector3(-5.20f, 2.05f, 0.20f), new Vector3(0.24f, 4.2f, 6.2f), sideWallColor);
            CreatePrimitiveBox(parent, "LivingRightWall", new Vector3(5.20f, 2.05f, 0.20f), new Vector3(0.24f, 4.2f, 6.2f), sideWallColor);
            CreatePrimitiveBox(parent, "LivingCeilingSlab", new Vector3(0f, 4.18f, 0.30f), new Vector3(10.8f, 0.16f, 6.4f), new Color(0.95f, 0.94f, 0.91f));
            AddWarmIndoorFillLight(parent, "LivingRoomWarmFillLight", new Vector3(0f, 3.45f, 0.20f), 0.78f);

            // Wooden Baseboards (踢脚线) & Crown Molding (顶角线)
            CreateTexturedBox(parent, "BackWallBaseboard", new Vector3(0f, 0.08f, 3.04f), new Vector3(10.1f, 0.16f, 0.04f), woodTex, baseboardWood);
            CreateTexturedBox(parent, "LeftWallBaseboard", new Vector3(-5.04f, 0.08f, 0.20f), new Vector3(0.04f, 0.16f, 5.6f), woodTex, baseboardWood);
            CreateTexturedBox(parent, "RightWallBaseboard", new Vector3(5.04f, 0.08f, 0.20f), new Vector3(0.04f, 0.16f, 5.6f), woodTex, baseboardWood);
            CreatePrimitiveBox(parent, "BackCrownMolding", new Vector3(0f, 4.02f, 3.03f), new Vector3(10.2f, 0.12f, 0.06f), new Color(0.94f, 0.92f, 0.88f));

            // Framed Floral Wall Painting above the Sofa (客厅沙发背景装饰挂画, distinct Z depths)
            CreateTexturedBox(parent, "WallArtWoodFrame", new Vector3(-0.25f, 2.28f, 3.03f), new Vector3(1.18f, 1.18f, 0.04f), woodTex, new Color(0.46f, 0.30f, 0.18f));
            CreatePrimitiveBox(parent, "WallArtCreamMat", new Vector3(-0.25f, 2.28f, 2.99f), new Vector3(0.98f, 0.98f, 0.02f), new Color(0.96f, 0.95f, 0.92f));
            var artQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            artQuad.name = "WallArtFloralCanvas";
            artQuad.transform.SetParent(parent, false);
            artQuad.transform.localPosition = new Vector3(-0.25f, 2.28f, 2.97f);
            artQuad.transform.localScale = new Vector3(0.76f, 0.76f, 1f);
            DestroyImmediate(artQuad.GetComponent<Collider>());
            artQuad.GetComponent<Renderer>().material = CreateSliceMaterial(YijingAssetLibrary.GetIconSlice("flower"));

            // Cozy Warm Grey-Beige Area Rug under Sofa & Coffee Table + Sunlit Floor Glow near Left Window
            CreatePrimitiveBox(parent, "LivingRoomWovenRug", new Vector3(-0.20f, 0.008f, 0.25f), new Vector3(3.6f, 0.014f, 2.3f), new Color(0.78f, 0.80f, 0.82f));
            CreatePrimitiveBox(parent, "SunlitFloorPatch_1", new Vector3(-3.35f, 0.005f, 0.85f), new Vector3(1.35f, 0.006f, 1.85f), new Color(0.98f, 0.92f, 0.76f));

            var scene = YijingAssetLibrary.Scenes[1];

            // 1. lv-0: 阳光落地窗 (Embedded into the Left Wall x=-5.0 with Warm Terracotta Curtains & Windowsill Plant!)
            var item0 = CreateInteractiveTargetRoot(parent, scene.annotations[0], new Vector3(1.1f, 2.2f, 2.2f));
            float lg0 = -scene.annotations[0].worldLocalPos.y;
            Color winWoodCol = new Color(0.46f, 0.28f, 0.16f);
            Color curtainCol = new Color(0.76f, 0.44f, 0.28f); // True warm terracotta curtain color matching reference photo
            // Layer 1 (x = -0.44): Bright Sunlit Window Glass Pane embedded on left wall
            CreatePrimitiveBox(item0.transform, "SunlitWindowGlass", new Vector3(-0.44f, lg0 + 1.48f, 0f), new Vector3(0.03f, 2.15f, 1.95f), new Color(0.84f, 0.94f, 1.0f));
            // Layer 2 (x = -0.39): 4 Hollow Window Frame Bars
            CreateTexturedBox(item0.transform, "WindowFrameTop", new Vector3(-0.39f, lg0 + 2.60f, 0f), new Vector3(0.06f, 0.10f, 2.15f), woodTex, winWoodCol);
            CreateTexturedBox(item0.transform, "WindowFrameBottom", new Vector3(-0.39f, lg0 + 0.36f, 0f), new Vector3(0.06f, 0.10f, 2.15f), woodTex, winWoodCol);
            CreateTexturedBox(item0.transform, "WindowFrameBack", new Vector3(-0.39f, lg0 + 1.48f, 1.02f), new Vector3(0.06f, 2.15f, 0.10f), woodTex, winWoodCol);
            CreateTexturedBox(item0.transform, "WindowFrameFront", new Vector3(-0.39f, lg0 + 1.48f, -1.02f), new Vector3(0.06f, 2.15f, 0.10f), woodTex, winWoodCol);
            // Layer 3 (x = -0.36): Horizontal Mullions (upper transom + lower wainscot rail)
            CreateTexturedBox(item0.transform, "WindowMullionUpperH", new Vector3(-0.36f, lg0 + 2.12f, 0f), new Vector3(0.03f, 0.05f, 1.94f), woodTex, winWoodCol);
            CreateTexturedBox(item0.transform, "WindowMullionLowerH", new Vector3(-0.36f, lg0 + 0.78f, 0f), new Vector3(0.03f, 0.06f, 1.94f), woodTex, winWoodCol);
            // Layer 4 (x = -0.33): Vertical Mullions
            CreateTexturedBox(item0.transform, "WindowMullionV1", new Vector3(-0.33f, lg0 + 1.48f, -0.34f), new Vector3(0.03f, 2.14f, 0.05f), woodTex, winWoodCol);
            CreateTexturedBox(item0.transform, "WindowMullionV2", new Vector3(-0.33f, lg0 + 1.48f, 0.34f), new Vector3(0.03f, 2.14f, 0.05f), woodTex, winWoodCol);
            // Windowsill Ledge & Windowsill Green Plant
            CreateTexturedBox(item0.transform, "WindowSillLedge", new Vector3(-0.24f, lg0 + 0.78f, 0f), new Vector3(0.24f, 0.05f, 2.10f), woodTex, winWoodCol);
            SpawnOpenSourceFBX(item0.transform, "ph_potted_plant_04", new Vector3(-0.18f, lg0 + 0.81f, 0.05f), 0.42f, 90f);
            // Warm Terracotta Curtains on Left & Right of Window + Curtain Rod
            CreateTexturedBox(item0.transform, "CurtainRod", new Vector3(-0.26f, lg0 + 2.74f, 0f), new Vector3(0.05f, 0.05f, 2.55f), woodTex, winWoodCol);
            CreatePrimitiveBox(item0.transform, "CurtainDrapeBack", new Vector3(-0.24f, lg0 + 1.38f, 1.08f), new Vector3(0.14f, 2.68f, 0.38f), curtainCol);
            CreatePrimitiveBox(item0.transform, "CurtainDrapeFront", new Vector3(-0.24f, lg0 + 1.38f, -1.08f), new Vector3(0.14f, 2.68f, 0.38f), curtainCol);

            // 2. lv-1: 立式落地灯 (Warm Corner Cabinet + Tall Cream Lampshade Lamp matching the reference photo!)
            var item1 = CreateInteractiveTargetRoot(parent, scene.annotations[1], new Vector3(1.1f, 1.9f, 1.1f));
            float lg1 = -scene.annotations[1].worldLocalPos.y;
            var lampCab = SpawnOpenSourceFBX(item1.transform, "ph_ClassicNightstand_01", new Vector3(0f, lg1, 0f), 0.74f, 0f);
            float lampTopY = GetRenderedTopLocalY(item1.transform, lampCab, lg1 + 0.74f);
            // Sculpted wooden lamp base & twin stems + warm cream drum lampshade sitting flush on cabinet top
            CreateTexturedBox(item1.transform, "LampWoodBase", new Vector3(0f, lampTopY + 0.02f, 0f), new Vector3(0.26f, 0.04f, 0.22f), woodTex, baseboardWood);
            CreateTexturedBox(item1.transform, "LampWoodStemL", new Vector3(-0.04f, lampTopY + 0.28f, 0f), new Vector3(0.035f, 0.50f, 0.035f), woodTex, baseboardWood);
            CreateTexturedBox(item1.transform, "LampWoodStemR", new Vector3(0.04f, lampTopY + 0.28f, 0f), new Vector3(0.035f, 0.50f, 0.035f), woodTex, baseboardWood);
            var shade = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shade.name = "WarmCreamLampshade";
            shade.transform.SetParent(item1.transform, false);
            shade.transform.localPosition = new Vector3(0f, lampTopY + 0.62f, 0f);
            shade.transform.localScale = new Vector3(0.46f, 0.22f, 0.46f);
            DestroyImmediate(shade.GetComponent<Collider>());
            shade.GetComponent<Renderer>().material = CreateLitMaterial(null, new Color(0.98f, 0.95f, 0.86f));

            // 3. lv-2: 现代电视柜 (Elevated Solid-Wood Media Console + Television sitting flush at eye level + Decorative Books)
            var item2 = CreateInteractiveTargetRoot(parent, scene.annotations[2], new Vector3(1.8f, 1.6f, 1.1f));
            float lg2 = -scene.annotations[2].worldLocalPos.y;
            CreateTexturedBox(item2.transform, "MediaConsoleLegFL", new Vector3(-0.62f, lg2 + 0.11f, -0.18f), new Vector3(0.06f, 0.22f, 0.06f), woodTex, baseboardWood);
            CreateTexturedBox(item2.transform, "MediaConsoleLegFR", new Vector3(0.62f, lg2 + 0.11f, -0.18f), new Vector3(0.06f, 0.22f, 0.06f), woodTex, baseboardWood);
            CreateTexturedBox(item2.transform, "MediaConsoleLegBL", new Vector3(-0.62f, lg2 + 0.11f, 0.18f), new Vector3(0.06f, 0.22f, 0.06f), woodTex, baseboardWood);
            CreateTexturedBox(item2.transform, "MediaConsoleLegBR", new Vector3(0.62f, lg2 + 0.11f, 0.18f), new Vector3(0.06f, 0.22f, 0.06f), woodTex, baseboardWood);
            var tvCab = SpawnOpenSourceFBX(item2.transform, "ph_modern_wooden_cabinet", new Vector3(0f, lg2 + 0.21f, 0f), 0.36f, 165f, 1.52f);
            float tvTopY = GetRenderedTopLocalY(item2.transform, tvCab, lg2 + 0.52f);
            SpawnOpenSourceFBX(item2.transform, "ph_Television_01", new Vector3(-0.08f, tvTopY, 0f), 0.58f, 165f);
            SpawnOpenSourceFBX(item2.transform, "ph_decorative_book_set_01", new Vector3(0.46f, tvTopY, 0.04f), 0.20f, -15f);

            // 4. lv-3: 龟背竹盆栽 (Lush Indoor Potted Foliage Plants near the Sunny Window)
            var item3 = CreateInteractiveTargetRoot(parent, scene.annotations[3], new Vector3(1.1f, 1.3f, 1.1f));
            float lg3 = -scene.annotations[3].worldLocalPos.y;
            SpawnOpenSourceFBX(item3.transform, "ph_potted_plant_02", new Vector3(0f, lg3, 0f), 1.08f, 15f);
            SpawnOpenSourceFBX(item3.transform, "ph_potted_plant_04", new Vector3(0.28f, lg3, -0.12f), 0.56f, -20f);

            // 5. lv-4: 浅蓝长沙发 (Light-Blue Fabric Sofa facing the camera + Plump Throw Pillows)
            var item4 = CreateInteractiveTargetRoot(parent, scene.annotations[4], new Vector3(2.4f, 1.2f, 1.2f));
            float lg4 = -scene.annotations[4].worldLocalPos.y;
            SpawnOpenSourceFBX(item4.transform, "ph_Sofa_01", new Vector3(0f, lg4, 0f), 0.94f, 180f, 2.05f);
            SpawnOpenSourceFBX(item4.transform, "ph_throw_pillows_01", new Vector3(-0.46f, lg4 + 0.42f, -0.04f), 0.30f, 15f);
            SpawnOpenSourceFBX(item4.transform, "ph_throw_pillows_01", new Vector3(0.46f, lg4 + 0.42f, -0.04f), 0.30f, -20f);

            // 6. lv-5: 复古收音机 (Classic Wooden Side Cabinet + Vintage Radio & Alarm Clock sitting flush on top)
            var item5 = CreateInteractiveTargetRoot(parent, scene.annotations[5], new Vector3(1.1f, 1.3f, 1.1f));
            float lg5 = -scene.annotations[5].worldLocalPos.y;
            var radioCab = SpawnOpenSourceFBX(item5.transform, "ph_ClassicNightstand_01", new Vector3(0f, lg5, 0f), 0.72f, -35f);
            float radioTopY = GetRenderedTopLocalY(item5.transform, radioCab, lg5 + 0.72f);
            SpawnOpenSourceFBX(item5.transform, "ph_vintage_radio_transceiver", new Vector3(-0.04f, radioTopY, 0f), 0.34f, -35f);
            SpawnOpenSourceFBX(item5.transform, "ph_alarm_clock_01", new Vector3(0.18f, radioTopY, -0.10f), 0.16f, -35f);

            // 7. lv-6: 单人休闲椅 (Modern Upholstered Lounge Armchair angled toward Coffee Table)
            var item6 = CreateInteractiveTargetRoot(parent, scene.annotations[6], new Vector3(1.2f, 1.1f, 1.2f));
            float lg6 = -scene.annotations[6].worldLocalPos.y;
            SpawnOpenSourceFBX(item6.transform, "ph_modern_arm_chair_01", new Vector3(0f, lg6, 0f), 0.88f, 135f, 0.86f);

            // 8. lv-7: 茶几水果盘 (Solid Wood Coffee Table in front of Sofa + Porcelain Tea Set + Plate of Fresh Red Apples sitting flush on top)
            var item7 = CreateInteractiveTargetRoot(parent, scene.annotations[7], new Vector3(1.6f, 1.0f, 1.2f));
            float lg7 = -scene.annotations[7].worldLocalPos.y;
            var coffeeTbl = SpawnOpenSourceFBX(item7.transform, "ph_modern_coffee_table_01", new Vector3(0f, lg7, 0f), 0.46f, 0f, 1.26f);
            float coffeeTopY = GetRenderedTopLocalY(item7.transform, coffeeTbl, lg7 + 0.44f);
            SpawnOpenSourceFBX(item7.transform, "ph_tea_set_01", new Vector3(-0.24f, coffeeTopY, 0.02f), 0.22f, 0f, 0.44f);
            // Ceramic Fruit Plate with 3 Scanned Red Apples
            var fruitPlate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            fruitPlate.name = "CeramicFruitPlate";
            fruitPlate.transform.SetParent(item7.transform, false);
            fruitPlate.transform.localPosition = new Vector3(0.24f, coffeeTopY + 0.01f, 0.02f);
            fruitPlate.transform.localScale = new Vector3(0.32f, 0.015f, 0.32f);
            DestroyImmediate(fruitPlate.GetComponent<Collider>());
            fruitPlate.GetComponent<Renderer>().material = CreateLitMaterial(null, new Color(0.96f, 0.95f, 0.92f));
            SpawnOpenSourceFBX(item7.transform, "ph_food_apple_01", new Vector3(0.19f, coffeeTopY + 0.02f, 0.05f), 0.11f, 0f);
            SpawnOpenSourceFBX(item7.transform, "ph_food_apple_01", new Vector3(0.29f, coffeeTopY + 0.02f, 0.04f), 0.11f, 120f);
            SpawnOpenSourceFBX(item7.transform, "ph_food_apple_01", new Vector3(0.24f, coffeeTopY + 0.02f, -0.04f), 0.10f, 240f);
        }

        // --------------------------------------------------------------------
        // SCENE 3: 午后的农村庭院 (Authentic Rural Courtyard with Wooden Veranda, Pitched-Roof Farmhouses, Round Dining Table & Harvest Baskets)
        // --------------------------------------------------------------------
        private void BuildRuralCourtyard3DModels(Transform parent)
        {
            Texture2D woodTex = YijingAssetLibrary.LoadModelTexture("RealWoodFloor") ?? YijingAssetLibrary.GetProceduralTexture("WoodPlanks");
            Texture2D stoneTex = YijingAssetLibrary.LoadModelTexture("RealStoneFloor") ?? YijingAssetLibrary.GetProceduralTexture("StonePaving");
            Texture2D grassTex = YijingAssetLibrary.LoadModelTexture("RealGrassGround") ?? YijingAssetLibrary.GetProceduralTexture("GrassLawn");
            Texture2D roofTex = YijingAssetLibrary.LoadModelTexture("RealRoofTile") ?? YijingAssetLibrary.GetProceduralTexture("RedRoofTile");
            Texture2D greyRoofTex = YijingAssetLibrary.LoadModelTexture("RealRoofTile") ?? YijingAssetLibrary.GetProceduralTexture("GreyTileRoof");
            Texture2D wallTex = YijingAssetLibrary.LoadModelTexture("RealCourtyardWall") ?? YijingAssetLibrary.GetProceduralTexture("WhiteCourtyardWall");

            BuildOutdoorSkyAndHills3D(parent, new Color(0.72f, 0.80f, 0.64f), new Color(0.64f, 0.74f, 0.56f));

            // 1. Rural Courtyard Ground: Outer Meadow + Slate Courtyard + Sunlit Timber Veranda Deck (matching reference photo!)
            var grassGround = CreateTexturedBox(parent, "RuralGrassGround", new Vector3(0f, -0.16f, 1.5f), new Vector3(18f, 0.1f, 14f), grassTex, new Color(0.84f, 0.88f, 0.74f));
            grassGround.GetComponent<Renderer>().material.mainTextureScale = new Vector2(8f, 7f);

            var patio = CreateTexturedBox(parent, "RuralStonePatio", new Vector3(0f, -0.09f, 0.35f), new Vector3(10.6f, 0.14f, 7.4f), stoneTex, new Color(0.86f, 0.83f, 0.77f));
            patio.GetComponent<Renderer>().material.mainTextureScale = new Vector2(5f, 4f);

            var woodDeck = CreateTexturedBox(parent, "RuralWoodVerandaDeck", new Vector3(0f, -0.025f, 0.20f), new Vector3(6.6f, 0.05f, 4.2f), woodTex, new Color(0.88f, 0.74f, 0.54f));
            woodDeck.GetComponent<Renderer>().material.mainTextureScale = new Vector2(5f, 3f);

            // 2. Low Whitewashed Rural Courtyard Walls & Raised Planter Beds + Wooden Courtyard Gate
            Color ruralWallTint = new Color(0.90f, 0.88f, 0.82f);
            CreateTexturedBox(parent, "RuralBackWallLeft", new Vector3(-2.85f, 0.65f, 3.45f), new Vector3(3.9f, 1.3f, 0.26f), wallTex, ruralWallTint);
            CreateTexturedBox(parent, "RuralBackWallLeftCoping", new Vector3(-2.85f, 1.35f, 3.45f), new Vector3(4.05f, 0.12f, 0.36f), greyRoofTex, new Color(0.46f, 0.48f, 0.50f));
            CreateTexturedBox(parent, "RuralBackWallRight", new Vector3(2.85f, 0.65f, 3.45f), new Vector3(3.9f, 1.3f, 0.26f), wallTex, ruralWallTint);
            CreateTexturedBox(parent, "RuralBackWallRightCoping", new Vector3(2.85f, 1.35f, 3.45f), new Vector3(4.05f, 0.12f, 0.36f), greyRoofTex, new Color(0.46f, 0.48f, 0.50f));
            SpawnOpenSourceFBX(parent, "nat_fence_gate", new Vector3(0f, 0f, 3.45f), 1.15f, 0f);

            CreateTexturedBox(parent, "RuralLeftLowWall", new Vector3(-5.1f, 0.75f, 0.30f), new Vector3(0.26f, 1.5f, 6.0f), wallTex, ruralWallTint);
            CreateTexturedBox(parent, "RuralLeftWallCoping", new Vector3(-5.1f, 1.55f, 0.30f), new Vector3(0.38f, 0.12f, 6.1f), greyRoofTex, new Color(0.46f, 0.48f, 0.50f));
            CreateTexturedBox(parent, "RuralRightLowWall", new Vector3(5.1f, 0.75f, 0.30f), new Vector3(0.26f, 1.5f, 6.0f), wallTex, ruralWallTint);
            CreateTexturedBox(parent, "RuralRightWallCoping", new Vector3(5.1f, 1.55f, 0.30f), new Vector3(0.38f, 0.12f, 6.1f), greyRoofTex, new Color(0.46f, 0.48f, 0.50f));

            // 3. Traditional Timber Veranda Columns & Overhead Pergola Beams (农家木廊柱与木梁, spaced wide so trees are never occluded!)
            Color timberCol = new Color(0.66f, 0.42f, 0.26f);
            CreateTexturedBox(parent, "VerandaColumnLeft", new Vector3(-4.85f, 1.75f, 2.05f), new Vector3(0.24f, 3.50f, 0.24f), woodTex, timberCol);
            CreateTexturedBox(parent, "VerandaColumnRight", new Vector3(4.85f, 1.75f, 2.05f), new Vector3(0.24f, 3.50f, 0.24f), woodTex, timberCol);
            CreateTexturedBox(parent, "VerandaMainBeam", new Vector3(0f, 3.58f, 2.05f), new Vector3(10.1f, 0.22f, 0.28f), woodTex, timberCol);
            CreateTexturedBox(parent, "VerandaRafterLeft", new Vector3(-2.8f, 3.74f, 1.45f), new Vector3(0.16f, 0.14f, 2.2f), woodTex, timberCol);
            CreateTexturedBox(parent, "VerandaRafterCenter", new Vector3(0f, 3.74f, 1.45f), new Vector3(0.16f, 0.14f, 2.2f), woodTex, timberCol);
            CreateTexturedBox(parent, "VerandaRafterRight", new Vector3(2.8f, 3.74f, 1.45f), new Vector3(0.16f, 0.14f, 2.2f), woodTex, timberCol);

            // 4. Distant Pitched-Roof Rural Farmhouse Cottages (远景白墙坡顶红瓦农家房舍) & Background Trees beyond the Courtyard Wall
            BuildDistantRuralFarmhouse(parent, "LeftFarmhouse", new Vector3(-4.3f, 0f, 6.4f), wallTex, roofTex, woodTex);
            BuildDistantRuralFarmhouse(parent, "RightFarmhouse", new Vector3(4.4f, 0f, 6.2f), wallTex, roofTex, woodTex);
            SpawnOpenSourceFBX(parent, "ph_tree_small_02", new Vector3(-1.3f, 0f, 5.2f), 3.4f, 15f);
            SpawnOpenSourceFBX(parent, "ph_pine_tree_01", new Vector3(1.5f, 0f, 5.4f), 3.5f, -20f);

            var scene = YijingAssetLibrary.Scenes[2];

            // 1. rc-0: 金秋大橡树 (Lush Courtyard Shade Tree + Shrubs & Ferns in Left Planter)
            var item0 = CreateInteractiveTargetRoot(parent, scene.annotations[0], new Vector3(1.6f, 2.3f, 1.6f));
            float rg0 = -scene.annotations[0].worldLocalPos.y;
            SpawnOpenSourceFBX(item0.transform, "ph_tree_small_02", new Vector3(0f, rg0, 0f), 2.55f, 0f);
            SpawnOpenSourceFBX(item0.transform, "ph_shrub_02", new Vector3(0.36f, rg0, -0.18f), 0.78f, 25f);
            SpawnOpenSourceFBX(item0.transform, "ph_fern_02", new Vector3(-0.28f, rg0, 0.22f), 0.48f, 0f);

            // 2. rc-1: 木栅柴火垛 (Neat Rural Firewood Rack with Stacked Round Logs, Chopping Stump & Barn Lantern against Back Wall)
            var item1 = CreateInteractiveTargetRoot(parent, scene.annotations[1], new Vector3(1.5f, 1.5f, 1.2f));
            float rg1 = -scene.annotations[1].worldLocalPos.y;
            Color logBarkCol = new Color(0.48f, 0.32f, 0.18f);
            CreateTexturedBox(item1.transform, "FirewoodRackPostL", new Vector3(-0.48f, rg1 + 0.55f, 0f), new Vector3(0.08f, 1.10f, 0.48f), woodTex, logBarkCol);
            CreateTexturedBox(item1.transform, "FirewoodRackPostR", new Vector3(0.28f, rg1 + 0.55f, 0f), new Vector3(0.08f, 1.10f, 0.48f), woodTex, logBarkCol);
            CreateTexturedBox(item1.transform, "FirewoodRackRoof", new Vector3(-0.10f, rg1 + 1.14f, 0f), new Vector3(0.96f, 0.08f, 0.62f), roofTex, new Color(0.68f, 0.34f, 0.24f));
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    var log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    log.name = $"StackedLog_{row}_{col}";
                    log.transform.SetParent(item1.transform, false);
                    log.transform.localPosition = new Vector3(-0.34f + col * 0.16f + (row % 2) * 0.03f, rg1 + 0.14f + row * 0.16f, 0f);
                    log.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    log.transform.localScale = new Vector3(0.14f, 0.24f, 0.14f);
                    DestroyImmediate(log.GetComponent<Collider>());
                    log.GetComponent<Renderer>().material = CreateLitMaterial(woodTex, new Color(0.62f - row * 0.04f, 0.42f - row * 0.03f, 0.24f));
                }
            }
            SpawnOpenSourceFBX(item1.transform, "ph_tree_stump_01", new Vector3(0.52f, rg1, -0.05f), 0.48f, 0f);
            SpawnOpenSourceFBX(item1.transform, "ph_Lantern_01", new Vector3(-0.10f, rg1 + 0.58f, -0.05f), 0.48f, 0f);

            // 3. rc-2: 翠竹篱笆林 (Evergreen Pine & Lush Green Shrub Grove in Right Planter)
            var item2 = CreateInteractiveTargetRoot(parent, scene.annotations[2], new Vector3(1.5f, 2.2f, 1.5f));
            float rg2 = -scene.annotations[2].worldLocalPos.y;
            SpawnOpenSourceFBX(item2.transform, "ph_pine_tree_01", new Vector3(-0.12f, rg2, 0f), 2.40f, 0f);
            SpawnOpenSourceFBX(item2.transform, "ph_shrub_01", new Vector3(0.28f, rg2, 0.12f), 0.92f, 35f);
            SpawnOpenSourceFBX(item2.transform, "ph_shrub_02", new Vector3(-0.26f, rg2, -0.18f), 0.72f, -15f);

            // 4. rc-3: 丰收瓜果篓 (Earthenware Harvest Jars + Golden Pumpkins + Sweet Potatoes & Fresh Apples on Wooden Tray)
            var item3 = CreateInteractiveTargetRoot(parent, scene.annotations[3], new Vector3(1.4f, 1.1f, 1.3f));
            float rg3 = -scene.annotations[3].worldLocalPos.y;
            SpawnOpenSourceFBX(item3.transform, "ph_ceramic_pot", new Vector3(-0.22f, rg3, 0.08f), 0.54f, 0f);
            SpawnOpenSourceFBX(item3.transform, "ph_planter_pot_clay", new Vector3(0.24f, rg3, 0.12f), 0.46f, 35f);
            SpawnOpenSourceFBX(item3.transform, "nat_crop_pumpkin", new Vector3(-0.04f, rg3, -0.20f), 0.34f, 20f);
            SpawnOpenSourceFBX(item3.transform, "nat_crop_pumpkin", new Vector3(0.24f, rg3, -0.16f), 0.28f, -30f);
            SpawnOpenSourceFBX(item3.transform, "ph_sweet_potato", new Vector3(-0.28f, rg3, -0.18f), 0.10f, 15f);
            SpawnOpenSourceFBX(item3.transform, "ph_food_apple_01", new Vector3(0.08f, rg3, 0.04f), 0.12f, 0f);

            // 5. rc-4: 农家大圆桌 (Central Round Wooden Dining Table + 4 Realistic Dining Chairs + Tea Set sitting flush on top!)
            var item4 = CreateInteractiveTargetRoot(parent, scene.annotations[4], new Vector3(2.0f, 1.3f, 1.8f));
            float rg4 = -scene.annotations[4].worldLocalPos.y;
            var rTable = SpawnOpenSourceFBX(item4.transform, "ph_round_wooden_table_01", new Vector3(0f, rg4, 0f), 0.74f, 0f);
            float rTableTopY = GetRenderedTopLocalY(item4.transform, rTable, rg4 + 0.72f);
            SpawnOpenSourceFBX(item4.transform, "ph_dining_chair_02", new Vector3(-0.86f, rg4, -0.12f), 0.86f, 80f);
            SpawnOpenSourceFBX(item4.transform, "ph_dining_chair_02", new Vector3(0.86f, rg4, -0.12f), 0.86f, -80f);
            SpawnOpenSourceFBX(item4.transform, "ph_dining_chair_02", new Vector3(-0.48f, rg4, 0.72f), 0.86f, 145f);
            SpawnOpenSourceFBX(item4.transform, "ph_dining_chair_02", new Vector3(0.48f, rg4, 0.72f), 0.86f, -145f);
            SpawnOpenSourceFBX(item4.transform, "ph_tea_set_01", new Vector3(-0.08f, rTableTopY, 0f), 0.22f, 0f, 0.44f);
            SpawnOpenSourceFBX(item4.transform, "ph_food_apple_01", new Vector3(0.22f, rTableTopY, -0.06f), 0.11f, 30f);

            // 6. rc-5: 山石红花圃 (Raised Wooden Planter Box + Blooming Gazania Flowers & Ferns)
            var item5 = CreateInteractiveTargetRoot(parent, scene.annotations[5], new Vector3(1.3f, 1.1f, 1.3f));
            float rg5 = -scene.annotations[5].worldLocalPos.y;
            SpawnOpenSourceFBX(item5.transform, "ph_planter_box_01", new Vector3(0f, rg5, 0f), 0.52f, -12f, 0.96f);
            SpawnOpenSourceFBX(item5.transform, "ph_flower_gazania", new Vector3(-0.18f, rg5 + 0.44f, 0f), 0.48f, 0f);
            SpawnOpenSourceFBX(item5.transform, "ph_flower_gazania", new Vector3(0.18f, rg5 + 0.44f, 0.06f), 0.50f, 45f);
            SpawnOpenSourceFBX(item5.transform, "ph_fern_02", new Vector3(0f, rg5 + 0.44f, -0.08f), 0.46f, 20f);

            // 7. rc-7: 庭院木长椅 (Scanned Painted Wooden Bench along Left Veranda facing inward at rotY=-35)
            var item6 = CreateInteractiveTargetRoot(parent, scene.annotations[6], new Vector3(1.5f, 1.1f, 1.1f));
            float rg6 = -scene.annotations[6].worldLocalPos.y;
            SpawnOpenSourceFBX(item6.transform, "ph_painted_wooden_bench", new Vector3(0f, rg6, 0f), 0.84f, -35f, 1.35f);

            // 8. rc-8: 木桶汲水台 (Authentic Rural Stone Water Platform + Scanned Wooden Water Buckets & Ferns)
            var item7 = CreateInteractiveTargetRoot(parent, scene.annotations[7], new Vector3(1.4f, 1.1f, 1.3f));
            float rg7 = -scene.annotations[7].worldLocalPos.y;
            CreateTexturedBox(item7.transform, "StoneWellPlatform", new Vector3(0f, rg7 + 0.08f, 0f), new Vector3(1.16f, 0.16f, 0.96f), stoneTex, new Color(0.76f, 0.75f, 0.72f));
            SpawnOpenSourceFBX(item7.transform, "ph_wooden_bucket_01", new Vector3(-0.22f, rg7 + 0.16f, -0.05f), 0.56f, 20f);
            SpawnOpenSourceFBX(item7.transform, "ph_wooden_bucket_01", new Vector3(0.24f, rg7 + 0.16f, 0.10f), 0.44f, -35f);
            SpawnOpenSourceFBX(item7.transform, "ph_fern_02", new Vector3(0.38f, rg7, -0.22f), 0.42f, 0f);
        }

        private static void BuildDistantRuralFarmhouse(Transform parent, string name, Vector3 basePos, Texture2D wallTex, Texture2D roofTex, Texture2D woodTex)
        {
            var house = new GameObject(name).transform;
            house.SetParent(parent, false);
            house.localPosition = basePos;

            Color wallCol = new Color(0.91f, 0.89f, 0.83f);
            Color roofCol = new Color(0.72f, 0.34f, 0.25f);
            Color winCol = new Color(0.46f, 0.30f, 0.18f);

            CreateTexturedBox(house, "HouseWall", new Vector3(0f, 1.25f, 0f), new Vector3(3.6f, 2.5f, 2.4f), wallTex, wallCol);
            // Pitched gable roof slopes (人字坡顶红瓦)
            var slopeF = CreateTexturedBox(house, "RoofSlopeFront", new Vector3(0f, 2.75f, -0.68f), new Vector3(4.0f, 0.16f, 1.65f), roofTex, roofCol);
            slopeF.transform.localRotation = Quaternion.Euler(-22f, 0f, 0f);
            var slopeB = CreateTexturedBox(house, "RoofSlopeBack", new Vector3(0f, 2.75f, 0.68f), new Vector3(4.0f, 0.16f, 1.65f), roofTex, roofCol);
            slopeB.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);
            CreateTexturedBox(house, "RoofRidgeBeam", new Vector3(0f, 3.08f, 0f), new Vector3(4.05f, 0.18f, 0.22f), roofTex, new Color(0.45f, 0.46f, 0.48f));
            // Front farmhouse wooden window
            CreateTexturedBox(house, "FarmhouseWindowFrame", new Vector3(0f, 1.45f, -1.21f), new Vector3(0.95f, 0.85f, 0.04f), woodTex, winCol);
            CreatePrimitiveBox(house, "FarmhouseWindowPane", new Vector3(0f, 1.45f, -1.23f), new Vector3(0.78f, 0.68f, 0.02f), new Color(0.88f, 0.84f, 0.72f));
        }

        // --------------------------------------------------------------------
        // SCENE 4: 阳光书房与卧室 (Warm Sunlit Bedroom & Study with Right-Wall Window, Yellow Curtains, Oak Bed, Bookshelves & Study Desk)
        // --------------------------------------------------------------------
        private void BuildBedroom3DModels(Transform parent)
        {
            Texture2D woodTex = YijingAssetLibrary.LoadModelTexture("RealWoodFloor") ?? YijingAssetLibrary.GetProceduralTexture("WoodPlanks");

            // 1. Warm Honey-Oak Floor, Soft Cream Bedroom Walls, Baseboards, Ceiling & Sunlit Window Patches (matching 172f4143...)
            var floor = CreateTexturedBox(parent, "BedroomWoodFloor", new Vector3(0f, -0.08f, 0.3f), new Vector3(11.6f, 0.16f, 8.2f), woodTex, new Color(0.88f, 0.75f, 0.58f));
            floor.GetComponent<Renderer>().material.mainTextureScale = new Vector2(6f, 4f);

            Color bedroomWallTint = new Color(0.93f, 0.90f, 0.84f);
            Color oakTrimCol = new Color(0.58f, 0.38f, 0.22f);
            CreatePrimitiveBox(parent, "BedroomBackWall", new Vector3(0f, 2.05f, 3.20f), new Vector3(10.6f, 4.2f, 0.24f), bedroomWallTint);
            CreatePrimitiveBox(parent, "BedroomLeftWall", new Vector3(-5.20f, 2.05f, 0.20f), new Vector3(0.24f, 4.2f, 6.2f), bedroomWallTint);
            CreatePrimitiveBox(parent, "BedroomRightWall", new Vector3(5.20f, 2.05f, 0.20f), new Vector3(0.24f, 4.2f, 6.2f), bedroomWallTint);
            CreatePrimitiveBox(parent, "BedroomCeilingSlab", new Vector3(0f, 4.18f, 0.30f), new Vector3(10.8f, 0.16f, 6.4f), new Color(0.96f, 0.95f, 0.92f));
            AddWarmIndoorFillLight(parent, "BedroomWarmFillLight", new Vector3(0f, 3.45f, 0.20f), 0.78f);

            CreateTexturedBox(parent, "BedroomBackBaseboard", new Vector3(0f, 0.08f, 3.04f), new Vector3(10.1f, 0.16f, 0.04f), woodTex, oakTrimCol);
            CreateTexturedBox(parent, "BedroomLeftBaseboard", new Vector3(-5.04f, 0.08f, 0.20f), new Vector3(0.04f, 0.16f, 5.6f), woodTex, oakTrimCol);
            CreateTexturedBox(parent, "BedroomRightBaseboard", new Vector3(5.04f, 0.08f, 0.20f), new Vector3(0.04f, 0.16f, 5.6f), woodTex, oakTrimCol);

            // Built-in Right-Wall Sunlit Study Window with Soft Sunny-Yellow Curtains (阳光书房大窗与暖黄窗帘, matching 172f4143...!)
            Color yellowCurtainCol = new Color(0.96f, 0.88f, 0.52f);
            CreatePrimitiveBox(parent, "StudyWindowGlass", new Vector3(5.04f, 2.05f, 0.55f), new Vector3(0.03f, 1.85f, 1.95f), new Color(0.92f, 0.97f, 1.0f));
            CreateTexturedBox(parent, "StudyWindowFrameTop", new Vector3(4.99f, 2.98f, 0.55f), new Vector3(0.06f, 0.10f, 2.12f), woodTex, oakTrimCol);
            CreateTexturedBox(parent, "StudyWindowFrameBottom", new Vector3(4.99f, 1.12f, 0.55f), new Vector3(0.06f, 0.10f, 2.12f), woodTex, oakTrimCol);
            CreateTexturedBox(parent, "StudyWindowMullionV", new Vector3(4.95f, 2.05f, 0.55f), new Vector3(0.04f, 1.84f, 0.06f), woodTex, oakTrimCol);
            CreateTexturedBox(parent, "StudyWindowMullionH", new Vector3(4.93f, 2.05f, 0.55f), new Vector3(0.04f, 0.05f, 1.94f), woodTex, oakTrimCol);
            CreatePrimitiveBox(parent, "StudyCurtainBack", new Vector3(4.86f, 2.02f, 1.56f), new Vector3(0.14f, 2.10f, 0.36f), yellowCurtainCol);
            CreatePrimitiveBox(parent, "StudyCurtainFront", new Vector3(4.86f, 2.02f, -0.46f), new Vector3(0.14f, 2.10f, 0.36f), yellowCurtainCol);

            // Warm Headboard Wall Art & Soft Cream Bedroom Rug + Sunlight Patches on Floor
            CreateTexturedBox(parent, "BedroomWallArtFrame", new Vector3(0.05f, 2.32f, 3.03f), new Vector3(1.18f, 0.92f, 0.04f), woodTex, oakTrimCol);
            CreatePrimitiveBox(parent, "BedroomWallArtCanvas", new Vector3(0.05f, 2.32f, 2.99f), new Vector3(0.98f, 0.74f, 0.02f), new Color(0.98f, 0.95f, 0.88f));
            var bedArtQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bedArtQuad.name = "BedroomWallArtPicture";
            bedArtQuad.transform.SetParent(parent, false);
            bedArtQuad.transform.localPosition = new Vector3(0.05f, 2.32f, 2.97f);
            bedArtQuad.transform.localScale = new Vector3(0.68f, 0.68f, 1f);
            DestroyImmediate(bedArtQuad.GetComponent<Collider>());
            bedArtQuad.GetComponent<Renderer>().material = CreateSliceMaterial(YijingAssetLibrary.GetIconSlice("flower"));

            CreatePrimitiveBox(parent, "BedroomSoftRug", new Vector3(0.05f, 0.008f, 0.15f), new Vector3(2.85f, 0.014f, 2.65f), new Color(0.86f, 0.81f, 0.73f));
            CreatePrimitiveBox(parent, "BedroomSunPatchFloor", new Vector3(-0.85f, 0.005f, -1.15f), new Vector3(1.45f, 0.006f, 0.95f), new Color(0.99f, 0.94f, 0.78f));

            var scene = YijingAssetLibrary.Scenes[3];

            // 1. bd-0: 实木双门书柜 (Everyday Warm Wooden Double-Door Cabinet facing camera at rotY=180)
            var item0 = CreateInteractiveTargetRoot(parent, scene.annotations[0], new Vector3(1.4f, 2.1f, 1.0f));
            float bg0 = -scene.annotations[0].worldLocalPos.y;
            SpawnOpenSourceFBX(item0.transform, "ph_painted_wooden_cabinet", new Vector3(0f, bg0, 0f), 1.88f, 180f, 1.15f);

            // 2. bd-1: 开放藏书架 (Open Multi-Shelf Wooden Bookcase facing camera + Stocked with Rows of Books)
            var item1 = CreateInteractiveTargetRoot(parent, scene.annotations[1], new Vector3(1.4f, 2.1f, 1.0f));
            float bg1 = -scene.annotations[1].worldLocalPos.y;
            SpawnOpenSourceFBX(item1.transform, "ph_wooden_bookshelf_worn", new Vector3(0f, bg1, 0f), 1.82f, 180f, 1.10f);
            SpawnOpenSourceFBX(item1.transform, "ph_book_encyclopedia_set_01", new Vector3(-0.14f, bg1 + 0.94f, 0.02f), 0.25f, 0f, 0.42f);
            SpawnOpenSourceFBX(item1.transform, "ph_decorative_book_set_01", new Vector3(0.18f, bg1 + 0.94f, 0.02f), 0.24f, 0f, 0.32f);
            SpawnOpenSourceFBX(item1.transform, "ph_book_encyclopedia_set_01", new Vector3(0.02f, bg1 + 1.32f, 0.02f), 0.25f, 0f, 0.44f);

            // 3. bd-2: 复古落地钟 (Photorealistic Scanned Vintage Grandfather Clock against Back Wall)
            var item2 = CreateInteractiveTargetRoot(parent, scene.annotations[2], new Vector3(1.0f, 2.1f, 1.0f));
            float bg2 = -scene.annotations[2].worldLocalPos.y;
            SpawnOpenSourceFBX(item2.transform, "ph_vintage_grandfather_clock_01", new Vector3(0f, bg2, 0f), 1.92f, -10f);

            // 4. bd-3: 床头台灯柜 (Classic Wooden Nightstand right beside the Bed + Articulated Lamp & Alarm Clock sitting flush on top)
            var item3 = CreateInteractiveTargetRoot(parent, scene.annotations[3], new Vector3(1.1f, 1.3f, 1.1f));
            float bg3 = -scene.annotations[3].worldLocalPos.y;
            var nightStand = SpawnOpenSourceFBX(item3.transform, "ph_ClassicNightstand_01", new Vector3(0f, bg3, 0f), 0.66f, 0f);
            float nsTopY = GetRenderedTopLocalY(item3.transform, nightStand, bg3 + 0.66f);
            SpawnOpenSourceFBX(item3.transform, "ph_desk_lamp_arm_01", new Vector3(-0.06f, nsTopY, 0f), 0.46f, 15f);
            SpawnOpenSourceFBX(item3.transform, "ph_alarm_clock_01", new Vector3(0.14f, nsTopY, 0.04f), 0.16f, 0f);

            // 5. bd-4: 阳光双人床 (Warm Modern Solid-Oak Double Bed with White Duvet, Plump White Sleeping Pillows, Throw Pillows & Open Book!)
            var item4 = CreateInteractiveTargetRoot(parent, scene.annotations[4], new Vector3(2.1f, 1.2f, 2.3f));
            float bg4 = -scene.annotations[4].worldLocalPos.y;
            Color bedOakCol = new Color(0.68f, 0.46f, 0.28f);
            CreateTexturedBox(item4.transform, "BedOakHeadboard", new Vector3(0f, bg4 + 0.68f, 1.02f), new Vector3(1.76f, 0.96f, 0.10f), woodTex, bedOakCol);
            CreateTexturedBox(item4.transform, "BedOakPlatformFrame", new Vector3(0f, bg4 + 0.24f, 0f), new Vector3(1.76f, 0.22f, 2.12f), woodTex, bedOakCol);
            CreateTexturedBox(item4.transform, "BedLegFL", new Vector3(-0.80f, bg4 + 0.08f, -0.98f), new Vector3(0.10f, 0.16f, 0.10f), woodTex, bedOakCol);
            CreateTexturedBox(item4.transform, "BedLegFR", new Vector3(0.80f, bg4 + 0.08f, -0.98f), new Vector3(0.10f, 0.16f, 0.10f), woodTex, bedOakCol);
            CreateTexturedBox(item4.transform, "BedLegBL", new Vector3(-0.80f, bg4 + 0.08f, 0.98f), new Vector3(0.10f, 0.16f, 0.10f), woodTex, bedOakCol);
            CreateTexturedBox(item4.transform, "BedLegBR", new Vector3(0.80f, bg4 + 0.08f, 0.98f), new Vector3(0.10f, 0.16f, 0.10f), woodTex, bedOakCol);
            // Plump white quilted mattress & sunlit duvet cover + warm caramel throw blanket at foot + 2 plump white sleeping pillows
            CreatePrimitiveBox(item4.transform, "BedWhiteMattress", new Vector3(0f, bg4 + 0.46f, 0.0f), new Vector3(1.66f, 0.26f, 2.02f), new Color(0.96f, 0.96f, 0.94f));
            CreatePrimitiveBox(item4.transform, "BedSunlitDuvet", new Vector3(0f, bg4 + 0.595f, -0.16f), new Vector3(1.68f, 0.04f, 1.56f), new Color(0.99f, 0.97f, 0.90f));
            CreatePrimitiveBox(item4.transform, "BedFootThrowBlanket", new Vector3(0f, bg4 + 0.605f, -0.72f), new Vector3(1.70f, 0.045f, 0.42f), new Color(0.82f, 0.68f, 0.50f));
            CreatePrimitiveBox(item4.transform, "BedWhitePillowL", new Vector3(-0.42f, bg4 + 0.65f, 0.80f), new Vector3(0.62f, 0.12f, 0.34f), new Color(0.98f, 0.98f, 0.96f));
            CreatePrimitiveBox(item4.transform, "BedWhitePillowR", new Vector3(0.42f, bg4 + 0.65f, 0.80f), new Vector3(0.62f, 0.12f, 0.34f), new Color(0.98f, 0.98f, 0.96f));
            SpawnOpenSourceFBX(item4.transform, "ph_throw_pillows_01", new Vector3(-0.42f, bg4 + 0.60f, 0.60f), 0.24f, 0f);
            SpawnOpenSourceFBX(item4.transform, "ph_throw_pillows_01", new Vector3(0.42f, bg4 + 0.60f, 0.60f), 0.24f, 180f);
            SpawnOpenSourceFBX(item4.transform, "ph_decorative_book_set_01", new Vector3(-0.22f, bg4 + 0.61f, -0.12f), 0.12f, 25f);

            // 6. bd-5: 书桌笔记本 (0.74m Warm Oak Study Desk by Right Window + Open 3D Laptop + Books & Potted Plant sitting flush on desktop!)
            var item5 = CreateInteractiveTargetRoot(parent, scene.annotations[5], new Vector3(1.5f, 1.4f, 1.3f));
            float bg5 = -scene.annotations[5].worldLocalPos.y;
            Color deskOakCol = new Color(0.72f, 0.50f, 0.30f);
            CreateTexturedBox(item5.transform, "StudyDeskTabletop", new Vector3(0f, bg5 + 0.72f, 0f), new Vector3(0.82f, 0.05f, 1.32f), woodTex, deskOakCol);
            CreateTexturedBox(item5.transform, "StudyDeskApronDrawer", new Vector3(0f, bg5 + 0.62f, 0f), new Vector3(0.74f, 0.14f, 1.22f), woodTex, new Color(0.64f, 0.44f, 0.26f));
            CreateTexturedBox(item5.transform, "StudyDeskLegFL", new Vector3(-0.34f, bg5 + 0.35f, -0.58f), new Vector3(0.06f, 0.70f, 0.06f), woodTex, deskOakCol);
            CreateTexturedBox(item5.transform, "StudyDeskLegFR", new Vector3(0.34f, bg5 + 0.35f, -0.58f), new Vector3(0.06f, 0.70f, 0.06f), woodTex, deskOakCol);
            CreateTexturedBox(item5.transform, "StudyDeskLegBL", new Vector3(-0.34f, bg5 + 0.35f, 0.58f), new Vector3(0.06f, 0.70f, 0.06f), woodTex, deskOakCol);
            CreateTexturedBox(item5.transform, "StudyDeskLegBR", new Vector3(0.34f, bg5 + 0.35f, 0.58f), new Vector3(0.06f, 0.70f, 0.06f), woodTex, deskOakCol);
            float deskSurfaceY = bg5 + 0.745f;
            SpawnOpenSourceFBX(item5.transform, "ph_classic_laptop", new Vector3(-0.04f, deskSurfaceY, -0.04f), 0.24f, -110f);
            SpawnOpenSourceFBX(item5.transform, "ph_decorative_book_set_01", new Vector3(0.06f, deskSurfaceY, 0.36f), 0.18f, -90f);
            SpawnOpenSourceFBX(item5.transform, "ph_potted_plant_04", new Vector3(0.12f, deskSurfaceY, -0.42f), 0.36f, 0f);

            // 7. bd-6: 花几与座钟 (Carved Wooden Stand + Vintage Mantel Clock & Porcelain Vase sitting flush on top)
            var item6 = CreateInteractiveTargetRoot(parent, scene.annotations[6], new Vector3(1.1f, 1.2f, 1.1f));
            float bg6 = -scene.annotations[6].worldLocalPos.y;
            var flowerStand = SpawnOpenSourceFBX(item6.transform, "ph_chinese_stool", new Vector3(0f, bg6, 0f), 0.52f, 0f);
            float standTopY = GetRenderedTopLocalY(item6.transform, flowerStand, bg6 + 0.50f);
            SpawnOpenSourceFBX(item6.transform, "ph_mantel_clock_01", new Vector3(-0.08f, standTopY, 0f), 0.26f, 25f);
            SpawnOpenSourceFBX(item6.transform, "ph_antique_ceramic_vase_01", new Vector3(0.14f, standTopY, 0.06f), 0.40f, 0f);

            // 8. bd-8: 书房转角椅 (Realistic Wooden Study Desk Chair facing the Study Desk)
            var item7 = CreateInteractiveTargetRoot(parent, scene.annotations[7], new Vector3(1.1f, 1.1f, 1.1f));
            float bg7 = -scene.annotations[7].worldLocalPos.y;
            SpawnOpenSourceFBX(item7.transform, "ph_dining_chair_02", new Vector3(0f, bg7, 0f), 0.86f, 55f);
        }

        private static float GetRenderedTopLocalY(Transform parent, GameObject spawnedObj, float fallbackTopY)
        {
            if (spawnedObj == null || parent == null) return fallbackTopY;
            var renderers = spawnedObj.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return fallbackTopY;
            float maxWorldY = float.MinValue;
            for (int i = 0; i < renderers.Length; i++)
            {
                maxWorldY = Mathf.Max(maxWorldY, renderers[i].bounds.max.y);
            }
            return parent.InverseTransformPoint(new Vector3(parent.position.x, maxWorldY, parent.position.z)).y;
        }

        private GameObject CreateInteractiveTargetRoot(Transform sceneParent, MemoryAnnotationDef item, Vector3 colliderSize)
        {
            var go = new GameObject("TargetItem_" + item.id + "_" + item.label);
            go.transform.SetParent(sceneParent, false);
            go.transform.localPosition = item.worldLocalPos;

            var col = go.AddComponent<BoxCollider>();
            col.size = colliderSize;

            // Subtle Base Pad under floor-standing objects (omitted for wall-mounted windows)
            Renderer ringRenderer = null;
            Color padColor = new Color(0.86f, 0.76f, 0.48f, 1f);
            if (item.propKind != "sunlit_window")
            {
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.name = "ItemBasePad";
                ring.transform.SetParent(go.transform, false);
                ring.transform.localPosition = new Vector3(0f, -item.worldLocalPos.y + 0.015f, 0f);
                ring.transform.localScale = new Vector3(Mathf.Min(1.20f, colliderSize.x * 0.68f), 0.012f, Mathf.Min(1.20f, colliderSize.z * 0.68f));
                var rCol = ring.GetComponent<Collider>();
                if (rCol != null) DestroyImmediate(rCol);
                ringRenderer = ring.GetComponent<Renderer>();
                ringRenderer.material = CreateLitMaterial(null, padColor);
            }

            var interact = go.AddComponent<Yijing3DInteractable>();
            interact.kind = "memory_item";
            interact.optionId = item.id;
            interact.zoneIndex = item.zone;
            interact.label = item.label;
            interact.highlightRenderer = ringRenderer;
            interact.baseColor = padColor;

            _memoryItemObjects[item.id] = go;
            return go;
        }

        private void BuildDetailedTeaSet3D(Transform parent, Vector3 localPos)
        {
            SpawnOpenSourceFBX(parent, "ph_tea_set_01", localPos, 0.22f, 0f, 0.50f);
        }

        // ====================================================================
        // OPEN-SOURCE FBX SPAWNER WITH AUTOMATIC BOUNDS CENTERING & PHOTOREALISTIC PBR MATERIALS
        // ====================================================================
        private GameObject SpawnOpenSourceFBX(Transform parent, string resourceModelName, Vector3 bottomCenterLocalPos, float targetHeight, float rotY, float uniformWidthFloor = 0f)
        {
            GameObject prefab = YijingAssetLibrary.LoadModelPrefab(resourceModelName);
            var wrapper = new GameObject(resourceModelName + "_Root");
            wrapper.transform.SetParent(parent, false);
            wrapper.transform.localPosition = bottomCenterLocalPos;
            wrapper.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);

            if (prefab != null)
            {
                var normContainer = new GameObject("NormalizedModel");
                normContainer.transform.SetParent(wrapper.transform, false);
                normContainer.transform.localPosition = Vector3.zero;
                normContainer.transform.localRotation = Quaternion.identity;
                normContainer.transform.localScale = Vector3.one;

                // Preserve prefab's imported FBX localPosition, localRotation (-90 X on single-mesh FBX), and localScale!
                var inst = Instantiate(prefab, normContainer.transform, false);
                inst.name = resourceModelName;
                inst.transform.localPosition = prefab.transform.localPosition;
                inst.transform.localRotation = prefab.transform.localRotation;
                inst.transform.localScale = prefab.transform.localScale;

                ApplyOpenSourceMaterialsToFBX(inst, resourceModelName);

                // Compute combined world bounds while wrapper is at origin/identity to auto-center & auto-scale
                Quaternion savedRot = wrapper.transform.rotation;
                Vector3 savedPos = wrapper.transform.position;
                wrapper.transform.position = Vector3.zero;
                wrapper.transform.rotation = Quaternion.identity;

                var renderers = inst.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    Bounds b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                    {
                        b.Encapsulate(renderers[i].bounds);
                    }

                    float rawH = Mathf.Max(0.02f, b.size.y);
                    float rawW = Mathf.Max(0.02f, Mathf.Max(b.size.x, b.size.z));
                    float scaleFactor = targetHeight / rawH;
                    if (uniformWidthFloor > 0.01f)
                    {
                        scaleFactor = uniformWidthFloor / rawW;
                    }
                    else if (rawW * scaleFactor > targetHeight * 1.85f)
                    {
                        scaleFactor = (targetHeight * 1.85f) / rawW;
                    }

                    normContainer.transform.localScale = Vector3.one * scaleFactor;
                    Vector3 scaledCenter = b.center * scaleFactor;
                    float scaledMinY = b.min.y * scaleFactor;
                    normContainer.transform.localPosition = new Vector3(-scaledCenter.x, -scaledMinY, -scaledCenter.z);
                }

                wrapper.transform.position = savedPos;
                wrapper.transform.rotation = savedRot;
            }
            else
            {
                CreatePrimitiveBox(wrapper.transform, resourceModelName + "_Fallback", new Vector3(0f, targetHeight * 0.5f, 0f), new Vector3(targetHeight * 0.6f, targetHeight, targetHeight * 0.6f), new Color(0.68f, 0.48f, 0.32f));
            }

            return wrapper;
        }

        private static void ApplyOpenSourceMaterialsToFBX(GameObject root, string resourceModelName = "")
        {
            bool isPolyHaven = !string.IsNullOrEmpty(resourceModelName) && resourceModelName.StartsWith("ph_");
            string rawPhId = isPolyHaven ? resourceModelName.Substring(3) : "";

            Texture2D phPrimaryDiff = isPolyHaven
                ? (YijingAssetLibrary.LoadModelTexture("ph_" + rawPhId + "_alpha") ?? YijingAssetLibrary.LoadPolyHavenDiffTexture(resourceModelName))
                : null;
            Texture2D woodTex = YijingAssetLibrary.LoadModelTexture("RealWoodFloor") ?? YijingAssetLibrary.GetProceduralTexture("WoodPlanks");
            Texture2D foliageTex = YijingAssetLibrary.GetProceduralTexture("FoliageGreen");
            Texture2D sofaTex = YijingAssetLibrary.GetProceduralTexture("SofaFabric");
            Texture2D stoneTex = YijingAssetLibrary.LoadModelTexture("RealStoneFloor") ?? YijingAssetLibrary.GetProceduralTexture("StonePaving");

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var shared = r.sharedMaterials;
                if (shared == null || shared.Length == 0) continue;

                string rName = r.name.ToLowerInvariant();
                Material[] newMats = new Material[shared.Length];
                for (int i = 0; i < shared.Length; i++)
                {
                    string mName = shared[i] != null ? shared[i].name.ToLowerInvariant() : "";
                    string combinedName = mName + "_" + rName;
                    Texture2D existingTex = (shared[i] != null && shared[i].mainTexture is Texture2D t2d) ? t2d : null;

                    if (isPolyHaven)
                    {
                        Texture2D subTex = existingTex;
                        bool isAlphaCutoutLeaf = false;
                        float smoothness = 0.34f;
                        Color matTint = Color.white;

                        if (combinedName.Contains("leaf") || combinedName.Contains("leaves") || combinedName.Contains("twig") || combinedName.Contains("needle") || combinedName.Contains("fern") || combinedName.Contains("shrub") || combinedName.Contains("flower"))
                        {
                            subTex = YijingAssetLibrary.LoadModelTexture("ph_" + rawPhId + "_alpha")
                                  ?? YijingAssetLibrary.LoadModelTexture(rawPhId + "_leaves_diff_1k")
                                  ?? YijingAssetLibrary.LoadModelTexture(rawPhId + "_twig_diff_1k")
                                  ?? existingTex
                                  ?? phPrimaryDiff;
                            isAlphaCutoutLeaf = true;
                            smoothness = 0.22f;
                            if (rawPhId.Contains("pine_tree"))
                            {
                                matTint = new Color(0.76f, 0.96f, 0.72f);
                            }
                        }
                        else if (combinedName.Contains("bark") || combinedName.Contains("branch") || combinedName.Contains("trunk"))
                        {
                            subTex = YijingAssetLibrary.LoadModelTexture(rawPhId + "_bark_diff_1k")
                                  ?? YijingAssetLibrary.LoadModelTexture(rawPhId + "_branch_diff_1k")
                                  ?? YijingAssetLibrary.LoadModelTexture(rawPhId + "_trunk_a_diff_1k")
                                  ?? existingTex
                                  ?? phPrimaryDiff;
                            smoothness = 0.16f;
                        }
                        else if (combinedName.Contains("pot"))
                        {
                            subTex = YijingAssetLibrary.LoadModelTexture(rawPhId + "_pot_diff_1k")
                                  ?? existingTex
                                  ?? phPrimaryDiff;
                            smoothness = 0.38f;
                        }
                        else if (combinedName.Contains("paper"))
                        {
                            subTex = YijingAssetLibrary.LoadModelTexture(rawPhId + "_paper_diff_1k")
                                  ?? existingTex
                                  ?? phPrimaryDiff;
                            smoothness = 0.18f;
                        }
                        else
                        {
                            subTex = existingTex ?? phPrimaryDiff;
                        }

                        if (rawPhId.Contains("fern") || rawPhId.Contains("flower") || rawPhId.Contains("shrub"))
                        {
                            isAlphaCutoutLeaf = true;
                        }
                        if (rawPhId.Contains("tea_set") || rawPhId.Contains("vase") || rawPhId.Contains("Lantern") || rawPhId.Contains("Television") || rawPhId.Contains("laptop") || rawPhId.Contains("clock"))
                        {
                            smoothness = 0.68f;
                        }
                        if ((rawPhId == "sofa_03" || rawPhId == "Sofa_01") && !combinedName.Contains("wood") && !combinedName.Contains("leg"))
                        {
                            matTint = new Color(0.68f, 0.85f, 0.96f); // Soft light-blue upholstery matching 浅蓝长沙发
                        }
                        else if (rawPhId == "painted_wooden_cabinet" || rawPhId == "wooden_bookshelf_worn")
                        {
                            matTint = new Color(0.92f, 0.78f, 0.62f); // Warm honey-oak wood finish
                        }

                        Material phMat = CreateLitMaterial(subTex ?? woodTex, subTex != null ? matTint : new Color(0.76f, 0.56f, 0.38f));
                        if (phMat.HasProperty("_Glossiness")) phMat.SetFloat("_Glossiness", smoothness);

                        if (isAlphaCutoutLeaf && phMat.HasProperty("_Mode"))
                        {
                            phMat.SetFloat("_Mode", 1f);
                            phMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                            phMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                            phMat.SetInt("_ZWrite", 1);
                            phMat.EnableKeyword("_ALPHATEST_ON");
                            phMat.DisableKeyword("_ALPHABLEND_ON");
                            phMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                            phMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                            if (phMat.HasProperty("_Cutoff")) phMat.SetFloat("_Cutoff", 0.32f);
                        }

                        newMats[i] = phMat;
                        continue;
                    }

                    Color col = new Color(0.80f, 0.66f, 0.48f);
                    Texture2D tex = existingTex;

                    if (mName.Contains("wooddark") || mName.Contains("barkdark"))
                    {
                        col = new Color(0.52f, 0.34f, 0.20f);
                        tex = woodTex;
                    }
                    else if (mName.Contains("woodbirch") || mName.Contains("woodinner"))
                    {
                        col = new Color(0.86f, 0.74f, 0.58f);
                        tex = woodTex;
                    }
                    else if (mName.Contains("wood") || mName.Contains("bark") || mName.Contains("dirt"))
                    {
                        col = new Color(0.72f, 0.48f, 0.30f);
                        tex = woodTex;
                    }
                    else if (mName.Contains("leafsfall") || mName.Contains("pumpkin"))
                    {
                        col = new Color(0.88f, 0.48f, 0.16f);
                        tex = foliageTex;
                    }
                    else if (mName.Contains("leafs") || mName.Contains("plant") || mName.Contains("grass") || mName.Contains("bamboo") || mName.Contains("lily") || mName.Contains("melon"))
                    {
                        col = new Color(0.28f, 0.58f, 0.30f);
                        tex = foliageTex;
                    }
                    else if (mName.Contains("carpetblue"))
                    {
                        col = new Color(0.38f, 0.56f, 0.74f);
                        tex = sofaTex;
                    }
                    else if (mName.Contains("carpetwhite") || mName.Contains("colorwhite"))
                    {
                        col = new Color(0.88f, 0.86f, 0.82f);
                    }
                    else if (mName.Contains("carpet"))
                    {
                        col = new Color(0.76f, 0.38f, 0.32f);
                        tex = sofaTex;
                    }
                    else if (mName.Contains("stone") || mName.Contains("rock"))
                    {
                        col = new Color(0.72f, 0.72f, 0.70f);
                        tex = stoneTex;
                    }
                    else if (mName.Contains("colorpurple"))
                    {
                        col = new Color(0.58f, 0.42f, 0.80f);
                    }
                    else if (mName.Contains("colorred"))
                    {
                        col = new Color(0.82f, 0.26f, 0.24f);
                    }
                    else if (mName.Contains("coloryellow") || mName.Contains("corn") || mName.Contains("lamp"))
                    {
                        col = new Color(0.92f, 0.74f, 0.24f);
                    }
                    else if (mName.Contains("fur") || mName.Contains("tan"))
                    {
                        col = new Color(0.68f, 0.46f, 0.28f);
                    }
                    else if (mName.Contains("glass") || mName.Contains("water"))
                    {
                        col = new Color(0.68f, 0.82f, 0.90f);
                    }
                    else if (mName.Contains("metaldark"))
                    {
                        col = new Color(0.26f, 0.30f, 0.34f);
                    }
                    else if (mName.Contains("metal"))
                    {
                        col = new Color(0.74f, 0.78f, 0.80f);
                    }
                    else if (shared[i] != null && shared[i].HasProperty("_Color"))
                    {
                        col = shared[i].color;
                    }

                    newMats[i] = CreateLitMaterial(tex, col);
                }
                r.sharedMaterials = newMats;
            }
        }

        // ====================================================================
        // DIRECT ON-OBJECT 3D SELECTION MARKERS (直接标在 3D 物品上的精巧锚点 — 无重复3D文字牌!)
        // ====================================================================
        public void ShowMemoryStage(string theme, string textureName)
        {
            targetCameraPos = memoryStageRoot.position + new Vector3(0f, 2.55f, -6.2f);
            targetCameraLookAt = memoryStageRoot.position + new Vector3(0f, 1.05f, 0.8f);

            if (courtyardGroup != null) courtyardGroup.SetActive(theme == "courtyard" || string.IsNullOrEmpty(theme));
            if (livingGroup != null) livingGroup.SetActive(theme == "living");
            if (bedroomGroup != null) bedroomGroup.SetActive(theme == "bedroom");
            if (ruralGroup != null) ruralGroup.SetActive(theme == "rural");

            // Show default preview markers on the active scene's items when browsing
            var scene = YijingAssetLibrary.Scenes.Find(s => s.theme == theme) ?? YijingAssetLibrary.Scenes[0];
            SetMemoryObserveMarkers(scene.annotations, true);
        }

        public void SetMemoryObserveMarkers(List<MemoryAnnotationDef> items, bool visible)
        {
            SafeDestroyChildren(memoryMarkersRoot);
            if (!visible || items == null) return;

            for (int i = 0; i < items.Count; i++)
            {
                SpawnSingleOnObjectMarker3D(items[i], i + 1, false, false);
            }
        }

        public void SetMemoryQuestionMarkers(List<MemoryAnnotationDef> allSceneItems, string currentTargetId, bool highlightTargetHint)
        {
            SafeDestroyChildren(memoryMarkersRoot);
            if (allSceneItems == null) return;

            for (int i = 0; i < allSceneItems.Count; i++)
            {
                var item = allSceneItems[i];
                bool isHintTarget = highlightTargetHint && item.id == currentTargetId;
                SpawnSingleOnObjectMarker3D(item, i + 1, true, isHintTarget);
            }
        }

        private void SpawnSingleOnObjectMarker3D(MemoryAnnotationDef item, int displayNumber, bool isQuestionPhase, bool isHintTarget)
        {
            var markerGo = new GameObject("OnObjectMarker3D_" + displayNumber + "_" + item.label);
            markerGo.transform.SetParent(memoryMarkersRoot, false);
            markerGo.transform.localPosition = item.worldLocalPos;

            Vector3 pinOffset = item.markerOffset.sqrMagnitude > 0.01f ? item.markerOffset : new Vector3(0f, 0.72f, 0f);

            Color primaryPinColor = isHintTarget
                ? new Color(0.12f, 0.86f, 0.36f)
                : (isQuestionPhase ? new Color(0.92f, 0.50f, 0.12f) : new Color(0.16f, 0.56f, 0.40f));

            Color haloColor = isHintTarget
                ? new Color(0.98f, 0.92f, 0.24f)
                : new Color(0.92f, 0.78f, 0.28f);

            // 1. Sleek Object Surface Anchor Dot
            var anchorDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            anchorDot.name = "ObjectCenterAnchorDot";
            anchorDot.transform.SetParent(markerGo.transform, false);
            anchorDot.transform.localPosition = Vector3.zero;
            anchorDot.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
            DestroyImmediate(anchorDot.GetComponent<Collider>());
            anchorDot.GetComponent<Renderer>().material = CreateLitMaterial(null, haloColor);

            // 2. Slim Vertical Pointer Stem connecting object surface to floating beacon
            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = "PointerStem";
            stem.transform.SetParent(markerGo.transform, false);
            stem.transform.localPosition = pinOffset * 0.5f;
            stem.transform.localScale = new Vector3(0.022f, pinOffset.magnitude * 0.5f, 0.022f);
            DestroyImmediate(stem.GetComponent<Collider>());
            stem.GetComponent<Renderer>().material = CreateLitMaterial(null, haloColor);

            // 3. Compact 3D Pin Head Beacon above the object (NO 3D text signboard so UI badges never overlap!)
            var pinHead = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pinHead.name = "PinHeadSphere";
            pinHead.transform.SetParent(markerGo.transform, false);
            pinHead.transform.localPosition = pinOffset;
            pinHead.transform.localScale = isHintTarget ? new Vector3(0.24f, 0.24f, 0.24f) : new Vector3(0.16f, 0.16f, 0.16f);
            DestroyImmediate(pinHead.GetComponent<Collider>());
            var pinRenderer = pinHead.GetComponent<Renderer>();
            pinRenderer.material = CreateLitMaterial(null, primaryPinColor);

            // 4. Make the 3D Marker Pin directly clickable in 3D space
            var col = markerGo.AddComponent<BoxCollider>();
            col.center = pinOffset * 0.65f;
            col.size = new Vector3(0.85f, pinOffset.y + 0.55f, 0.65f);

            var interact = markerGo.AddComponent<Yijing3DInteractable>();
            interact.kind = "memory_item";
            interact.optionId = item.id;
            interact.zoneIndex = item.zone;
            interact.label = item.label;
            interact.highlightRenderer = pinRenderer;
            interact.baseColor = primaryPinColor;
        }

        // ====================================================================
        // STAGE 2: 注意力追踪 3D 阳光活动室展台与动态轨道建模
        // ====================================================================
        private void BuildAttentionTrackingStage(Transform root)
        {
            Texture2D woodTex = YijingAssetLibrary.LoadModelTexture("RealWoodFloor") ?? YijingAssetLibrary.GetProceduralTexture("WoodPlanks");
            Texture2D wallTex = YijingAssetLibrary.GetProceduralTexture("WhiteCourtyardWall");

            var floor = CreateTexturedBox(root, "StageFloor", new Vector3(0f, -0.12f, 0.5f), new Vector3(13f, 0.24f, 9f), woodTex, new Color(0.86f, 0.72f, 0.56f));
            floor.GetComponent<Renderer>().material.mainTextureScale = new Vector2(6f, 4f);
            CreateTexturedBox(root, "GardenBackdropWall", new Vector3(0f, 2.3f, 3.8f), new Vector3(12f, 5.2f, 0.25f), wallTex, new Color(0.92f, 0.89f, 0.83f));
            CreatePrimitiveBox(root, "AttentionLeftWall", new Vector3(-5.9f, 2.3f, 0.6f), new Vector3(0.25f, 5.2f, 6.6f), new Color(0.90f, 0.87f, 0.81f));
            CreatePrimitiveBox(root, "AttentionRightWall", new Vector3(5.9f, 2.3f, 0.6f), new Vector3(0.25f, 5.2f, 6.6f), new Color(0.90f, 0.87f, 0.81f));
            CreatePrimitiveBox(root, "AttentionCeilingSlab", new Vector3(0f, 4.82f, 0.6f), new Vector3(12.2f, 0.16f, 6.8f), new Color(0.96f, 0.95f, 0.92f));
            CreateTexturedBox(root, "BackWallWainscot", new Vector3(0f, 0.45f, 3.62f), new Vector3(11.5f, 0.90f, 0.05f), woodTex, new Color(0.62f, 0.42f, 0.26f));
            AddWarmIndoorFillLight(root, "AttentionWarmFillLight", new Vector3(0f, 3.85f, 0.50f), 0.78f);

            SpawnOpenSourceFBX(root, "ph_potted_plant_01", new Vector3(-4.5f, 0f, 1.8f), 1.35f, 15f);
            SpawnOpenSourceFBX(root, "ph_potted_plant_02", new Vector3(4.5f, 0f, 1.8f), 1.30f, -20f);
            SpawnOpenSourceFBX(root, "ph_chinese_stool", new Vector3(-4.6f, 0f, 0.2f), 0.52f, 0f);
            SpawnOpenSourceFBX(root, "ph_antique_ceramic_vase_01", new Vector3(-4.6f, 0.52f, 0.2f), 0.46f, 0f);

            CreateTexturedBox(root, "TargetBoardFrame", new Vector3(0f, 3.35f, 1.6f), new Vector3(2.2f, 1.55f, 0.18f), woodTex, new Color(0.45f, 0.30f, 0.18f));
            var targetQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            targetQuad.name = "TargetPlaqueQuad";
            targetQuad.transform.SetParent(root, false);
            targetQuad.transform.localPosition = new Vector3(0f, 3.35f, 1.49f);
            targetQuad.transform.localScale = new Vector3(1.35f, 1.35f, 1f);
            var tCol = targetQuad.GetComponent<Collider>();
            if (tCol != null) DestroyImmediate(tCol);
            attentionTargetPlaque = targetQuad.GetComponent<Renderer>();
            attentionTargetPlaque.material = CreateSliceMaterial(YijingAssetLibrary.GetIconSlice("apple"));

            attentionLanesRoot = new GameObject("AttentionLanes_3D").transform;
            attentionLanesRoot.SetParent(root, false);
        }

        public void ShowAttentionStage(string targetIcon, List<QuestionOptionRuntime> options, string difficulty)
        {
            targetCameraPos = attentionStageRoot.position + new Vector3(0f, 2.25f, -6.9f);
            targetCameraLookAt = attentionStageRoot.position + new Vector3(0f, 1.75f, 1.0f);

            if (attentionTargetPlaque != null && !string.IsNullOrEmpty(targetIcon))
            {
                attentionTargetPlaque.material = CreateSliceMaterial(YijingAssetLibrary.GetIconSlice(targetIcon));
            }

            _activeAttentionTokens.Clear();
            SafeDestroyChildren(attentionLanesRoot);
            if (options == null || options.Count == 0) return;

            Texture2D woodTex = YijingAssetLibrary.GetProceduralTexture("WoodPlanks");
            float cycleSpeed = difficulty == "hard" ? 3.0f : (difficulty == "normal" ? 4.5f : 6.0f);
            int count = options.Count;

            for (int i = 0; i < count; i++)
            {
                var opt = options[i];
                float laneY = Mathf.Lerp(2.05f, 0.35f, count > 1 ? i / (float)(count - 1) : 0.5f);
                float laneZ = 0.2f + (i % 2) * 0.25f;

                CreateTexturedBox(attentionLanesRoot, "TrackRail_" + i, new Vector3(0f, laneY - 0.35f, laneZ + 0.12f), new Vector3(8.2f, 0.08f, 0.18f), woodTex, new Color(0.62f, 0.44f, 0.28f));

                var tokenGo = new GameObject("Token3D_" + i + "_" + opt.label);
                tokenGo.transform.SetParent(attentionLanesRoot, false);
                float startX = Mathf.Lerp(-2.6f, 2.6f, (i % 3) / 2f);
                tokenGo.transform.localPosition = new Vector3(startX, laneY, laneZ);

                var frameGo = CreatePrimitiveBox(tokenGo.transform, "TokenFrame", Vector3.zero, new Vector3(0.96f, 0.96f, 0.12f), new Color(0.98f, 0.96f, 0.90f));
                var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quadGo.name = "TokenPicture";
                quadGo.transform.SetParent(tokenGo.transform, false);
                quadGo.transform.localPosition = new Vector3(0f, 0f, -0.07f);
                quadGo.transform.localScale = new Vector3(0.84f, 0.84f, 1f);
                var qCol = quadGo.GetComponent<Collider>();
                if (qCol != null) DestroyImmediate(qCol);
                quadGo.GetComponent<Renderer>().material = CreateSliceMaterial(YijingAssetLibrary.GetIconSlice(opt.icon));

                var col = tokenGo.AddComponent<BoxCollider>();
                col.size = new Vector3(1.05f, 1.05f, 0.3f);

                var interact = tokenGo.AddComponent<Yijing3DInteractable>();
                interact.kind = "attention_token";
                interact.optionId = opt.id;
                interact.label = opt.label;
                interact.laneIndex = i;
                interact.cycleSpeed = cycleSpeed;
                interact.phaseOffset = i * 1.15f;
                interact.laneMinX = -3.3f;
                interact.laneMaxX = 3.3f;
                interact.isMovingToken = true;
                interact.baseLocalPos = tokenGo.transform.localPosition;
                interact.highlightRenderer = frameGo.GetComponent<Renderer>();
                interact.baseColor = new Color(0.98f, 0.96f, 0.90f);

                _activeAttentionTokens.Add(interact);
            }
        }

        // ====================================================================
        // STAGE 3: 厨房步骤 3D 现代居家厨房实景建模 (FBX Kitchen Appliances + Household Details + Step Easels)
        // ====================================================================
        private void BuildKitchenStepsStage(Transform root)
        {
            Texture2D tileTex = YijingAssetLibrary.LoadModelTexture("FloorTile1") ?? YijingAssetLibrary.GetProceduralTexture("KitchenTile");
            Texture2D woodTex = YijingAssetLibrary.LoadModelTexture("RealWoodFloor") ?? YijingAssetLibrary.GetProceduralTexture("WoodPlanks");
            Texture2D wallTex = YijingAssetLibrary.GetProceduralTexture("WhiteCourtyardWall");

            var floor = CreateTexturedBox(root, "KitchenFloor", new Vector3(0f, -0.1f, 0.5f), new Vector3(13f, 0.2f, 9f), tileTex, Color.white);
            floor.GetComponent<Renderer>().material.mainTextureScale = new Vector2(6f, 4f);
            CreateTexturedBox(root, "KitchenBackWall", new Vector3(0f, 2.2f, 3.6f), new Vector3(12f, 4.6f, 0.25f), wallTex, new Color(0.94f, 0.91f, 0.85f));
            CreatePrimitiveBox(root, "KitchenLeftWall", new Vector3(-5.9f, 2.2f, 0.5f), new Vector3(0.25f, 4.6f, 6.4f), new Color(0.92f, 0.89f, 0.83f));
            CreatePrimitiveBox(root, "KitchenRightWall", new Vector3(5.9f, 2.2f, 0.5f), new Vector3(0.25f, 4.6f, 6.4f), new Color(0.92f, 0.89f, 0.83f));
            CreatePrimitiveBox(root, "KitchenCeilingSlab", new Vector3(0f, 4.46f, 0.5f), new Vector3(12.2f, 0.16f, 6.6f), new Color(0.96f, 0.95f, 0.92f));
            AddWarmIndoorFillLight(root, "KitchenWarmFillLight", new Vector3(0f, 3.65f, 0.40f), 0.78f);

            var appliancesRoot = new GameObject("KitchenAppliances_FBX").transform;
            appliancesRoot.SetParent(root, false);

            SpawnKitchenFBXOrFallback(appliancesRoot, "Sink", "CountertopTexturing", new Vector3(-3.6f, 0f, 2.4f), Vector3.one * 1.15f, new Color(0.84f, 0.86f, 0.88f));
            SpawnKitchenFBXOrFallback(appliancesRoot, "Countertop", "CountertopTexturing", new Vector3(-2.1f, 0f, 2.4f), Vector3.one * 1.15f, new Color(0.86f, 0.72f, 0.54f));
            SpawnKitchenFBXOrFallback(appliancesRoot, "ChoppingBoard", "ChoppingBoardTexture", new Vector3(-2.1f, 0.95f, 2.4f), Vector3.one * 1.15f, new Color(0.90f, 0.78f, 0.58f));
            SpawnKitchenFBXOrFallback(appliancesRoot, "Knife", "KnifeTexture", new Vector3(-1.8f, 0.98f, 2.3f), Vector3.one * 1.15f, Color.white);
            SpawnKitchenFBXOrFallback(appliancesRoot, "Hob", "HobTexture", new Vector3(-0.6f, 0f, 2.4f), Vector3.one * 1.15f, new Color(0.45f, 0.48f, 0.52f));
            SpawnKitchenFBXOrFallback(appliancesRoot, "CookingPot", "CountertopTexturing", new Vector3(-0.6f, 0.98f, 2.4f), Vector3.one * 1.1f, new Color(0.82f, 0.36f, 0.28f));
            SpawnKitchenFBXOrFallback(appliancesRoot, "Countertop", "CountertopTexturing", new Vector3(0.9f, 0f, 2.4f), Vector3.one * 1.15f, new Color(0.86f, 0.72f, 0.54f));
            SpawnKitchenFBXOrFallback(appliancesRoot, "Plate", "DishTray_Texture", new Vector3(0.9f, 0.98f, 2.4f), Vector3.one * 1.15f, Color.white);
            SpawnKitchenFBXOrFallback(appliancesRoot, "DeliverCountertop", "CountertopTexturing", new Vector3(2.5f, 0f, 2.4f), Vector3.one * 1.15f, new Color(0.78f, 0.66f, 0.50f));
            SpawnKitchenFBXOrFallback(appliancesRoot, "IngredientCrate", "IngredientCrate_Texture", new Vector3(4.0f, 0f, 2.2f), Vector3.one * 1.1f, new Color(0.82f, 0.65f, 0.44f));

            // Photorealistic Household Kitchen Utensils & Fresh Ingredients on Shelf and Counter
            CreateTexturedBox(root, "UpperKitchenShelf", new Vector3(-1.4f, 2.55f, 3.25f), new Vector3(5.2f, 0.10f, 0.42f), woodTex, new Color(0.68f, 0.46f, 0.28f));
            SpawnOpenSourceFBX(root, "ph_tea_set_01", new Vector3(-2.6f, 2.60f, 3.22f), 0.24f, 0f, 0.48f);
            SpawnOpenSourceFBX(root, "ph_ceramic_pot", new Vector3(-1.4f, 2.60f, 3.22f), 0.38f, 0f);
            SpawnOpenSourceFBX(root, "ph_potted_plant_04", new Vector3(-0.3f, 2.60f, 3.22f), 0.42f, 0f);
            SpawnOpenSourceFBX(root, "ph_wicker_basket_02", new Vector3(3.9f, 0.95f, 2.2f), 0.42f, 15f);
            SpawnOpenSourceFBX(root, "ph_food_apple_01", new Vector3(2.4f, 0.98f, 2.35f), 0.14f, 0f);

            var chefGo = SpawnKitchenFBXOrFallback(appliancesRoot, "Chef", "CountertopTexturing", new Vector3(0.1f, 0f, 1.55f), Vector3.one * 1.05f, new Color(0.95f, 0.95f, 0.92f));
            if (chefGo != null)
            {
                var headPrefab = YijingAssetLibrary.LoadModelPrefab("Chef_Model_Head");
                if (headPrefab != null)
                {
                    var headGo = Instantiate(headPrefab, chefGo.transform);
                    headGo.name = "ChefHead_3D";
                    headGo.transform.localPosition = new Vector3(0f, 0.75f, 0f);
                    ApplyUniformMaterialToHierarchy(headGo, null, new Color(0.96f, 0.88f, 0.78f));
                }
                chefGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                kitchenChefTransform = chefGo.transform;
            }

            kitchenEaselsRoot = new GameObject("KitchenStepEasels_3D").transform;
            kitchenEaselsRoot.SetParent(root, false);
        }

        private GameObject SpawnKitchenFBXOrFallback(Transform parent, string modelName, string texName, Vector3 localPos, Vector3 scale, Color fallbackTint)
        {
            GameObject prefab = YijingAssetLibrary.LoadModelPrefab(modelName);
            Texture2D tex = YijingAssetLibrary.LoadModelTexture(texName) ?? YijingAssetLibrary.GetProceduralTexture("WarmWood");
            GameObject instance;

            if (prefab != null)
            {
                instance = Instantiate(prefab, parent);
                instance.name = modelName + "_3D";
                instance.transform.localPosition = localPos;
                instance.transform.localScale = scale;
                ApplyUniformMaterialToHierarchy(instance, tex, fallbackTint);
            }
            else
            {
                instance = CreateTexturedBox(parent, modelName + "_Fallback", localPos + new Vector3(0f, 0.45f, 0f), new Vector3(1.1f, 0.9f, 1.0f), tex, fallbackTint);
            }
            return instance;
        }

        private static void ApplyUniformMaterialToHierarchy(GameObject root, Texture2D tex, Color tint)
        {
            Material mat = CreateLitMaterial(tex, tint);
            Material accentMat = CreateLitMaterial(null, new Color(0.85f, 0.87f, 0.88f));
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                int slotCount = Mathf.Max(1, r.sharedMaterials != null ? r.sharedMaterials.Length : 1);
                Material[] mats = new Material[slotCount];
                for (int i = 0; i < slotCount; i++)
                {
                    mats[i] = (i % 2 == 0) ? mat : accentMat;
                }
                r.sharedMaterials = mats;
            }
        }

        public void ShowKitchenStage(List<KitchenStepDef> steps, bool showOrderBadges)
        {
            targetCameraPos = kitchenStageRoot.position + new Vector3(0f, 2.35f, -6.5f);
            targetCameraLookAt = kitchenStageRoot.position + new Vector3(0f, 1.35f, 1.2f);

            SafeDestroyChildren(kitchenEaselsRoot);
            if (steps == null || steps.Count == 0) return;

            Texture2D woodTex = YijingAssetLibrary.GetProceduralTexture("WoodPlanks");
            int n = steps.Count;

            for (int i = 0; i < n; i++)
            {
                var step = steps[i];
                float x = Mathf.Lerp(-3.4f, 3.4f, n > 1 ? i / (float)(n - 1) : 0.5f);
                float z = -0.35f + (i % 2) * 0.18f;

                var easelGo = new GameObject("StepEasel3D_" + (i + 1) + "_" + step.label);
                easelGo.transform.SetParent(kitchenEaselsRoot, false);
                easelGo.transform.localPosition = new Vector3(x, 0.85f, z);
                easelGo.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);

                var board = CreateTexturedBox(easelGo.transform, "EaselBoard", Vector3.zero, new Vector3(1.12f, 1.18f, 0.1f), woodTex, new Color(0.82f, 0.62f, 0.40f));
                CreateTexturedBox(easelGo.transform, "EaselLeg", new Vector3(0f, -0.45f, 0.18f), new Vector3(0.14f, 0.95f, 0.1f), woodTex, new Color(0.62f, 0.42f, 0.25f));

                var photoQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                photoQuad.name = "StepPhotoQuad";
                photoQuad.transform.SetParent(easelGo.transform, false);
                photoQuad.transform.localPosition = new Vector3(0f, 0.04f, -0.06f);
                photoQuad.transform.localScale = new Vector3(0.98f, 0.98f, 1f);
                var pqCol = photoQuad.GetComponent<Collider>();
                if (pqCol != null) DestroyImmediate(pqCol);
                photoQuad.GetComponent<Renderer>().material = CreateSliceMaterial(YijingAssetLibrary.GetIconSlice(step.icon));

                if (showOrderBadges)
                {
                    var badge = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    badge.name = "StepOrderBadge";
                    badge.transform.SetParent(easelGo.transform, false);
                    badge.transform.localPosition = new Vector3(-0.42f, 0.46f, -0.1f);
                    badge.transform.localScale = new Vector3(0.26f, 0.26f, 0.12f);
                    var bCol = badge.GetComponent<Collider>();
                    if (bCol != null) DestroyImmediate(bCol);
                    badge.GetComponent<Renderer>().material = CreateLitMaterial(null, new Color(0.18f, 0.54f, 0.40f));
                }

                var col = easelGo.AddComponent<BoxCollider>();
                col.size = new Vector3(1.16f, 1.25f, 0.3f);

                var interact = easelGo.AddComponent<Yijing3DInteractable>();
                interact.kind = "kitchen_step";
                interact.optionId = step.id;
                interact.label = step.label;
                interact.highlightRenderer = board.GetComponent<Renderer>();
                interact.baseColor = new Color(0.82f, 0.62f, 0.40f);
            }
        }

        private static GameObject CreatePrimitiveBox(Transform parent, string name, Vector3 localPos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
            go.GetComponent<Renderer>().material = CreateLitMaterial(null, color);
            return go;
        }

        private static GameObject CreateTexturedBox(Transform parent, string name, Vector3 localPos, Vector3 scale, Texture2D tex, Color tint)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
            go.GetComponent<Renderer>().material = CreateLitMaterial(tex, tint);
            return go;
        }

        public static Material CreateLitMaterial(Texture2D tex, Color color)
        {
            Shader sh = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Unlit/Texture");
            Material mat = new Material(sh);
            mat.color = color;
            if (tex != null) mat.mainTexture = tex;
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.22f);
            return mat;
        }

        public static Material CreateUnlitTextureMaterial(Texture2D tex)
        {
            Shader sh = Shader.Find("Unlit/Texture") ?? Shader.Find("Standard");
            Material mat = new Material(sh);
            mat.color = Color.white;
            if (tex != null) mat.mainTexture = tex;
            return mat;
        }

        public static Material CreateSliceMaterial(IconAtlasSlice slice)
        {
            Material mat = CreateUnlitTextureMaterial(slice.sheetTexture);
            mat.mainTextureScale = new Vector2(slice.uvRect.width, slice.uvRect.height);
            mat.mainTextureOffset = new Vector2(slice.uvRect.x, slice.uvRect.y);
            return mat;
        }
    }
}
