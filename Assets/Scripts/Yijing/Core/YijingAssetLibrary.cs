using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Yijing.Data;

namespace Yijing.Core
{
    public struct IconAtlasSlice
    {
        public Texture2D sheetTexture;
        public Rect uvRect; // Normalized UV rect (x, y, width, height) in bottom-left origin
        public string iconKey;
    }

    public static class YijingAssetLibrary
    {
        private static TaskCatalogRoot _catalog;
        private static List<MemorySceneDef> _scenes;
        private static List<TrainingSessionRecord> _simulationRecords;
        private static readonly Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Texture2D> _proceduralTextures = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, IconAtlasSlice> _sliceMap = new Dictionary<string, IconAtlasSlice>();

        private static readonly HashSet<string> StandaloneGameIcons = new HashSet<string>
        {
            "apple", "cup", "teapot", "clock", "flower", "basket",
            "wash", "peel", "cut", "plate", "memory"
        };

        public static TaskCatalogRoot Catalog
        {
            get
            {
                if (_catalog == null) LoadCatalog();
                return _catalog;
            }
        }

        public static List<MemorySceneDef> Scenes
        {
            get
            {
                if (_scenes == null) BuildDefaultScenes();
                return _scenes;
            }
        }

        public static List<TrainingSessionRecord> SimulationRecords
        {
            get
            {
                if (_simulationRecords == null) LoadSimulationRecords();
                return _simulationRecords;
            }
        }

        public static void Initialize()
        {
            LoadCatalog();
            BuildDefaultScenes();
            BuildSliceMap();
        }

        private static void LoadCatalog()
        {
            TextAsset asset = Resources.Load<TextAsset>("Yijing/Data/task_catalog");
            if (asset != null && !string.IsNullOrEmpty(asset.text))
            {
                _catalog = JsonUtility.FromJson<TaskCatalogRoot>(asset.text);
            }
            if (_catalog == null)
            {
                _catalog = new TaskCatalogRoot();
            }
            BuildSliceMap();
        }

        private static void BuildSliceMap()
        {
            _sliceMap.Clear();
            if (_catalog == null) return;

            // 1. Standalone icons (1x1 full UV)
            foreach (string key in StandaloneGameIcons)
            {
                Texture2D tex = LoadGameSheetTexture(key);
                _sliceMap[key] = new IconAtlasSlice
                {
                    sheetTexture = tex,
                    uvRect = new Rect(0f, 0f, 1f, 1f),
                    iconKey = key
                };
            }

            // 2. Kitchen hygiene care sheet (2 cols x 1 row)
            Texture2D careSheet = LoadGameSheetTexture("kitchen-care");
            _sliceMap["care-wash"] = new IconAtlasSlice
            {
                sheetTexture = careSheet,
                uvRect = new Rect(0f, 0f, 0.5f, 1f),
                iconKey = "care-wash"
            };
            _sliceMap["care-clean"] = new IconAtlasSlice
            {
                sheetTexture = careSheet,
                uvRect = new Rect(0.5f, 0f, 0.5f, 1f),
                iconKey = "care-clean"
            };

            // 3. Attention themes (3 cols x 2 rows for non-standalone icons)
            if (_catalog.attention != null)
            {
                foreach (var task in _catalog.attention)
                {
                    if (task.items == null) continue;
                    for (int i = 0; i < task.items.Count; i++)
                    {
                        string key = task.items[i].icon;
                        if (StandaloneGameIcons.Contains(key)) continue;
                        Texture2D sheet = LoadGameSheetTexture(task.id);
                        int cols = 3;
                        int rows = 2;
                        int col = i % cols;
                        int rowFromTop = i / cols;
                        float w = 1f / cols;
                        float h = 1f / rows;
                        float u = col * w;
                        float v = (rows - 1 - rowFromTop) * h;
                        _sliceMap[key] = new IconAtlasSlice
                        {
                            sheetTexture = sheet,
                            uvRect = new Rect(u, v, w, h),
                            iconKey = key
                        };
                    }
                }
            }

            // 4. Kitchen tasks (2 cols x 2 rows for non-standalone icons)
            if (_catalog.kitchen != null)
            {
                foreach (var task in _catalog.kitchen)
                {
                    if (task.steps == null) continue;
                    for (int i = 0; i < task.steps.Count; i++)
                    {
                        string key = task.steps[i].icon;
                        if (StandaloneGameIcons.Contains(key)) continue;
                        Texture2D sheet = LoadGameSheetTexture(task.id);
                        int cols = 2;
                        int rows = 2;
                        int col = i % cols;
                        int rowFromTop = i / cols;
                        float w = 1f / cols;
                        float h = 1f / rows;
                        float u = col * w;
                        float v = (rows - 1 - rowFromTop) * h;
                        _sliceMap[key] = new IconAtlasSlice
                        {
                            sheetTexture = sheet,
                            uvRect = new Rect(u, v, w, h),
                            iconKey = key
                        };
                    }
                }
            }
        }

        public static IconAtlasSlice GetIconSlice(string iconKey)
        {
            if (_sliceMap.Count == 0) BuildSliceMap();
            if (!string.IsNullOrEmpty(iconKey) && _sliceMap.TryGetValue(iconKey, out var slice))
            {
                if (slice.sheetTexture == null)
                {
                    string sheetName = StandaloneGameIcons.Contains(iconKey) ? iconKey : iconKey.Split('-')[0];
                    if (iconKey == "care-wash" || iconKey == "care-clean") sheetName = "kitchen-care";
                    slice.sheetTexture = LoadGameSheetTexture(sheetName);
                    _sliceMap[iconKey] = slice;
                }
                return slice;
            }
            Texture2D fallback = LoadGameSheetTexture("apple");
            return new IconAtlasSlice
            {
                sheetTexture = fallback,
                uvRect = new Rect(0f, 0f, 1f, 1f),
                iconKey = iconKey ?? "apple"
            };
        }

        public static Texture2D LoadGameSheetTexture(string name)
        {
            if (string.IsNullOrEmpty(name)) name = "apple";
            string cacheKey = "game_" + name;
            if (_textureCache.TryGetValue(cacheKey, out var cached) && cached != null)
                return cached;

            Texture2D tex = Resources.Load<Texture2D>("Yijing/Games/" + name);
            if (tex != null)
            {
                _textureCache[cacheKey] = tex;
                return tex;
            }
            return GetProceduralTexture("WarmWood");
        }

        public static Texture2D LoadSceneTexture(string resourceName)
        {
            if (string.IsNullOrEmpty(resourceName)) resourceName = "first-courtyard";
            string cacheKey = "scene_" + resourceName;
            if (_textureCache.TryGetValue(cacheKey, out var cached) && cached != null)
                return cached;

            Texture2D tex = Resources.Load<Texture2D>("Yijing/Scenes/" + resourceName);
            if (tex != null)
            {
                _textureCache[cacheKey] = tex;
                return tex;
            }
            return GetProceduralTexture("WarmWood");
        }

        public static Texture2D LoadModelTexture(string name)
        {
            string cacheKey = "modeltex_" + name;
            if (_textureCache.TryGetValue(cacheKey, out var cached) && cached != null)
                return cached;

            Texture2D tex = Resources.Load<Texture2D>("Yijing/Textures/" + name);
            if (tex == null)
            {
                tex = Resources.Load<Texture2D>("Yijing/Models/textures/" + name);
            }
            if (tex != null)
            {
                _textureCache[cacheKey] = tex;
                return tex;
            }
            return null;
        }

        public static Texture2D LoadPolyHavenDiffTexture(string resourceModelName)
        {
            if (string.IsNullOrEmpty(resourceModelName)) return null;
            string cacheKey = "phtex_" + resourceModelName;
            if (_textureCache.TryGetValue(cacheKey, out var cached) && cached != null)
                return cached;

            Texture2D tex = Resources.Load<Texture2D>("Yijing/Models/textures/" + resourceModelName + "_diff");
            if (tex == null && resourceModelName.StartsWith("ph_"))
            {
                string rawId = resourceModelName.Substring(3);
                tex = Resources.Load<Texture2D>("Yijing/Models/textures/" + rawId + "_diff_1k");
            }
            if (tex != null)
            {
                _textureCache[cacheKey] = tex;
                return tex;
            }
            return null;
        }

        public static GameObject LoadModelPrefab(string name)
        {
            return Resources.Load<GameObject>("Yijing/Models/" + name);
        }

        private static void BuildDefaultScenes()
        {
            _scenes = new List<MemorySceneDef>
            {
                // Scene 1: 老家小院 · 朱檐庭院 (Pure 3D Courtyard with Open-Source FBX Nature & Furniture)
                new MemorySceneDef
                {
                    id = "courtyard-main",
                    title = "老家小院 · 朱檐庭院",
                    description = "粉墙黛瓦与朱红木廊围合的江南庭院，院内布置迎客青松、石灯古塔、紫砂茶席、雕花木窗与多处盆景花卉（纯3D实景建模）",
                    theme = "courtyard",
                    textureResourceName = "first-courtyard",
                    reviewed = true,
                    revision = 3,
                    annotations = new List<MemoryAnnotationDef>
                    {
                        new MemoryAnnotationDef { id = "cy-0", label = "青松翠柏", x = 0.12f, y = 0.22f, zone = 0, propKind = "pine_tree", iconKey = "flower", worldLocalPos = new Vector3(-3.65f, 1.15f, 1.95f), markerOffset = new Vector3(0f, 0.95f, 0f) },
                        new MemoryAnnotationDef { id = "cy-1", label = "石灯古塔", x = 0.42f, y = 0.20f, zone = 1, propKind = "stone_lantern", iconKey = "clock", worldLocalPos = new Vector3(-0.95f, 0.95f, 2.25f), markerOffset = new Vector3(0f, 0.85f, 0f) },
                        new MemoryAnnotationDef { id = "cy-2", label = "雕花木窗", x = 0.88f, y = 0.24f, zone = 2, propKind = "carved_window", iconKey = "memory", worldLocalPos = new Vector3(3.65f, 1.45f, 1.55f), markerOffset = new Vector3(0f, 0.72f, 0f) },
                        new MemoryAnnotationDef { id = "cy-3", label = "迎客盆景", x = 0.26f, y = 0.52f, zone = 3, propKind = "bonsai_pot", iconKey = "flower", worldLocalPos = new Vector3(-1.95f, 0.55f, 0.65f), markerOffset = new Vector3(0f, 0.68f, 0f) },
                        new MemoryAnnotationDef { id = "cy-4", label = "紫砂茶席", x = 0.50f, y = 0.56f, zone = 4, propKind = "tea_table", iconKey = "teapot", worldLocalPos = new Vector3(0.05f, 0.78f, 0.25f), markerOffset = new Vector3(0f, 0.65f, 0f) },
                        new MemoryAnnotationDef { id = "cy-5", label = "紫兰花钵", x = 0.72f, y = 0.48f, zone = 5, propKind = "purple_orchid", iconKey = "flower", worldLocalPos = new Vector3(1.95f, 0.55f, 1.25f), markerOffset = new Vector3(0f, 0.68f, 0f) },
                        new MemoryAnnotationDef { id = "cy-6", label = "金菊石坛", x = 0.16f, y = 0.78f, zone = 6, propKind = "chrysanthemum", iconKey = "flower", worldLocalPos = new Vector3(-2.85f, 0.42f, -1.25f), markerOffset = new Vector3(0f, 0.65f, 0f) },
                        new MemoryAnnotationDef { id = "cy-7", label = "庭院藤椅", x = 0.82f, y = 0.78f, zone = 7, propKind = "courtyard_chair", iconKey = "basket", worldLocalPos = new Vector3(2.55f, 0.52f, -1.05f), markerOffset = new Vector3(0f, 0.65f, 0f) },
                    }
                },

                // Scene 2: 家中就餐 · 温馨客厅 (Pure 3D Sunlit Living Room with Realistic Home Layout)
                new MemorySceneDef
                {
                    id = "0a2c9b47eb464893b4ae65482212e5ee",
                    title = "家中就餐 · 温馨客厅",
                    description = "阳光透过左侧落地窗与暖棕窗帘洒入木地板客厅，沙发靠墙配挂画，茶几居中摆放茶具鲜果，电视柜、落地灯与收音机错落有致（纯3D实景建模）",
                    theme = "living",
                    textureResourceName = "0a2c9b47eb464893b4ae65482212e5ee",
                    reviewed = true,
                    revision = 4,
                    annotations = new List<MemoryAnnotationDef>
                    {
                        new MemoryAnnotationDef { id = "lv-0", label = "阳光落地窗", x = 0.08f, y = 0.24f, zone = 0, propKind = "sunlit_window", iconKey = "memory", worldLocalPos = new Vector3(-4.55f, 1.45f, 0.85f), markerOffset = new Vector3(0.18f, 0.75f, 0f) },
                        new MemoryAnnotationDef { id = "lv-1", label = "立式落地灯", x = 0.30f, y = 0.20f, zone = 1, propKind = "floor_lamp", iconKey = "clock", worldLocalPos = new Vector3(-2.25f, 1.15f, 2.25f), markerOffset = new Vector3(0f, 0.78f, 0f) },
                        new MemoryAnnotationDef { id = "lv-2", label = "现代电视柜", x = 0.78f, y = 0.24f, zone = 2, propKind = "tv_cabinet", iconKey = "memory", worldLocalPos = new Vector3(2.75f, 0.92f, 2.05f), markerOffset = new Vector3(0f, 0.78f, 0f) },
                        new MemoryAnnotationDef { id = "lv-3", label = "龟背竹盆栽", x = 0.20f, y = 0.54f, zone = 3, propKind = "indoor_plant", iconKey = "flower", worldLocalPos = new Vector3(-3.35f, 0.55f, 0.15f), markerOffset = new Vector3(0f, 0.68f, 0f) },
                        new MemoryAnnotationDef { id = "lv-4", label = "浅蓝长沙发", x = 0.46f, y = 0.36f, zone = 4, propKind = "blue_sofa", iconKey = "basket", worldLocalPos = new Vector3(-0.25f, 0.68f, 2.15f), markerOffset = new Vector3(-0.35f, 0.75f, 0f) },
                        new MemoryAnnotationDef { id = "lv-5", label = "复古收音机", x = 0.86f, y = 0.62f, zone = 5, propKind = "vintage_radio", iconKey = "clock", worldLocalPos = new Vector3(2.95f, 0.75f, -0.65f), markerOffset = new Vector3(0f, 0.65f, 0f) },
                        new MemoryAnnotationDef { id = "lv-6", label = "单人休闲椅", x = 0.26f, y = 0.78f, zone = 6, propKind = "lounge_chair", iconKey = "basket", worldLocalPos = new Vector3(-2.05f, 0.52f, -1.25f), markerOffset = new Vector3(0f, 0.65f, 0f) },
                        new MemoryAnnotationDef { id = "lv-7", label = "茶几水果盘", x = 0.54f, y = 0.68f, zone = 7, propKind = "coffee_table", iconKey = "apple", worldLocalPos = new Vector3(-0.15f, 0.45f, 0.15f), markerOffset = new Vector3(0.28f, 0.58f, 0f) },
                    }
                },

                // Scene 3: 午后的农村庭院 (Pure 3D Authentic Rural Veranda & Courtyard)
                new MemorySceneDef
                {
                    id = "1a4c6ce6da854c7f9652f4c41079131a",
                    title = "午后的农村庭院",
                    description = "午后暖阳下的传统农家木廊院落，院心摆放农家大圆木桌与餐椅，周围配有老槐树、整齐木柴棚、丰收瓜果竹篓、红花木箱、长椅与汲水木桶台（纯3D实景建模）",
                    theme = "rural",
                    textureResourceName = "1a4c6ce6da854c7f9652f4c41079131a",
                    reviewed = true,
                    revision = 4,
                    annotations = new List<MemoryAnnotationDef>
                    {
                        new MemoryAnnotationDef { id = "rc-0", label = "金秋大橡树", x = 0.12f, y = 0.22f, zone = 0, propKind = "oak_tree", iconKey = "flower", worldLocalPos = new Vector3(-3.85f, 1.25f, 2.05f), markerOffset = new Vector3(0f, 0.95f, 0f) },
                        new MemoryAnnotationDef { id = "rc-1", label = "木栅柴火垛", x = 0.38f, y = 0.20f, zone = 1, propKind = "log_stack", iconKey = "basket", worldLocalPos = new Vector3(-1.45f, 0.72f, 2.35f), markerOffset = new Vector3(0f, 0.75f, 0f) },
                        new MemoryAnnotationDef { id = "rc-2", label = "翠竹篱笆林", x = 0.88f, y = 0.24f, zone = 2, propKind = "bamboo_grove", iconKey = "flower", worldLocalPos = new Vector3(3.75f, 1.25f, 2.05f), markerOffset = new Vector3(0f, 0.90f, 0f) },
                        new MemoryAnnotationDef { id = "rc-3", label = "丰收瓜果篓", x = 0.24f, y = 0.52f, zone = 3, propKind = "pumpkin_pile", iconKey = "apple", worldLocalPos = new Vector3(-2.25f, 0.45f, 0.45f), markerOffset = new Vector3(0f, 0.65f, 0f) },
                        new MemoryAnnotationDef { id = "rc-4", label = "农家大圆桌", x = 0.50f, y = 0.56f, zone = 4, propKind = "round_table", iconKey = "teapot", worldLocalPos = new Vector3(0.05f, 0.72f, 0.15f), markerOffset = new Vector3(0f, 0.65f, 0f) },
                        new MemoryAnnotationDef { id = "rc-5", label = "山石红花圃", x = 0.76f, y = 0.50f, zone = 5, propKind = "rock_flowerbed", iconKey = "flower", worldLocalPos = new Vector3(2.25f, 0.48f, 0.55f), markerOffset = new Vector3(0f, 0.68f, 0f) },
                        new MemoryAnnotationDef { id = "rc-7", label = "庭院木长椅", x = 0.18f, y = 0.78f, zone = 6, propKind = "wooden_bench", iconKey = "basket", worldLocalPos = new Vector3(-2.85f, 0.50f, -1.15f), markerOffset = new Vector3(0f, 0.65f, 0f) },
                        new MemoryAnnotationDef { id = "rc-8", label = "木桶汲水台", x = 0.82f, y = 0.78f, zone = 7, propKind = "stone_bridge", iconKey = "cup", worldLocalPos = new Vector3(2.85f, 0.48f, -1.15f), markerOffset = new Vector3(0f, 0.65f, 0f) },
                    }
                },

                // Scene 4: 阳光书房与卧室 (Pure 3D Warm Bedroom & Sunlit Study)
                new MemorySceneDef
                {
                    id = "172f4143c7db4a309f3c6ed1a6fc266b",
                    title = "阳光书房与卧室",
                    description = "右侧阳光大窗与暖黄窗帘照亮原木卧室书房，整面藏书架与双门木柜靠墙排布，配有温馨原木双人床、床头台灯柜、窗边笔记本电脑书桌与复古落地钟（纯3D实景建模）",
                    theme = "bedroom",
                    textureResourceName = "172f4143c7db4a309f3c6ed1a6fc266b",
                    reviewed = true,
                    revision = 4,
                    annotations = new List<MemoryAnnotationDef>
                    {
                        new MemoryAnnotationDef { id = "bd-0", label = "实木双门书柜", x = 0.12f, y = 0.22f, zone = 0, propKind = "bookcase_closed", iconKey = "memory", worldLocalPos = new Vector3(-3.65f, 1.15f, 2.15f), markerOffset = new Vector3(0f, 0.85f, 0f) },
                        new MemoryAnnotationDef { id = "bd-1", label = "开放藏书架", x = 0.36f, y = 0.20f, zone = 1, propKind = "bookcase_open", iconKey = "memory", worldLocalPos = new Vector3(-1.35f, 1.15f, 2.25f), markerOffset = new Vector3(0f, 0.85f, 0f) },
                        new MemoryAnnotationDef { id = "bd-2", label = "复古落地钟", x = 0.68f, y = 0.22f, zone = 2, propKind = "coat_rack", iconKey = "clock", worldLocalPos = new Vector3(1.85f, 1.15f, 2.25f), markerOffset = new Vector3(0f, 0.85f, 0f) },
                        new MemoryAnnotationDef { id = "bd-3", label = "床头台灯柜", x = 0.26f, y = 0.50f, zone = 3, propKind = "nightstand_lamp", iconKey = "clock", worldLocalPos = new Vector3(-1.95f, 0.65f, 0.55f), markerOffset = new Vector3(0f, 0.65f, 0f) },
                        new MemoryAnnotationDef { id = "bd-4", label = "阳光双人床", x = 0.50f, y = 0.56f, zone = 4, propKind = "double_bed", iconKey = "pillow", worldLocalPos = new Vector3(0.05f, 0.62f, 0.35f), markerOffset = new Vector3(0f, 0.68f, 0f) },
                        new MemoryAnnotationDef { id = "bd-5", label = "书桌笔记本", x = 0.82f, y = 0.50f, zone = 5, propKind = "study_desk", iconKey = "memory", worldLocalPos = new Vector3(3.35f, 0.82f, 0.45f), markerOffset = new Vector3(0f, 0.68f, 0f) },
                        new MemoryAnnotationDef { id = "bd-6", label = "花几与座钟", x = 0.16f, y = 0.78f, zone = 6, propKind = "teddy_bear", iconKey = "clock", worldLocalPos = new Vector3(-2.85f, 0.52f, -1.10f), markerOffset = new Vector3(0f, 0.62f, 0f) },
                        new MemoryAnnotationDef { id = "bd-8", label = "书房转角椅", x = 0.72f, y = 0.78f, zone = 7, propKind = "desk_chair", iconKey = "basket", worldLocalPos = new Vector3(2.15f, 0.55f, -1.05f), markerOffset = new Vector3(0f, 0.62f, 0f) },
                    }
                }
            };
        }

        private static void LoadSimulationRecords()
        {
            _simulationRecords = new List<TrainingSessionRecord>();
            TextAsset asset = Resources.Load<TextAsset>("Yijing/Data/simulation_records");
            if (asset == null || string.IsNullOrEmpty(asset.text)) return;

            try
            {
                string wrapped = "{\"records\":" + asset.text + "}";
                var col = JsonUtility.FromJson<TrainingRecordCollection>(wrapped);
                if (col != null && col.records != null)
                {
                    foreach (var r in col.records)
                    {
                        r.synthetic = true;
                        NormalizeRecordMetrics(r);
                        _simulationRecords.Add(r);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Simulation records parse warning: " + e.Message);
            }
        }

        public static void NormalizeRecordMetrics(TrainingSessionRecord r)
        {
            if (r == null) return;
            if (string.IsNullOrEmpty(r.game)) r.game = "memory";
            if (string.IsNullOrEmpty(r.gameName)) r.gameName = YijingZones.GameCodeToName(r.game);
            if (r.responses == null) r.responses = new List<QuestionResponseRecord>();
            if (r.metrics == null) r.metrics = new SessionMetricsRecord();

            int answered = r.responses.Count;
            int correct = 0;
            int skipped = 0;
            int omissions = 0;
            int errors = 0;
            List<int> validTimes = new List<int>();

            foreach (var resp in r.responses)
            {
                bool isUnselected = string.IsNullOrEmpty(resp.choice) || resp.choiceLabel == "未选择" || resp.choiceLabel == "不记得" || resp.choiceLabel == "跳过";
                if (resp.omitted)
                {
                    resp.choice = null;
                    omissions++;
                }
                else if (isUnselected)
                {
                    resp.choice = null;
                    skipped++;
                }
                else
                {
                    validTimes.Add(Mathf.Max(100, resp.reactionMs));
                    if (resp.correct) correct++;
                    else errors++;
                }
            }

            r.metrics.answered = answered;
            r.metrics.correct = correct;
            if (r.total <= 0) r.total = Mathf.Max(answered, 3);
            r.metrics.total = r.total;
            r.metrics.skipped = skipped;
            r.metrics.omissions = omissions;
            r.metrics.errors = errors;
            r.metrics.accuracy = answered > 0 ? (float)correct / answered : -1f;

            if (validTimes.Count > 0)
            {
                double mean = validTimes.Average();
                r.metrics.meanReactionMs = Mathf.RoundToInt((float)mean);
                if (validTimes.Count > 1)
                {
                    double variance = validTimes.Sum(t => (t - mean) * (t - mean)) / validTimes.Count;
                    double sd = Math.Sqrt(variance);
                    r.metrics.sdReactionMs = Mathf.RoundToInt((float)sd);
                    r.metrics.cvReactionMs = mean > 1.0 ? (float)(sd / mean) : 0f;
                }
                else
                {
                    r.metrics.sdReactionMs = 0;
                    r.metrics.cvReactionMs = 0f;
                }
            }
            else
            {
                r.metrics.meanReactionMs = -1;
                r.metrics.sdReactionMs = 0;
                r.metrics.cvReactionMs = 0f;
            }
        }

        public static Texture2D GetProceduralTexture(string style)
        {
            if (_proceduralTextures.TryGetValue(style, out var tex) && tex != null)
                return tex;

            int size = 256;
            tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size;
                    float v = y / (float)size;
                    Color c;

                    switch (style)
                    {
                        case "WoodPlanks":
                        {
                            int plankIdx = x / 32;
                            float plankVar = ((plankIdx * 37) % 11) * 0.008f - 0.04f;
                            float grain1 = Mathf.PerlinNoise(u * 10f + plankIdx * 1.7f, v * 58f) * 0.14f;
                            float grain2 = Mathf.Sin((v * 24f + Mathf.PerlinNoise(u * 6f, v * 12f) * 2.5f) * Mathf.PI) * 0.035f;
                            bool plankLine = (x % 32 < 2) || ((y + (plankIdx % 2) * 128) % 256 < 3);
                            c = new Color(
                                Mathf.Clamp01(0.62f - grain1 + grain2 + plankVar),
                                Mathf.Clamp01(0.43f - grain1 * 0.82f + grain2 * 0.7f + plankVar * 0.8f),
                                Mathf.Clamp01(0.26f - grain1 * 0.65f + grain2 * 0.5f + plankVar * 0.6f),
                                1f);
                            if (plankLine) c *= 0.62f;
                            break;
                        }
                        case "RedRoofTile":
                        {
                            float wave = Mathf.Sin(u * Mathf.PI * 24f) * 0.09f;
                            float weathering = Mathf.PerlinNoise(u * 18f, v * 18f) * 0.06f;
                            bool rowLine = (y % 24 < 3);
                            c = new Color(0.58f + wave - weathering, 0.22f + wave * 0.35f - weathering * 0.5f, 0.17f + wave * 0.25f, 1f);
                            if (rowLine) c *= 0.68f;
                            break;
                        }
                        case "WhiteCourtyardWall":
                        {
                            float plasterNoise = Mathf.PerlinNoise(u * 18f, v * 18f) * 0.07f + Mathf.PerlinNoise(u * 48f, v * 48f) * 0.03f;
                            int row = y / 24;
                            int offset = (row % 2) * 24;
                            bool mortar = (y % 24 < 2) || ((x + offset) % 48 < 2);
                            // Warm natural plaster & ash-brick tone that never overexposes to pure white
                            c = new Color(0.84f - plasterNoise, 0.82f - plasterNoise, 0.77f - plasterNoise, 1f);
                            if (mortar) c = new Color(0.71f - plasterNoise * 0.5f, 0.68f - plasterNoise * 0.5f, 0.63f - plasterNoise * 0.5f, 1f);
                            break;
                        }
                        case "FoliageGreen":
                        {
                            float n1 = Mathf.PerlinNoise(u * 18f, v * 18f);
                            float n2 = Mathf.PerlinNoise(u * 42f + 5f, v * 42f + 5f);
                            c = Color.Lerp(new Color(0.18f, 0.42f, 0.16f), new Color(0.42f, 0.64f, 0.24f), n1 * 0.65f + n2 * 0.35f);
                            break;
                        }
                        case "SofaFabric":
                        {
                            float weave = (((x / 2) + (y / 2)) % 2 == 0) ? 0.025f : -0.025f;
                            float n = Mathf.PerlinNoise(u * 32f, v * 32f) * 0.04f;
                            c = new Color(0.38f + weave - n, 0.56f + weave - n, 0.68f + weave - n, 1f);
                            break;
                        }
                        case "BookShelf":
                        {
                            int shelf = y % 64;
                            if (shelf < 8)
                            {
                                c = new Color(0.42f, 0.28f, 0.16f, 1f);
                            }
                            else
                            {
                                int bookIdx = x / 16;
                                Color[] bookColors =
                                {
                                    new Color(0.64f, 0.24f, 0.20f),
                                    new Color(0.22f, 0.42f, 0.58f),
                                    new Color(0.26f, 0.50f, 0.32f),
                                    new Color(0.70f, 0.56f, 0.26f),
                                    new Color(0.46f, 0.32f, 0.54f),
                                    new Color(0.78f, 0.74f, 0.66f)
                                };
                                c = bookColors[bookIdx % bookColors.Length];
                                if (x % 16 < 2) c *= 0.62f;
                            }
                            break;
                        }
                        case "KitchenTile":
                        {
                            bool grout = (x % 32 < 2) || (y % 32 < 2);
                            int cell = ((x / 32) + (y / 32)) % 2;
                            c = grout ? new Color(0.68f, 0.66f, 0.62f) : (cell == 0 ? new Color(0.88f, 0.86f, 0.81f) : new Color(0.82f, 0.79f, 0.73f));
                            break;
                        }
                        case "GreyTileRoof":
                        {
                            float wave = Mathf.Sin(u * Mathf.PI * 28f) * 0.07f;
                            float moss = Mathf.PerlinNoise(u * 22f, v * 22f) * 0.05f;
                            bool rowLine = (y % 20 < 3);
                            c = new Color(0.26f + wave - moss, 0.29f + wave - moss * 0.5f, 0.32f + wave - moss, 1f);
                            if (rowLine) c *= 0.65f;
                            break;
                        }
                        case "StonePaving":
                        {
                            int tileX = x / 42;
                            int tileY = y / 42;
                            float tileTint = (((tileX * 19 + tileY * 31) % 9) - 4) * 0.012f;
                            float n = Mathf.PerlinNoise(u * 24f, v * 24f) * 0.09f + Mathf.PerlinNoise(u * 64f, v * 64f) * 0.04f;
                            bool seam = (x % 42 < 3) || (y % 42 < 3);
                            c = seam
                                ? new Color(0.44f, 0.43f, 0.40f, 1f)
                                : new Color(0.70f - n + tileTint, 0.68f - n + tileTint, 0.64f - n + tileTint, 1f);
                            break;
                        }
                        case "GrassLawn":
                        {
                            float n1 = Mathf.PerlinNoise(u * 24f, v * 24f);
                            float n2 = Mathf.PerlinNoise(u * 56f + 7f, v * 56f + 7f);
                            c = Color.Lerp(new Color(0.26f, 0.48f, 0.20f), new Color(0.44f, 0.64f, 0.28f), n1 * 0.6f + n2 * 0.4f);
                            break;
                        }
                        default:
                        {
                            float g = Mathf.PerlinNoise(u * 12f, v * 36f) * 0.10f;
                            c = new Color(0.64f - g, 0.46f - g * 0.8f, 0.30f - g * 0.6f, 1f);
                            break;
                        }
                    }
                    pixels[y * size + x] = c;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(true);
            _proceduralTextures[style] = tex;
            return tex;
        }

        public static Texture2D GetProceduralNormalMap(string style)
        {
            string key = "normal_" + style;
            if (_proceduralTextures.TryGetValue(key, out var normTex) && normTex != null)
                return normTex;

            Texture2D src = GetProceduralTexture(style);
            int size = src.width;
            Color[] srcPixels = src.GetPixels();
            Color[] normPixels = new Color[size * size];
            float strength = 3.2f;

            for (int y = 0; y < size; y++)
            {
                int yUp = (y + 1) % size;
                int yDown = (y - 1 + size) % size;
                for (int x = 0; x < size; x++)
                {
                    int xRight = (x + 1) % size;
                    int xLeft = (x - 1 + size) % size;

                    float hL = srcPixels[y * size + xLeft].grayscale;
                    float hR = srcPixels[y * size + xRight].grayscale;
                    float hD = srcPixels[yDown * size + x].grayscale;
                    float hU = srcPixels[yUp * size + x].grayscale;

                    Vector3 n = new Vector3((hL - hR) * strength, (hD - hU) * strength, 1.0f).normalized;
                    normPixels[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1.0f);
                }
            }

            normTex = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
            normTex.wrapMode = TextureWrapMode.Repeat;
            normTex.filterMode = FilterMode.Trilinear;
            normTex.SetPixels(normPixels);
            normTex.Apply(true);
            _proceduralTextures[key] = normTex;
            return normTex;
        }
    }
}
