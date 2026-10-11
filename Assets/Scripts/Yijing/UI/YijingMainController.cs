using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Yijing.Analytics;
using Yijing.Core;
using Yijing.Data;
using Yijing.World3D;

namespace Yijing.UI
{
    public enum YijingViewMode
    {
        Home = 0,
        GameSetup = 1,
        Demo = 2,
        Playing = 3,
        Result = 4,
        DailySetup = 5,
        DailySummary = 6,
        Assessment = 7,
        Records = 8,
        RecordDetail = 9,
        SceneStudio = 10
    }

    public class YijingMainController : MonoBehaviour
    {
        public static YijingMainController Instance { get; private set; }

        public YijingViewMode CurrentView = YijingViewMode.Home;
        public YijingGameType SelectedSetupGame = YijingGameType.Memory;

        // Assessment & Records Filter State
        public bool UseSimulationDataInRecords = false;
        public string FilterParticipant = "*";
        public string FilterGame = "";
        public int SelectedTrendGroupIndex = 0;
        public TrainingSessionRecord SelectedDetailRecord;
        public CognitiveAssessmentProfile CachedProfile;

        // Demo & Hint state
        private YijingGameType _demoGame;
        private int _demoStepIndex = 0;
        private string _demoFeedback = "";
        private bool _showMemoryTargetHint = false;

        // Scene Studio annotation state
        private int _studioSceneIndex = 0;
        private string _newAnnotationLabel = "新标注物品";
        private int _newAnnotationZone = 4;

        // Toast notification
        private string _toastText = "";
        private float _toastUntil = 0f;

        // Scroll positions
        private Vector2 _mainScroll;
        private Vector2 _detailScroll;
        private Vector2 _assessmentScroll;

        // Custom GUI styles
        private bool _stylesInitialized;
        private GUIStyle _headerStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _cardStyle;
        private GUIStyle _panelStyle;
        private GUIStyle _primaryBtnStyle;
        private GUIStyle _secondaryBtnStyle;
        private GUIStyle _zoneBtnStyle;
        private GUIStyle _badgeStyle;
        private GUIStyle _toastStyle;
        private GUIStyle _bodyStyle;

        // Enlarged Sequence & Non-Overlapping Marker styles
        private GUIStyle _markerLabelStyle;
        private GUIStyle _markerSeqLabelStyle;
        private GUIStyle _markerClickBtnStyle;
        private GUIStyle _markerDotStyle;
        private GUIStyle _seqHeaderStyle;
        private GUIStyle _seqStepBannerStyle;
        private GUIStyle _seqItemTitleStyle;
        private GUIStyle _seqItemSpotStyle;
        private GUIStyle _seqArrowStyle;
        private GUIStyle _hugeTargetTitleStyle;

        private Texture2D _texBgCream;
        private Texture2D _texCardWhite;
        private Texture2D _texPrimaryGreen;
        private Texture2D _texPrimaryGreenHover;
        private Texture2D _texSecondarySoft;
        private Texture2D _texAccentAmber;
        private Texture2D _texMarkerDark;
        private Texture2D _texMarkerSeqGold;
        private Texture2D _texBarBg;
        private Texture2D _texBarFill;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrapOnPlay()
        {
            if (FindFirstObjectByType<YijingMainController>() != null) return;
            var root = GameObject.Find("YijingSystemRoot");
            if (root == null) root = new GameObject("YijingSystemRoot");
            if (root.GetComponent<Yijing3DWorldBuilder>() == null)
                root.AddComponent<Yijing3DWorldBuilder>().BuildCompleteWorldIfNeeded();
            if (root.GetComponent<YijingGameEngine>() == null)
                root.AddComponent<YijingGameEngine>();
            if (root.GetComponent<YijingMainController>() == null)
                root.AddComponent<YijingMainController>();
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            var world = GetComponent<Yijing3DWorldBuilder>() ?? FindFirstObjectByType<Yijing3DWorldBuilder>();
            if (world == null)
            {
                world = gameObject.AddComponent<Yijing3DWorldBuilder>();
            }
            world.BuildCompleteWorldIfNeeded();

            var engine = GetComponent<YijingGameEngine>() ?? FindFirstObjectByType<YijingGameEngine>();
            if (engine == null)
            {
                engine = gameObject.AddComponent<YijingGameEngine>();
            }

            engine.OnRunFinished += HandleRunFinished;
            engine.OnToastMessage += ShowToast;

            // Seed initial local sample records if empty so Assessment Dashboard works immediately out of the box
            if (engine.GetLocalRecords().Count == 0)
            {
                SeedSampleLocalRecords();
            }
            RefreshAssessmentProfile();
        }

        public void ShowToast(string msg)
        {
            _toastText = msg;
            _toastUntil = Time.realtimeSinceStartup + 4.0f;
        }

        private void HandleRunFinished(TrainingSessionRecord record)
        {
            SelectedDetailRecord = record;
            RefreshAssessmentProfile();

            var engine = YijingGameEngine.Instance;
            if (engine != null && engine.IsDailyFlowActive && engine.ActiveDailySession != null && record.dailyId == engine.ActiveDailySession.id)
            {
                CurrentView = YijingViewMode.DailySummary;
            }
            else
            {
                CurrentView = YijingViewMode.Result;
            }
        }

        private void RefreshAssessmentProfile()
        {
            var rows = GetFilteredRecords();
            CachedProfile = YijingCognitiveEvaluator.EvaluateRecords(rows, FilterParticipant);
            SelectedTrendGroupIndex = 0;
        }

        private List<TrainingSessionRecord> GetFilteredRecords()
        {
            var engine = YijingGameEngine.Instance;
            if (engine == null) return new List<TrainingSessionRecord>();

            var source = UseSimulationDataInRecords ? engine.GetSimulationRecords() : engine.GetLocalRecords();
            return source.Where(r =>
                (FilterParticipant == "*" || string.IsNullOrEmpty(FilterParticipant) || (r.participant ?? "") == FilterParticipant) &&
                (string.IsNullOrEmpty(FilterGame) || (r.game ?? "memory") == FilterGame)
            ).ToList();
        }

        private void InitStyles()
        {
            if (_stylesInitialized && _texCardWhite != null) return;

            _texBgCream = MakeSolidTex(new Color(0.95f, 0.96f, 0.94f, 0.96f));
            _texCardWhite = MakeSolidTex(new Color(0.99f, 0.99f, 0.98f, 0.97f));
            _texPrimaryGreen = MakeSolidTex(new Color(0.14f, 0.44f, 0.32f, 1.0f));
            _texPrimaryGreenHover = MakeSolidTex(new Color(0.20f, 0.56f, 0.40f, 1.0f));
            _texSecondarySoft = MakeSolidTex(new Color(0.86f, 0.90f, 0.87f, 0.98f));
            _texAccentAmber = MakeSolidTex(new Color(0.82f, 0.48f, 0.12f, 1.0f));
            _texMarkerDark = MakeSolidTex(new Color(0.10f, 0.24f, 0.19f, 0.96f));
            _texMarkerSeqGold = MakeSolidTex(new Color(0.76f, 0.42f, 0.08f, 0.98f));
            _texBarBg = MakeSolidTex(new Color(0.84f, 0.88f, 0.85f, 1.0f));
            _texBarFill = MakeSolidTex(new Color(0.20f, 0.54f, 0.40f, 1.0f));

            _headerStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _texCardWhite, textColor = new Color(0.14f, 0.24f, 0.20f) },
                padding = new RectOffset(14, 14, 8, 8)
            };

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 21,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.12f, 0.24f, 0.19f) },
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 2, 2),
                wordWrap = true,
                clipping = TextClipping.Overflow
            };

            _hugeTargetTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.10f, 0.36f, 0.24f) },
                padding = new RectOffset(0, 0, 0, 0),
                wordWrap = false,
                clipping = TextClipping.Overflow
            };

            _subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.26f, 0.36f, 0.31f) },
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 2, 2),
                wordWrap = true,
                clipping = TextClipping.Overflow
            };

            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.18f, 0.26f, 0.22f) },
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 2, 2),
                wordWrap = true
            };

            _cardStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _texCardWhite, textColor = new Color(0.15f, 0.24f, 0.20f) },
                padding = new RectOffset(14, 14, 10, 10),
                margin = new RectOffset(6, 6, 6, 6)
            };

            _panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _texBgCream, textColor = new Color(0.15f, 0.24f, 0.20f) },
                padding = new RectOffset(16, 16, 12, 12)
            };

            _primaryBtnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = _texPrimaryGreen, textColor = Color.white },
                hover = { background = _texPrimaryGreenHover, textColor = Color.white },
                active = { background = _texPrimaryGreen, textColor = Color.white },
                padding = new RectOffset(10, 10, 3, 3),
                margin = new RectOffset(4, 4, 3, 3),
                clipping = TextClipping.Overflow
            };

            _secondaryBtnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = _texSecondarySoft, textColor = new Color(0.14f, 0.26f, 0.21f) },
                hover = { background = _texPrimaryGreenHover, textColor = Color.white },
                padding = new RectOffset(10, 10, 3, 3),
                margin = new RectOffset(4, 4, 3, 3),
                clipping = TextClipping.Overflow
            };

            _zoneBtnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = _texCardWhite, textColor = new Color(0.12f, 0.26f, 0.20f) },
                hover = { background = _texPrimaryGreenHover, textColor = Color.white },
                padding = new RectOffset(8, 8, 3, 3),
                clipping = TextClipping.Overflow
            };

            _badgeStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = _texAccentAmber, textColor = Color.white },
                padding = new RectOffset(8, 8, 2, 2),
                clipping = TextClipping.Overflow
            };

            _toastStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = _texPrimaryGreen, textColor = Color.white },
                padding = new RectOffset(14, 14, 4, 4),
                clipping = TextClipping.Overflow
            };

            // 1. Unified Non-Overlapping 3D Marker Badge Styles (Large & High Contrast, zero vertical clipping)
            _markerLabelStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = _texMarkerDark, textColor = new Color(0.98f, 0.97f, 0.90f) },
                padding = new RectOffset(10, 10, 3, 3),
                wordWrap = false,
                clipping = TextClipping.Overflow
            };

            _markerSeqLabelStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = _texMarkerSeqGold, textColor = Color.white },
                padding = new RectOffset(12, 12, 3, 3),
                wordWrap = false,
                clipping = TextClipping.Overflow
            };

            _markerClickBtnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = _texMarkerDark, textColor = new Color(1.0f, 0.96f, 0.82f) },
                hover = { background = _texPrimaryGreenHover, textColor = Color.white },
                active = { background = _texPrimaryGreen, textColor = Color.white },
                padding = new RectOffset(12, 12, 3, 3),
                wordWrap = false,
                clipping = TextClipping.Overflow
            };

            _markerDotStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = _texAccentAmber, textColor = Color.white },
                padding = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Overflow
            };

            // 2. Enlarged Bottom Item Sequence Prompt Styles
            _seqHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 19,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.10f, 0.24f, 0.18f) },
                padding = new RectOffset(0, 0, 0, 0),
                wordWrap = false,
                clipping = TextClipping.Overflow
            };

            _seqStepBannerStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = _texPrimaryGreen, textColor = Color.white },
                padding = new RectOffset(4, 4, 2, 2),
                wordWrap = false,
                clipping = TextClipping.Overflow
            };

            _seqItemTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.10f, 0.22f, 0.16f) },
                padding = new RectOffset(0, 0, 0, 0),
                wordWrap = false,
                clipping = TextClipping.Overflow
            };

            _seqItemSpotStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.78f, 0.42f, 0.08f) },
                padding = new RectOffset(0, 0, 0, 0),
                wordWrap = false,
                clipping = TextClipping.Overflow
            };

            _seqArrowStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.80f, 0.45f, 0.10f) },
                padding = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Overflow
            };

            _stylesInitialized = true;
        }

        private static Texture2D MakeSolidTex(Color col)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.SetPixels(new[] { col, col, col, col });
            t.Apply();
            return t;
        }

        private float _currentUiScale = 1f;
        private Vector2 _setupScroll;

        private void OnGUI()
        {
            InitStyles();

            Matrix4x4 prevMatrix = GUI.matrix;
            _currentUiScale = Mathf.Clamp(Screen.height / 840f, 1.0f, 2.5f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(_currentUiScale, _currentUiScale, 1f));

            float sw = Screen.width / _currentUiScale;
            float sh = Screen.height / _currentUiScale;

            DrawTopNavigationBar(sw);

            // Toast notification banner
            if (Time.realtimeSinceStartup < _toastUntil && !string.IsNullOrEmpty(_toastText))
            {
                GUI.Box(new Rect(sw * 0.5f - 280f, 64f, 560f, 44f), _toastText, _toastStyle);
            }

            switch (CurrentView)
            {
                case YijingViewMode.Home:
                    DrawHomeDashboard(sw, sh);
                    break;
                case YijingViewMode.GameSetup:
                    DrawGameSetupView(sw, sh);
                    break;
                case YijingViewMode.Demo:
                    DrawDemoView(sw, sh);
                    break;
                case YijingViewMode.Playing:
                    DrawPlayingHUD(sw, sh);
                    break;
                case YijingViewMode.Result:
                    DrawResultView(sw, sh);
                    break;
                case YijingViewMode.DailySetup:
                    DrawDailySetupView(sw, sh);
                    break;
                case YijingViewMode.DailySummary:
                    DrawDailySummaryView(sw, sh);
                    break;
                case YijingViewMode.Assessment:
                    DrawCognitiveAssessmentDashboard(sw, sh);
                    break;
                case YijingViewMode.Records:
                    DrawRecordsListView(sw, sh);
                    break;
                case YijingViewMode.RecordDetail:
                    DrawRecordDetailView(sw, sh);
                    break;
                case YijingViewMode.SceneStudio:
                    DrawSceneStudioView(sw, sh);
                    break;
            }

            GUI.matrix = prevMatrix;
        }

        // ====================================================================
        // TOP NAVIGATION BAR
        // ====================================================================
        private void DrawTopNavigationBar(float sw)
        {
            GUI.Box(new Rect(0f, 0f, sw, 56f), "", _headerStyle);

            var engine = YijingGameEngine.Instance;
            var world = Yijing3DWorldBuilder.Instance;

            GUI.Label(new Rect(18f, 8f, 340f, 40f), "忆境 3D · 老年认知训练与评估系统", _titleStyle);

            if (CurrentView != YijingViewMode.Playing)
            {
                float bx = 362f;
                if (GUI.Button(new Rect(bx, 10f, 98f, 36f), "训练首页", _secondaryBtnStyle))
                {
                    CurrentView = YijingViewMode.Home;
                    if (world != null) world.ShowMemoryStage("courtyard", "first-courtyard");
                }
                bx += 106f;
                if (GUI.Button(new Rect(bx, 10f, 110f, 36f), "今日训练", _secondaryBtnStyle))
                {
                    CurrentView = YijingViewMode.DailySetup;
                }
                bx += 118f;
                if (GUI.Button(new Rect(bx, 10f, 152f, 36f), "认知评估与统计", _primaryBtnStyle))
                {
                    RefreshAssessmentProfile();
                    CurrentView = YijingViewMode.Assessment;
                }
                bx += 160f;
                if (GUI.Button(new Rect(bx, 10f, 118f, 36f), "训练记录库", _secondaryBtnStyle))
                {
                    RefreshAssessmentProfile();
                    CurrentView = YijingViewMode.Records;
                }
                bx += 126f;
                if (GUI.Button(new Rect(bx, 10f, 128f, 36f), "3D场景与标注", _secondaryBtnStyle))
                {
                    CurrentView = YijingViewMode.SceneStudio;
                }
            }

            // Right utility controls
            if (engine != null)
            {
                string voiceTxt = engine.VoiceEnabled ? "语音: 开" : "语音: 关";
                if (GUI.Button(new Rect(sw - 226f, 10f, 98f, 36f), voiceTxt, _secondaryBtnStyle))
                {
                    engine.VoiceEnabled = !engine.VoiceEnabled;
                    ShowToast(engine.VoiceEnabled ? "中文语音提示已开启" : "中文语音提示已关闭");
                }
            }
            if (world != null)
            {
                string motionTxt = world.reduceMotion ? "动态: 静止" : "动态: 正常";
                if (GUI.Button(new Rect(sw - 120f, 10f, 104f, 36f), motionTxt, _secondaryBtnStyle))
                {
                    world.reduceMotion = !world.reduceMotion;
                    ShowToast(world.reduceMotion ? "已开启减少动态效果（候选物品保持静止）" : "已恢复3D动态移动效果");
                }
            }
        }

        // ====================================================================
        // 1. HOME DASHBOARD (训练首页 - 3D 实景背景 + 三大游戏卡片)
        // ====================================================================
        private void DrawHomeDashboard(float sw, float sh)
        {
            var engine = YijingGameEngine.Instance;
            var world = Yijing3DWorldBuilder.Instance;

            // Quick 3D Scene Camera Switcher Strip at Top-Left so user can admire all 3D modeled scenes
            GUI.Box(new Rect(16f, 64f, sw - 32f, 48f), "", _cardStyle);
            GUI.Label(new Rect(28f, 70f, 175f, 36f), "3D 建模实景切换预览：", _bodyStyle);
            if (GUI.Button(new Rect(195f, 70f, 118f, 35f), "老家小院(3D)", _secondaryBtnStyle))
                world?.ShowMemoryStage("courtyard", "first-courtyard");
            if (GUI.Button(new Rect(321f, 70f, 118f, 35f), "温馨客厅(3D)", _secondaryBtnStyle))
                world?.ShowMemoryStage("living", "0a2c9b47eb464893b4ae65482212e5ee");
            if (GUI.Button(new Rect(447f, 70f, 118f, 35f), "阳光书房(3D)", _secondaryBtnStyle))
                world?.ShowMemoryStage("bedroom", "172f4143c7db4a309f3c6ed1a6fc266b");
            if (GUI.Button(new Rect(573f, 70f, 118f, 35f), "农家院落(3D)", _secondaryBtnStyle))
                world?.ShowMemoryStage("rural", "1a4c6ce6da854c7f9652f4c41079131a");
            if (GUI.Button(new Rect(699f, 70f, 128f, 35f), "注意力展台(3D)", _secondaryBtnStyle))
                world?.ShowAttentionStage("apple", new List<QuestionOptionRuntime>
                {
                    new QuestionOptionRuntime { id = "1", icon = "apple", label = "苹果" },
                    new QuestionOptionRuntime { id = "2", icon = "cup", label = "茶杯" },
                    new QuestionOptionRuntime { id = "3", icon = "clock", label = "时钟" }
                }, "easy");
            if (GUI.Button(new Rect(835f, 70f, 128f, 35f), "现代厨房(3D)", _secondaryBtnStyle))
                world?.ShowKitchenStage(YijingAssetLibrary.Catalog.kitchen[0].steps, true);

            // Lower Interactive Dock (leaves the middle 3D viewport open to show the 3D models!)
            float dockH = Mathf.Min(382f, sh * 0.54f);
            float dockY = sh - dockH - 12f;
            GUI.Box(new Rect(16f, dockY, sw - 32f, dockH), "", _panelStyle);

            // Daily Training Banner (Generous full-width & height so neither title nor subtitle is ever clipped!)
            GUI.Box(new Rect(32f, dockY + 10f, sw - 64f, 68f), "", _cardStyle);
            float bannerTextW = Mathf.Max(480f, sw - 310f);
            GUI.Label(new Rect(48f, dockY + 14f, bannerTextW, 34f), "今日三合一综合训练（记忆花园 · 注意力追踪 · 厨房步骤）", _titleStyle);
            GUI.Label(new Rect(48f, dockY + 46f, bannerTextW, 28f), "依次完成空间位置记忆、视觉选择性注意与执行步骤规划，自动生成五维认知评估报告。", _subtitleStyle);
            string dailyBtnTxt = (engine != null && engine.ActiveDailySession != null) ? "继续今日训练 →" : "开始今日训练 →";
            if (GUI.Button(new Rect(sw - 236f, dockY + 22f, 186f, 44f), dailyBtnTxt, _primaryBtnStyle))
            {
                CurrentView = YijingViewMode.DailySetup;
            }

            // Three Game Cards
            float cardsTop = dockY + 86f;
            float cardsAreaW = sw - 64f;
            float cardW = (cardsAreaW - 32f) / 3f;
            float cardH = dockH - 98f;

            DrawHomeGameCard(
                new Rect(32f, cardsTop, cardW, cardH),
                "记忆花园",
                "记住物品位置 · 4套3D实景直标点选",
                "观察 3D 庭院/居室中标在物品上的选择点，倒计时后直接在 3D 物品上点选目标。",
                "memory",
                () =>
                {
                    SelectedSetupGame = YijingGameType.Memory;
                    world?.ShowMemoryStage("courtyard", "first-courtyard");
                    CurrentView = YijingViewMode.GameSetup;
                });

            DrawHomeGameCard(
                new Rect(32f + cardW + 16f, cardsTop, cardW, cardH),
                "注意力追踪",
                "跟着目标找一找 · 15套主题 × 3档难度",
                "看清上方目标物品，在 3D 动态展台缓慢移动的候选物品中点选相同物品。",
                "apple",
                () =>
                {
                    SelectedSetupGame = YijingGameType.Attention;
                    world?.ShowAttentionStage("apple", new List<QuestionOptionRuntime>
                    {
                        new QuestionOptionRuntime { id = "1", icon = "apple", label = "苹果" },
                        new QuestionOptionRuntime { id = "2", icon = "cup", label = "茶杯" },
                        new QuestionOptionRuntime { id = "3", icon = "teapot", label = "茶壶" }
                    }, "easy");
                    CurrentView = YijingViewMode.GameSetup;
                });

            DrawHomeGameCard(
                new Rect(32f + (cardW + 16f) * 2f, cardsTop, cardW, cardH),
                "厨房步骤",
                "想好先后排一排 · 10套任务 × 3/4/6步",
                "在 3D 厨房中观察制作工序卡片，步骤隐藏并打乱后按先后顺序逐个选择。",
                "plate",
                () =>
                {
                    SelectedSetupGame = YijingGameType.Kitchen;
                    world?.ShowKitchenStage(YijingAssetLibrary.Catalog.kitchen[0].steps, true);
                    CurrentView = YijingViewMode.GameSetup;
                });
        }

        private void DrawHomeGameCard(Rect r, string title, string subtitle, string desc, string iconKey, Action onEnter)
        {
            GUI.Box(r, "", _cardStyle);
            float imgSize = Mathf.Min(92f, r.height - 44f);
            DrawIconSliceRect(new Rect(r.x + 14f, r.y + 18f, imgSize, imgSize), YijingAssetLibrary.GetIconSlice(iconKey));

            float tx = r.x + imgSize + 24f;
            float tw = r.width - imgSize - 36f;
            GUI.Label(new Rect(tx, r.y + 12f, tw, 34f), title, _titleStyle);
            GUI.Label(new Rect(tx, r.y + 44f, tw, 28f), subtitle, _subtitleStyle);
            GUI.Label(new Rect(tx, r.y + 72f, tw, 64f), desc, _bodyStyle);

            if (GUI.Button(new Rect(tx, r.y + r.height - 48f, Mathf.Min(165f, tw), 38f), "进入练习 →", _primaryBtnStyle))
            {
                onEnter?.Invoke();
            }
        }

        // ====================================================================
        // 2. GAME SETUP VIEW (单项游戏设置页 - 左侧设置面板 + 右侧 3D 实时场景预览)
        // ====================================================================
        private void DrawGameSetupView(float sw, float sh)
        {
            var engine = YijingGameEngine.Instance;
            var world = Yijing3DWorldBuilder.Instance;

            // Draw on-object 3D labels on the right 3D preview area when setting up Memory Garden
            if (SelectedSetupGame == YijingGameType.Memory && world != null)
            {
                var activeScene = YijingAssetLibrary.Scenes.FirstOrDefault(s => s.id == engine.SelectedMemorySceneId) ?? YijingAssetLibrary.Scenes[0];
                DrawScenePreviewObjectBadges(activeScene.annotations, sw, sh, 500f);
            }

            float panelW = Mathf.Min(475f, sw * 0.45f);
            Rect leftRect = new Rect(18f, 66f, panelW, sh - 80f);
            GUI.Box(leftRect, "", _panelStyle);

            GUILayout.BeginArea(new Rect(leftRect.x + 16f, leftRect.y + 12f, leftRect.width - 32f, leftRect.height - 24f));
            _setupScroll = GUILayout.BeginScrollView(_setupScroll);

            if (GUILayout.Button("← 返回选择游戏", _secondaryBtnStyle, GUILayout.Width(160f), GUILayout.Height(36f)))
            {
                CurrentView = YijingViewMode.Home;
            }

            GUILayout.Space(6f);
            string title = SelectedSetupGame == YijingGameType.Memory ? "记忆花园 · 3D物品直标位置记忆"
                         : SelectedSetupGame == YijingGameType.Attention ? "注意力追踪 · 3D动态视觉搜索"
                         : "厨房步骤 · 3D工序序列规划";
            GUILayout.Label(title, _titleStyle);

            string sub = SelectedSetupGame == YijingGameType.Memory
                ? "先观察纯 3D 庭院/居室场景中直接标在 3D 物品上的标点，倒计时结束后直接点击目标物品上的选择点。"
                : SelectedSetupGame == YijingGameType.Attention
                ? "看清上方目标物品，从 3D 展台移动的候选物品中点选相同物品（支持直接点击3D模型或下方卡片）。"
                : "先在 3D 厨房观察步骤顺序，倒计时结束后步骤打乱，请按第1步、第2步顺序逐次选择。";
            GUILayout.Label(sub, _subtitleStyle);
            GUILayout.Space(8f);

            // Scene or Task selector
            if (SelectedSetupGame == YijingGameType.Memory)
            {
                GUILayout.Label("选择纯 3D 记忆场景（4套实景扫描建模）：", _bodyStyle);
                foreach (var sc in YijingAssetLibrary.Scenes)
                {
                    bool selected = engine.SelectedMemorySceneId == sc.id;
                    string label = (selected ? "● " : "○ ") + sc.title + " (" + sc.annotations.Count + "个直标3D物品)";
                    if (GUILayout.Button(label, selected ? _primaryBtnStyle : _secondaryBtnStyle, GUILayout.Height(36f)))
                    {
                        engine.SelectedMemorySceneId = sc.id;
                        world?.ShowMemoryStage(sc.theme, sc.textureResourceName);
                    }
                }
            }
            else if (SelectedSetupGame == YijingGameType.Attention)
            {
                GUILayout.Label("选择图片主题（共 15 套主题）：", _bodyStyle);
                var themes = YijingAssetLibrary.Catalog.attention;
                int cols = 3;
                for (int i = 0; i < themes.Count; i += cols)
                {
                    GUILayout.BeginHorizontal();
                    for (int c = 0; c < cols && (i + c) < themes.Count; c++)
                    {
                        var th = themes[i + c];
                        bool sel = engine.SelectedAttentionTaskId == th.id;
                        if (GUILayout.Button(th.name, sel ? _primaryBtnStyle : _secondaryBtnStyle, GUILayout.Height(35f)))
                        {
                            engine.SelectedAttentionTaskId = th.id;
                            world?.ShowAttentionStage(th.items[0].icon, th.items.Take(3).Select(x => new QuestionOptionRuntime { id = x.icon, icon = x.icon, label = x.label }).ToList(), engine.PreferredDifficulty);
                        }
                    }
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.Label("选择厨房任务（共 10 套任务）：", _bodyStyle);
                var tasks = YijingAssetLibrary.Catalog.kitchen;
                int cols = 2;
                for (int i = 0; i < tasks.Count; i += cols)
                {
                    GUILayout.BeginHorizontal();
                    for (int c = 0; c < cols && (i + c) < tasks.Count; c++)
                    {
                        var tk = tasks[i + c];
                        bool sel = engine.SelectedKitchenTaskId == tk.id;
                        if (GUILayout.Button(tk.name, sel ? _primaryBtnStyle : _secondaryBtnStyle, GUILayout.Height(36f)))
                        {
                            engine.SelectedKitchenTaskId = tk.id;
                            world?.ShowKitchenStage(tk.steps, true);
                        }
                    }
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("选择难度档位：", _bodyStyle);
            GUILayout.BeginHorizontal();
            foreach (var diff in new[] { ("easy", "轻松 / 基础"), ("normal", "标准"), ("hard", "挑战") })
            {
                bool sel = engine.PreferredDifficulty == diff.Item1;
                if (GUILayout.Button(diff.Item2, sel ? _primaryBtnStyle : _secondaryBtnStyle, GUILayout.Height(36f)))
                {
                    engine.PreferredDifficulty = diff.Item1;
                }
            }
            GUILayout.EndHorizontal();

            // Difficulty description matching 忆境便携包 rules
            string diffHint = GetDifficultyRuleHint(SelectedSetupGame, engine.PreferredDifficulty);
            GUILayout.Label("规则说明：" + diffHint, _subtitleStyle);

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("参与者代号：", _bodyStyle, GUILayout.Width(100f), GUILayout.Height(32f));
            engine.PreferredParticipant = GUILayout.TextField(engine.PreferredParticipant ?? "P01", 24, GUILayout.Height(32f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("记录类型：", _bodyStyle, GUILayout.Width(100f), GUILayout.Height(34f));
            if (GUILayout.Button("正式训练", engine.PreferredMode == "formal" ? _primaryBtnStyle : _secondaryBtnStyle, GUILayout.Height(34f)))
                engine.PreferredMode = "formal";
            if (GUILayout.Button("练习模式", engine.PreferredMode == "practice" ? _primaryBtnStyle : _secondaryBtnStyle, GUILayout.Height(34f)))
                engine.PreferredMode = "practice";
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);

            if (GUILayout.Button("▶ 开始练习", _primaryBtnStyle, GUILayout.Height(44f)))
            {
                engine.IsDailyFlowActive = false;
                StartSelectedGameNow("");
            }
            GUILayout.Space(6f);
            if (GUILayout.Button("👁 先看示范（不计时间与成绩）", _secondaryBtnStyle, GUILayout.Height(38f)))
            {
                StartDemoMode(SelectedSetupGame);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void StartSelectedGameNow(string dailyId)
        {
            var engine = YijingGameEngine.Instance;
            if (SelectedSetupGame == YijingGameType.Memory)
            {
                engine.StartMemoryGarden(engine.SelectedMemorySceneId, engine.PreferredDifficulty, engine.PreferredMode, engine.PreferredParticipant, dailyId);
            }
            else if (SelectedSetupGame == YijingGameType.Attention)
            {
                engine.StartAttentionTracking(engine.SelectedAttentionTaskId, engine.PreferredDifficulty, engine.PreferredMode, engine.PreferredParticipant, dailyId);
            }
            else
            {
                engine.StartKitchenSteps(engine.SelectedKitchenTaskId, engine.PreferredDifficulty, engine.PreferredMode, engine.PreferredParticipant, dailyId);
            }
            CurrentView = YijingViewMode.Playing;
        }

        private string GetDifficultyRuleHint(YijingGameType g, string diff)
        {
            if (g == YijingGameType.Memory)
            {
                if (diff == "hard") return "挑战档：观察 12 秒 · 最多回忆 8 个物品位置";
                if (diff == "normal") return "标准档：观察 20 秒 · 最多回忆 5 个物品位置";
                return "基础档：观察 30 秒 · 最多回忆 3 个物品位置";
            }
            if (g == YijingGameType.Attention)
            {
                if (diff == "hard") return "挑战档：同屏 6 张同主题图片 · 每题 6 秒 · 正式 10 题";
                if (diff == "normal") return "标准档：同屏 4 张同主题图片 · 每题 9 秒 · 正式 8 题";
                return "轻松档：同屏 3 张图片（跨主题干扰） · 每题 12 秒 · 正式 6 题";
            }
            if (diff == "hard") return "挑战档：观察 30 秒 · 记住 6 步（含先洗净双手与最后擦净台面）";
            if (diff == "normal") return "标准档：观察 24 秒 · 记住 4 个完整制作步骤";
            return "轻松档：观察 20 秒 · 记住最后 3 个关键制作步骤";
        }

        // ====================================================================
        // 3. INTERACTIVE DEMO VIEW (先看示范 - 不计成绩)
        // ====================================================================
        private void StartDemoMode(YijingGameType g)
        {
            _demoGame = g;
            _demoStepIndex = 0;
            _demoFeedback = "本轮为操作示范，不计时、不保存成绩。";
            CurrentView = YijingViewMode.Demo;
        }

        private void DrawDemoView(float sw, float sh)
        {
            Rect box = new Rect(sw * 0.5f - 360f, sh * 0.5f - 230f, 720f, 460f);
            GUI.Box(box, "", _panelStyle);

            GUILayout.BeginArea(new Rect(box.x + 24f, box.y + 20f, box.width - 48f, box.height - 40f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("先试一次 · 操作示范（不计成绩）", _titleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("返回准备页", _secondaryBtnStyle, GUILayout.Width(120f), GUILayout.Height(32f)))
            {
                CurrentView = YijingViewMode.GameSetup;
            }
            GUILayout.EndHorizontal();

            GUILayout.Label(_demoFeedback, _subtitleStyle);
            GUILayout.Space(12f);

            if (_demoStepIndex >= 99)
            {
                GUILayout.Space(40f);
                GUILayout.Label("✓ 示范完成！记住操作方法就好，这一轮不计入训练成绩。", _titleStyle);
                GUILayout.Space(24f);
                if (GUILayout.Button("返回，开始正式练习", _primaryBtnStyle, GUILayout.Height(46f)))
                {
                    CurrentView = YijingViewMode.GameSetup;
                }
                GUILayout.EndArea();
                return;
            }

            if (_demoGame == YijingGameType.Memory)
            {
                if (_demoStepIndex == 0)
                {
                    GUILayout.Label("第一步：观察 3D 场景中直接标在物品上的选择标点（例如：紫砂茶席标在正中央茶桌的【5号标点】上）。看清后点击“记住了”。", _bodyStyle);
                    GUILayout.Space(10f);
                    Rect previewR = GUILayoutUtility.GetRect(180f, 160f);
                    DrawIconSliceRect(new Rect(previewR.x + previewR.width * 0.5f - 65f, previewR.y + 8f, 130f, 130f), YijingAssetLibrary.GetIconSlice("teapot"));
                    GUI.Box(new Rect(previewR.x + previewR.width * 0.5f - 95f, previewR.y + 115f, 190f, 32f), "◉ 5号标点 · 紫砂茶席", _badgeStyle);
                    if (GUILayout.Button("记住了，开始点选物品标点", _primaryBtnStyle, GUILayout.Height(44f)))
                    {
                        _demoStepIndex = 1;
                        _demoFeedback = "请直接点击【◉ 5号标点（紫砂茶席所在位置）】完成选择。";
                    }
                }
                else
                {
                    GUILayout.Label("第二步：请选出【紫砂茶席】所在的物品标点（提示：点击 ◉ 5号标点）", _titleStyle);
                    GUILayout.Space(14f);
                    string[] demoSpots = { "◉ 1号标点 (青松)", "◉ 4号标点 (盆景)", "◉ 5号标点 (茶席)", "◉ 8号标点 (藤椅)" };
                    int[] demoZones = { 0, 3, 4, 7 };
                    GUILayout.BeginHorizontal();
                    for (int i = 0; i < demoSpots.Length; i++)
                    {
                        int z = demoZones[i];
                        if (GUILayout.Button(demoSpots[i], z == 4 ? _primaryBtnStyle : _zoneBtnStyle, GUILayout.Height(72f)))
                        {
                            if (z == 4) _demoStepIndex = 99;
                            else _demoFeedback = "没关系，请点击【◉ 5号标点 (茶席)】。";
                        }
                    }
                    GUILayout.EndHorizontal();
                }
            }
            else if (_demoGame == YijingGameType.Attention)
            {
                GUILayout.Label("请找【苹果】，在下方三张图片中轻轻点一下苹果：", _titleStyle);
                GUILayout.Space(16f);
                string[] demoKeys = { "apple", "cup", "clock" };
                string[] demoLabels = { "苹果", "茶杯", "时钟" };
                Rect rowR = GUILayoutUtility.GetRect(640f, 180f);
                for (int i = 0; i < 3; i++)
                {
                    Rect cardR = new Rect(rowR.x + i * 215f, rowR.y, 195f, 175f);
                    GUI.Box(cardR, "", _cardStyle);
                    DrawIconSliceRect(new Rect(cardR.x + 37f, cardR.y + 10f, 120f, 115f), YijingAssetLibrary.GetIconSlice(demoKeys[i]));
                    if (GUI.Button(new Rect(cardR.x + 15f, cardR.y + 132f, 165f, 34f), "选择：" + demoLabels[i], _primaryBtnStyle))
                    {
                        if (demoKeys[i] == "apple") _demoStepIndex = 99;
                        else _demoFeedback = "没关系，请点击第一张“苹果”。";
                    }
                }
            }
            else
            {
                string prompt = _demoStepIndex == 0 ? "演示顺序：先【洗净水果】，再【装入盘中】。请先选第 1 步：" : "很好！接下来请选择第 2 步【装入盘中】：";
                GUILayout.Label(prompt, _titleStyle);
                GUILayout.Space(16f);
                string[] keys = { "plate", "wash" };
                string[] labels = { "装入盘中", "洗净水果" };
                Rect rowR = GUILayoutUtility.GetRect(640f, 190f);
                for (int i = 0; i < 2; i++)
                {
                    Rect cardR = new Rect(rowR.x + 80f + i * 260f, rowR.y, 220f, 185f);
                    GUI.Box(cardR, "", _cardStyle);
                    DrawIconSliceRect(new Rect(cardR.x + 50f, cardR.y + 10f, 120f, 120f), YijingAssetLibrary.GetIconSlice(keys[i]));
                    if (GUI.Button(new Rect(cardR.x + 20f, cardR.y + 138f, 180f, 36f), labels[i], _primaryBtnStyle))
                    {
                        string expected = _demoStepIndex == 0 ? "wash" : "plate";
                        if (keys[i] == expected)
                        {
                            _demoStepIndex++;
                            if (_demoStepIndex >= 2) _demoStepIndex = 99;
                        }
                        else
                        {
                            _demoFeedback = "没关系，请按“先洗净水果，再装入盘中”的先后顺序选择。";
                        }
                    }
                }
            }

            GUILayout.EndArea();
        }

        // ====================================================================
        // 4. ACTIVE GAMEPLAY HUD (3D 场景直标交互 + 适老化大按钮 HUD)
        // ====================================================================
        private void DrawPlayingHUD(float sw, float sh)
        {
            var engine = YijingGameEngine.Instance;
            if (engine == null || engine.CurrentRun == null)
            {
                CurrentView = YijingViewMode.Home;
                return;
            }

            var run = engine.CurrentRun;

            // Top Task Status Bar
            GUI.Box(new Rect(16f, 62f, sw - 32f, 54f), "", _cardStyle);
            if (GUI.Button(new Rect(28f, 70f, 108f, 38f), "结束练习", _secondaryBtnStyle))
            {
                engine.AbortCurrentRun();
                return;
            }

            string headerTitle;
            string rightCounter;
            float titleAvailW;
            if (run.phase == YijingRunPhase.Observe)
            {
                int leftSec = Mathf.Max(0, Mathf.CeilToInt(run.observeEndTime - Time.realtimeSinceStartup));
                headerTitle = run.gameType == YijingGameType.Memory
                    ? ("记忆花园 · 请观察 3D 物品上的标点并记住位置（" + run.sceneTitle + "）")
                    : ("厨房步骤 · 请记住【" + run.taskName + "】的先后顺序");
                rightCounter = "观察倒计时：" + leftSec + " 秒";
                titleAvailW = sw - 548f;

                if (GUI.Button(new Rect(sw - 394f, 70f, 170f, 38f), "已记住，立即答题 →", _primaryBtnStyle))
                {
                    _showMemoryTargetHint = (run.mode == "practice");
                    engine.BeginQuestionsFromObserve();
                    return;
                }
            }
            else
            {
                headerTitle = run.gameType == YijingGameType.Memory
                    ? ("第 " + run.currentIndex + " / " + run.totalQuestions + " 题：请直接点击 3D 场景中【" + run.memoryOrder[run.currentIndex - 1].label + "】上的选择标点！")
                    : run.gameType == YijingGameType.Attention
                    ? ("第 " + run.currentIndex + " / " + run.totalQuestions + " 题：请找【" + run.currentAttentionTarget.label + "】")
                    : ("第 " + run.currentIndex + " / " + run.totalQuestions + " 步：请问第 " + run.currentIndex + " 步做什么？");

                if (run.gameType == YijingGameType.Attention)
                {
                    int remSec = Mathf.Max(0, Mathf.CeilToInt(run.questionDeadlineTime - Time.realtimeSinceStartup));
                    rightCounter = "本题限时：" + remSec + " 秒";
                }
                else
                {
                    rightCounter = "进度：" + run.currentIndex + " / " + run.totalQuestions;
                }
                titleAvailW = sw - 372f;
            }

            GUI.Label(new Rect(146f, 68f, Mathf.Max(320f, titleAvailW), 42f), headerTitle, _titleStyle);
            GUI.Box(new Rect(sw - 214f, 70f, 184f, 38f), rightCounter, _badgeStyle);

            if (run.gameType == YijingGameType.Memory)
            {
                DrawMemoryGardenPlayOverlay(run, sw, sh);
            }
            else if (run.gameType == YijingGameType.Attention)
            {
                DrawAttentionTrackingPlayOverlay(run, sw, sh);
            }
            else
            {
                DrawKitchenStepsPlayOverlay(run, sw, sh);
            }
        }

        private struct ProjectedMarkerLayoutItem
        {
            public MemoryAnnotationDef item;
            public string text;
            public bool isSequenceHighlight;
            public bool isTargetHint;
            public bool isHovered;
            public float objX;
            public float objY;
            public Rect rect;
            public Action onClick;
        }

        private void DrawMemoryGardenPlayOverlay(ActiveGameRunState run, float sw, float sh)
        {
            var engine = YijingGameEngine.Instance;
            var world = Yijing3DWorldBuilder.Instance;

            if (run.phase == YijingRunPhase.Observe)
            {
                float bottomPanelH = 176f;
                float maxMarkerBottomY = sh - bottomPanelH - 22f;

                // 1. Project All Scene Object Markers with Automatic 2D Anti-Overlap Layout!
                // Items in run.memoryOrder prominently display their sequence number: 【第 1 顺位】 5号 · 紫砂茶席
                var allSceneItems = (run.sceneSnapshot != null && run.sceneSnapshot.annotations != null && run.sceneSnapshot.annotations.Count > 0)
                    ? run.sceneSnapshot.annotations
                    : run.memoryOrder;

                if (world != null && world.mainCamera != null && allSceneItems != null)
                {
                    var markerEntries = new List<ProjectedMarkerLayoutItem>();
                    for (int i = 0; i < allSceneItems.Count; i++)
                    {
                        var item = allSceneItems[i];
                        int seqIdx = run.memoryOrder.FindIndex(m => m.id == item.id);
                        bool inOrder = seqIdx >= 0;
                        string badgeText = inOrder
                            ? string.Format("【第{0}顺位】{1}号 · {2}", seqIdx + 1, item.zone + 1, item.label)
                            : string.Format("◉ {0}号 · {1}", item.zone + 1, item.label);

                        if (TryBuildProjectedMarkerLayout(world, item, badgeText, inOrder, false, false, sw, sh, 18f, maxMarkerBottomY, Rect.zero, null, out var layoutItem))
                        {
                            markerEntries.Add(layoutItem);
                        }
                    }
                    ResolveAndDrawProjectedMarkers(markerEntries, 18f, sw - 18f, 122f, maxMarkerBottomY, Rect.zero);
                }

                // 2. Enlarged Bottom Item Sequence Prompt Panel (大幅放大物品顺序提示栏，清晰显示第1➔第N顺位 + 图标 + 大字号)
                Rect bottomBar = new Rect(16f, sh - bottomPanelH - 10f, sw - 32f, bottomPanelH);
                GUI.Box(bottomBar, "", _panelStyle);

                int seqCount = run.memoryOrder.Count;
                GUI.Label(
                    new Rect(30f, bottomBar.y + 8f, sw - 64f, 32f),
                    string.Format("📋 本轮需按顺序记住位置的物品（共 {0} 项 —— 请按【第 1 顺位 ➔ 第 {0} 顺位】依次观察上方 3D 物品标点）：", seqCount),
                    _seqHeaderStyle);

                float arrowW = seqCount > 5 ? 20f : 30f;
                float availW = sw - 64f - Mathf.Max(0, seqCount - 1) * arrowW;
                float cardW = availW / Mathf.Max(1, seqCount);
                float cardY = bottomBar.y + 44f;
                float cardH = 118f;

                for (int i = 0; i < seqCount; i++)
                {
                    var it = run.memoryOrder[i];
                    float cardX = 32f + i * (cardW + arrowW);
                    Rect cardR = new Rect(cardX, cardY, cardW, cardH);
                    GUI.Box(cardR, "", _cardStyle);

                    // Top sequence order banner
                    Rect bannerR = new Rect(cardR.x + 5f, cardR.y + 5f, cardR.width - 10f, 30f);
                    string bannerText = seqCount > 5
                        ? string.Format("第 {0} 顺位", i + 1)
                        : string.Format("第 {0} 顺位（第 {0} 题考）", i + 1);
                    GUI.Box(bannerR, bannerText, _seqStepBannerStyle);

                    // Item icon on the left
                    string iconKey = !string.IsNullOrEmpty(it.iconKey) ? it.iconKey : "teapot";
                    float iconSz = seqCount > 5 ? 44f : 62f;
                    DrawIconSliceRect(new Rect(cardR.x + 8f, cardR.y + 44f, iconSz, iconSz), YijingAssetLibrary.GetIconSlice(iconKey));

                    // Large item name & marker number on the right
                    float textX = cardR.x + iconSz + 14f;
                    float textW = Mathf.Max(65f, cardR.width - iconSz - 18f);
                    GUI.Label(new Rect(textX, cardR.y + 42f, textW, 34f), it.label, seqCount > 5 ? _subtitleStyle : _seqItemTitleStyle);
                    string spotLabel = seqCount > 5
                        ? string.Format("◉ {0}号标点", it.zone + 1)
                        : string.Format("位置：◉ {0}号标点", it.zone + 1);
                    GUI.Label(new Rect(textX, cardR.y + 76f, textW, 30f), spotLabel, _seqItemSpotStyle);

                    // Bold arrow between sequence cards
                    if (i < seqCount - 1)
                    {
                        GUI.Label(new Rect(cardR.xMax, cardY + 38f, arrowW, 42f), "➔", _seqArrowStyle);
                    }
                }
            }
            else
            {
                var currentItem = run.memoryOrder[run.currentIndex - 1];
                bool showHintNow = _showMemoryTargetHint || (run.mode == "practice");

                // 1. Enlarged Top-Left Prominent Target & Sequence Prompt Card (清楚显示当前是第几顺位、要选哪个物品!)
                Rect targetCard = new Rect(18f, 122f, 520f, 170f);
                GUI.Box(targetCard, "", _panelStyle);
                string iconKey = !string.IsNullOrEmpty(currentItem.iconKey) ? currentItem.iconKey : "teapot";
                DrawIconSliceRect(new Rect(targetCard.x + 16f, targetCard.y + 24f, 118f, 118f), YijingAssetLibrary.GetIconSlice(iconKey));

                GUI.Box(
                    new Rect(targetCard.x + 146f, targetCard.y + 12f, 356f, 34f),
                    string.Format("当前第 {0} / {1} 顺位  ·  请找目标物品", run.currentIndex, run.totalQuestions),
                    _badgeStyle);
                GUI.Label(new Rect(targetCard.x + 146f, targetCard.y + 48f, 356f, 40f), "【 " + currentItem.label + " 】", _hugeTargetTitleStyle);
                GUI.Label(new Rect(targetCard.x + 146f, targetCard.y + 88f, 356f, 28f), "请直接点击 3D 物品上的【 ◉ 选择标点 】完成选择", _subtitleStyle);

                string hintBtnTxt = showHintNow ? "💡 隐藏位置提示" : "💡 提示物品位置";
                if (GUI.Button(new Rect(targetCard.x + 146f, targetCard.y + 120f, 172f, 36f), hintBtnTxt, showHintNow ? _primaryBtnStyle : _secondaryBtnStyle))
                {
                    _showMemoryTargetHint = !_showMemoryTargetHint;
                    world?.SetMemoryQuestionMarkers(run.sceneSnapshot.annotations, currentItem.id, _showMemoryTargetHint || run.mode == "practice");
                }
                if (GUI.Button(new Rect(targetCard.x + 326f, targetCard.y + 120f, 172f, 36f), "不记得（跳过）", _secondaryBtnStyle))
                {
                    _showMemoryTargetHint = false;
                    engine.SubmitMemoryAnswer(null);
                    return;
                }

                // 2. Project Clickable Non-Overlapping Selection Points Directly onto Every 3D Object!
                float bottomStripH = 152f;
                float maxMarkerBottomY = sh - bottomStripH - 20f;
                var allItems = run.sceneSnapshot.annotations;

                if (world != null && world.mainCamera != null && allItems != null)
                {
                    var markerEntries = new List<ProjectedMarkerLayoutItem>();
                    for (int i = 0; i < allItems.Count; i++)
                    {
                        var item = allItems[i];
                        bool isTargetHinted = showHintNow && item.id == currentItem.id;
                        bool isHovered3D = world.HoveredMemoryItemId == item.id;
                        string badgeText = isTargetHinted
                            ? string.Format("★ {0}号标点 · {1} [点此选]", item.zone + 1, item.label)
                            : string.Format("◉ {0}号选择点 [点此选]", item.zone + 1);

                        string capturedId = item.id;
                        if (TryBuildProjectedMarkerLayout(world, item, badgeText, false, isTargetHinted, isHovered3D, sw, sh, 18f, maxMarkerBottomY, targetCard, () =>
                        {
                            _showMemoryTargetHint = false;
                            engine.SubmitMemoryAnswerByItem(capturedId);
                        }, out var layoutItem))
                        {
                            markerEntries.Add(layoutItem);
                        }
                    }

                    if (ResolveAndDrawProjectedMarkers(markerEntries, 18f, sw - 18f, 122f, maxMarkerBottomY, targetCard))
                    {
                        return;
                    }
                }

                // 3. Enlarged Bottom Sequence Progress Strip + Quick-Select Buttons
                Rect bottomStrip = new Rect(16f, sh - bottomStripH - 10f, sw - 32f, bottomStripH);
                GUI.Box(bottomStrip, "", _panelStyle);

                // Row 1: Sequence Order Progress Bar (清楚显示本轮第1➔第N顺位进度)
                int seqCount = run.memoryOrder.Count;
                float seqArrowW = seqCount > 5 ? 18f : 28f;
                float seqBoxW = (sw - 64f - Mathf.Max(0, seqCount - 1) * seqArrowW) / Mathf.Max(1, seqCount);
                for (int i = 0; i < seqCount; i++)
                {
                    var seqItem = run.memoryOrder[i];
                    int stepNum = i + 1;
                    bool isDone = stepNum < run.currentIndex;
                    bool isCurrent = stepNum == run.currentIndex;
                    string seqText;
                    if (seqCount > 5)
                    {
                        seqText = isDone
                            ? string.Format("✓ {0}.{1}", stepNum, seqItem.label)
                            : (isCurrent ? string.Format("▶ {0}.{1}", stepNum, seqItem.label) : string.Format("{0}.{1}", stepNum, seqItem.label));
                    }
                    else
                    {
                        seqText = isDone
                            ? string.Format("✓ 第{0}顺位：{1}", stepNum, seqItem.label)
                            : (isCurrent
                                ? string.Format("▶ 第{0}顺位：【{1}】", stepNum, seqItem.label)
                                : string.Format("第{0}顺位：{1}", stepNum, seqItem.label));
                    }

                    GUIStyle seqSt = isCurrent ? _markerSeqLabelStyle : (isDone ? _seqStepBannerStyle : _secondaryBtnStyle);
                    Rect sr = new Rect(32f + i * (seqBoxW + seqArrowW), bottomStrip.y + 10f, seqBoxW, 42f);
                    GUI.Box(sr, seqText, seqSt);

                    if (i < seqCount - 1)
                    {
                        GUI.Label(new Rect(sr.xMax, bottomStrip.y + 10f, seqArrowW, 42f), "➔", _seqArrowStyle);
                    }
                }

                // Row 2: Large Quick-Select Buttons for all 8 On-Object Markers
                GUI.Label(new Rect(32f, bottomStrip.y + 56f, 760f, 28f),
                    "快捷点选（直接点击上方 3D 物品上的标点，或点击下方大号标点按钮）：", _subtitleStyle);

                int count = allItems.Count;
                float btnW = (sw - 64f - (count - 1) * 10f) / Mathf.Max(1, count);
                for (int i = 0; i < count; i++)
                {
                    var item = allItems[i];
                    bool isTargetHinted = showHintNow && item.id == currentItem.id;
                    string bLabel = isTargetHinted
                        ? string.Format("★ {0}号·{1}", item.zone + 1, item.label)
                        : string.Format("◉ {0}号标点", item.zone + 1);
                    Rect br = new Rect(32f + i * (btnW + 10f), bottomStrip.y + 88f, btnW, 48f);
                    if (GUI.Button(br, bLabel, isTargetHinted ? _primaryBtnStyle : _zoneBtnStyle))
                    {
                        _showMemoryTargetHint = false;
                        engine.SubmitMemoryAnswerByItem(item.id);
                        return;
                    }
                }
            }
        }

        private void DrawScenePreviewObjectBadges(List<MemoryAnnotationDef> items, float sw, float sh, float minScreenX)
        {
            var world = Yijing3DWorldBuilder.Instance;
            if (world == null || world.mainCamera == null || items == null) return;

            var entries = new List<ProjectedMarkerLayoutItem>();
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                string text = string.Format("◉ {0}号 · {1}", item.zone + 1, item.label);
                if (TryBuildProjectedMarkerLayout(world, item, text, false, false, false, sw, sh, minScreenX, sh - 40f, Rect.zero, null, out var layoutItem))
                {
                    entries.Add(layoutItem);
                }
            }
            ResolveAndDrawProjectedMarkers(entries, minScreenX, sw - 16f, 75f, sh - 35f, Rect.zero);
        }

        private bool TryBuildProjectedMarkerLayout(
            Yijing3DWorldBuilder world,
            MemoryAnnotationDef item,
            string badgeText,
            bool isSequenceHighlight,
            bool isTargetHint,
            bool isHovered,
            float sw,
            float sh,
            float minX,
            float maxBottomY,
            Rect avoidTopLeftCard,
            Action onClick,
            out ProjectedMarkerLayoutItem layoutItem)
        {
            layoutItem = default;
            Vector3 objWorld = world.GetMemoryItemWorldPosition(item);
            Vector3 pinWorld = world.GetMemoryMarkerWorldPosition(item);

            Vector3 objScreen = world.mainCamera.WorldToScreenPoint(objWorld);
            Vector3 pinScreen = world.mainCamera.WorldToScreenPoint(pinWorld);
            if (objScreen.z <= 0.1f || pinScreen.z <= 0.1f) return false;

            float scale = Mathf.Max(0.1f, _currentUiScale);
            float objX = objScreen.x / scale;
            float objY = (Screen.height - objScreen.y) / scale;
            float pinX = pinScreen.x / scale;
            float pinY = (Screen.height - pinScreen.y) / scale;

            if (objX < minX - 20f || objX > sw - 10f || objY < 90f || objY > sh - 40f) return false;

            GUIStyle measureStyle = onClick != null ? _markerClickBtnStyle : (isSequenceHighlight ? _markerSeqLabelStyle : _markerLabelStyle);
            Vector2 measured = measureStyle.CalcSize(new GUIContent(badgeText));
            float bw = Mathf.Max(180f, measured.x + 26f);
            float bh = Mathf.Max(onClick != null ? 44f : (isSequenceHighlight ? 42f : 38f), measured.y + 10f);
            float bx = Mathf.Clamp(pinX - bw * 0.5f, minX, sw - bw - 16f);
            float by = Mathf.Clamp(pinY - bh * 0.5f, 122f, maxBottomY - bh);

            Rect r = new Rect(bx, by, bw, bh);
            if (avoidTopLeftCard.width > 0f && r.Overlaps(avoidTopLeftCard))
            {
                if (r.center.y > avoidTopLeftCard.y + avoidTopLeftCard.height * 0.45f)
                    r.y = Mathf.Min(maxBottomY - bh, avoidTopLeftCard.yMax + 10f);
                else
                    r.x = Mathf.Min(sw - bw - 16f, avoidTopLeftCard.xMax + 12f);
            }

            layoutItem = new ProjectedMarkerLayoutItem
            {
                item = item,
                text = badgeText,
                isSequenceHighlight = isSequenceHighlight,
                isTargetHint = isTargetHint,
                isHovered = isHovered,
                objX = objX,
                objY = objY,
                rect = r,
                onClick = onClick
            };
            return true;
        }

        private bool ResolveAndDrawProjectedMarkers(
            List<ProjectedMarkerLayoutItem> entries,
            float minX,
            float maxX,
            float minY,
            float maxY,
            Rect avoidRect)
        {
            if (entries == null || entries.Count == 0) return false;

            // 1. Iterative 2D Anti-Overlap Solver so zero object labels ever overlap each other!
            const float padX = 12f;
            const float padY = 10f;
            for (int iter = 0; iter < 8; iter++)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var a = entries[i];
                    if (avoidRect.width > 0f && a.rect.Overlaps(avoidRect))
                    {
                        if (a.rect.center.x < avoidRect.xMax + 20f && a.rect.center.y < avoidRect.yMax + 10f)
                        {
                            a.rect.y = Mathf.Clamp(avoidRect.yMax + 10f, minY, maxY - a.rect.height);
                        }
                    }

                    for (int j = i + 1; j < entries.Count; j++)
                    {
                        var b = entries[j];
                        float overlapX = (a.rect.width * 0.5f + b.rect.width * 0.5f + padX) - Mathf.Abs(a.rect.center.x - b.rect.center.x);
                        float overlapY = (a.rect.height * 0.5f + b.rect.height * 0.5f + padY) - Mathf.Abs(a.rect.center.y - b.rect.center.y);

                        if (overlapX > 0f && overlapY > 0f)
                        {
                            // Push apart primarily along Y (vertical staggering keeps horizontal alignment over the 3D object)
                            float pushY = overlapY * 0.55f + 2f;
                            if (a.rect.center.y <= b.rect.center.y)
                            {
                                a.rect.y -= pushY;
                                b.rect.y += pushY;
                            }
                            else
                            {
                                a.rect.y += pushY;
                                b.rect.y -= pushY;
                            }

                            // Also nudge slightly along X if their X centers are very close
                            if (Mathf.Abs(a.rect.center.x - b.rect.center.x) < 90f)
                            {
                                float pushX = 14f;
                                if (a.rect.center.x <= b.rect.center.x)
                                {
                                    a.rect.x -= pushX;
                                    b.rect.x += pushX;
                                }
                                else
                                {
                                    a.rect.x += pushX;
                                    b.rect.x -= pushX;
                                }
                            }

                            a.rect.x = Mathf.Clamp(a.rect.x, minX, maxX - a.rect.width);
                            a.rect.y = Mathf.Clamp(a.rect.y, minY, maxY - a.rect.height);
                            b.rect.x = Mathf.Clamp(b.rect.x, minX, maxX - b.rect.width);
                            b.rect.y = Mathf.Clamp(b.rect.y, minY, maxY - b.rect.height);

                            entries[i] = a;
                            entries[j] = b;
                        }
                    }
                }
            }

            // 2. First Pass: Draw clean pointer stems & object surface bullseye dots
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                float stemBottomY = Mathf.Max(e.objY, e.rect.yMax);
                float stemTopY = Mathf.Min(e.objY, e.rect.yMax);
                float stemH = Mathf.Max(8f, stemBottomY - stemTopY);
                float stemX = Mathf.Clamp(e.objX, e.rect.x + 16f, e.rect.xMax - 16f);

                GUI.DrawTexture(new Rect(stemX - 2f, stemTopY, 4f, stemH), (e.isTargetHint || e.isSequenceHighlight) ? _texPrimaryGreen : _texAccentAmber);
                GUI.Box(new Rect(e.objX - 13f, e.objY - 13f, 26f, 26f), (e.item.zone + 1).ToString(), _markerDotStyle);
            }

            // 3. Second Pass: Draw crisp, non-overlapping interactive badges on top
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                // Outer golden highlight frame
                Rect borderR = new Rect(e.rect.x - 2f, e.rect.y - 2f, e.rect.width + 4f, e.rect.height + 4f);
                GUI.DrawTexture(borderR, (e.isTargetHint || e.isHovered) ? _texPrimaryGreenHover : _texAccentAmber);

                if (e.onClick != null)
                {
                    GUIStyle btnSt = (e.isTargetHint || e.isHovered) ? _primaryBtnStyle : _markerClickBtnStyle;
                    if (GUI.Button(e.rect, e.text, btnSt))
                    {
                        e.onClick.Invoke();
                        return true;
                    }
                }
                else
                {
                    GUIStyle boxSt = e.isTargetHint ? _toastStyle : (e.isSequenceHighlight ? _markerSeqLabelStyle : _markerLabelStyle);
                    GUI.Box(e.rect, e.text, boxSt);
                }
            }

            return false;
        }

        private void DrawAttentionTrackingPlayOverlay(ActiveGameRunState run, float sw, float sh)
        {
            var engine = YijingGameEngine.Instance;
            var world = Yijing3DWorldBuilder.Instance;

            // Top Target Cue Card
            Rect cueRect = new Rect(20f, 124f, 380f, 128f);
            GUI.Box(cueRect, "", _cardStyle);
            DrawIconSliceRect(new Rect(cueRect.x + 14f, cueRect.y + 16f, 96f, 96f), YijingAssetLibrary.GetIconSlice(run.currentAttentionTarget.icon));
            GUI.Label(new Rect(cueRect.x + 124f, cueRect.y + 14f, 240f, 28f), "请寻找目标物品：", _subtitleStyle);
            GUI.Label(new Rect(cueRect.x + 124f, cueRect.y + 44f, 240f, 40f), run.currentAttentionTarget.label, _titleStyle);
            GUI.Label(new Rect(cueRect.x + 124f, cueRect.y + 86f, 240f, 28f), "可点3D移动画牌或下方卡片", _bodyStyle);

            // Lower Interactive Moving Candidate Strip
            float dockH = 220f;
            Rect dockRect = new Rect(16f, sh - dockH - 14f, sw - 32f, dockH);
            GUI.Box(dockRect, "", _panelStyle);

            int count = run.currentOptions.Count;
            float cycleSpeed = run.difficulty == "hard" ? 3.0f : (run.difficulty == "normal" ? 4.5f : 6.0f);
            float cardW = Mathf.Min(165f, (dockRect.width - 60f) / Mathf.Max(3, count) - 14f);
            float cardH = 152f;

            for (int i = 0; i < count; i++)
            {
                var opt = run.currentOptions[i];
                float baseSlotX = dockRect.x + 24f + i * ((dockRect.width - 48f) / count);
                float sway = (world != null && world.reduceMotion)
                    ? 0f
                    : Mathf.Sin(Time.realtimeSinceStartup * (Mathf.PI * 2f / cycleSpeed) + i * 1.1f) * 18f;

                Rect cardR = new Rect(baseSlotX + sway, dockRect.y + 12f, cardW, cardH);
                GUI.Box(cardR, "", _cardStyle);
                DrawIconSliceRect(new Rect(cardR.x + (cardW - 96f) * 0.5f, cardR.y + 8f, 96f, 92f), YijingAssetLibrary.GetIconSlice(opt.icon));

                if (GUI.Button(new Rect(cardR.x + 8f, cardR.y + 106f, cardW - 16f, 38f), "选择", _primaryBtnStyle))
                {
                    engine.SubmitAttentionAnswer(opt.id, false);
                    return;
                }
            }

            if (GUI.Button(new Rect(dockRect.x + dockRect.width - 216f, dockRect.y + dockH - 46f, 196f, 38f), "没找到，下一题 →", _secondaryBtnStyle))
            {
                engine.SubmitAttentionAnswer(null, false);
            }
        }

        private void DrawKitchenStepsPlayOverlay(ActiveGameRunState run, float sw, float sh)
        {
            var engine = YijingGameEngine.Instance;

            float dockH = 240f;
            Rect dockRect = new Rect(16f, sh - dockH - 14f, sw - 32f, dockH);
            GUI.Box(dockRect, "", _panelStyle);

            if (run.phase == YijingRunPhase.Observe)
            {
                GUI.Label(new Rect(dockRect.x + 18f, dockRect.y + 8f, 760f, 30f),
                    "请按顺序记住以下 " + run.kitchenRecipe.Count + " 个步骤（上方 3D 厨房画架同步展示）：", _bodyStyle);

                int n = run.kitchenRecipe.Count;
                float cardW = (dockRect.width - 36f - (n - 1) * 10f) / n;
                for (int i = 0; i < n; i++)
                {
                    var step = run.kitchenRecipe[i];
                    Rect cardR = new Rect(dockRect.x + 18f + i * (cardW + 10f), dockRect.y + 42f, cardW, 184f);
                    GUI.Box(cardR, "", _cardStyle);

                    float imgSz = Mathf.Min(110f, cardW - 20f);
                    DrawIconSliceRect(new Rect(cardR.x + (cardW - imgSz) * 0.5f, cardR.y + 34f, imgSz, imgSz), YijingAssetLibrary.GetIconSlice(step.icon));
                    GUI.Box(new Rect(cardR.x + 8f, cardR.y + 6f, 64f, 26f), "第" + (i + 1) + "步", _badgeStyle);
                    GUI.Label(new Rect(cardR.x + 8f, cardR.y + 148f, cardW - 16f, 30f), step.label, _subtitleStyle);
                }
            }
            else
            {
                GUI.Label(new Rect(dockRect.x + 18f, dockRect.y + 8f, 820f, 30f),
                    "请点击【第 " + run.currentIndex + " 步】对应的卡片（可直接点击上方 3D 厨房画架或下方卡片，每张卡片用一次）：", _bodyStyle);

                int n = run.currentOptions.Count;
                if (n == 0) return;
                float cardW = Mathf.Min(210f, (dockRect.width - 36f - (n - 1) * 12f) / n);
                for (int i = 0; i < n; i++)
                {
                    var opt = run.currentOptions[i];
                    Rect cardR = new Rect(dockRect.x + 18f + i * (cardW + 12f), dockRect.y + 42f, cardW, 184f);
                    GUI.Box(cardR, "", _cardStyle);

                    float imgSz = Mathf.Min(110f, cardW - 24f);
                    DrawIconSliceRect(new Rect(cardR.x + (cardW - imgSz) * 0.5f, cardR.y + 10f, imgSz, imgSz), YijingAssetLibrary.GetIconSlice(opt.icon));

                    if (GUI.Button(new Rect(cardR.x + 10f, cardR.y + 132f, cardW - 20f, 42f), opt.label, _primaryBtnStyle))
                    {
                        engine.SubmitKitchenAnswer(opt.id);
                        return;
                    }
                }
            }
        }

        // ====================================================================
        // 5. SINGLE SESSION RESULT VIEW (训练完成与即时认知评估卡片)
        // ====================================================================
        private void DrawResultView(float sw, float sh)
        {
            var rec = SelectedDetailRecord;
            if (rec == null)
            {
                CurrentView = YijingViewMode.Home;
                return;
            }

            Rect box = new Rect(sw * 0.5f - 420f, 70f, 840f, sh - 88f);
            GUI.Box(box, "", _panelStyle);

            GUILayout.BeginArea(new Rect(box.x + 24f, box.y + 18f, box.width - 48f, box.height - 36f));
            GUILayout.Label(rec.status == "completed" ? ("✓ " + rec.gameName + " · 练习完成") : ("— " + rec.gameName + " · 已提前结束"), _titleStyle);
            GUILayout.Label(string.Format("参与者：{0} | 任务：{1} | 难度：{2} | 模式：{3}",
                rec.participant, !string.IsNullOrEmpty(rec.taskName) ? rec.taskName : rec.sceneTitle,
                YijingZones.DifficultyLabel(rec.difficulty, rec.game == "memory"),
                rec.mode == "formal" ? "正式训练" : "练习"), _subtitleStyle);

            GUILayout.Space(10f);
            GUILayout.BeginHorizontal(_cardStyle);
            GUILayout.Label("答对 / 已答：" + rec.metrics.correct + " / " + rec.metrics.answered + " (计划 " + rec.total + " 题)", _titleStyle);
            GUILayout.Label("正确率：" + YijingCognitiveEvaluator.FormatAccuracy(rec.metrics.accuracy), _titleStyle);
            GUILayout.Label("平均反应：" + YijingCognitiveEvaluator.FormatReaction(rec.metrics.meanReactionMs), _titleStyle);
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("逐题作答明细：", _bodyStyle);
            _detailScroll = GUILayout.BeginScrollView(_detailScroll, GUILayout.Height(210f));
            foreach (var r in rec.responses)
            {
                string statusTxt = r.correct ? "【正确】" : (r.omitted ? "【超时遗漏】" : (string.IsNullOrEmpty(r.choice) ? "【主动跳过】" : "【错误】"));
                GUILayout.BeginHorizontal(_cardStyle);
                GUILayout.Label(string.Format("第 {0} 题 · 目标：{1}", r.index, r.label), _bodyStyle, GUILayout.Width(220f));
                GUILayout.Label(string.Format("你的选择：{0} ({1:F1}秒)", r.choiceLabel, r.reactionMs / 1000f), _bodyStyle, GUILayout.Width(240f));
                GUILayout.Label(statusTxt, _bodyStyle);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("再练一次", _primaryBtnStyle, GUILayout.Height(42f)))
            {
                CurrentView = YijingViewMode.GameSetup;
            }
            if (GUILayout.Button("查看五维认知评估画像 →", _primaryBtnStyle, GUILayout.Height(42f)))
            {
                UseSimulationDataInRecords = false;
                FilterParticipant = rec.participant;
                RefreshAssessmentProfile();
                CurrentView = YijingViewMode.Assessment;
            }
            if (GUILayout.Button("回到首页", _secondaryBtnStyle, GUILayout.Height(42f)))
            {
                CurrentView = YijingViewMode.Home;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ====================================================================
        // 6. DAILY TRAINING 3-IN-1 ORCHESTRATION (今日训练设置与汇总)
        // ====================================================================
        private void DrawDailySetupView(float sw, float sh)
        {
            var engine = YijingGameEngine.Instance;
            Rect box = new Rect(sw * 0.5f - 410f, 70f, 820f, sh - 92f);
            GUI.Box(box, "", _panelStyle);

            GUILayout.BeginArea(new Rect(box.x + 24f, box.y + 18f, box.width - 48f, box.height - 36f));
            GUILayout.Label("今日训练 · 三项认知任务连续练习", _titleStyle);
            GUILayout.Label("按顺序完成：① 记忆花园 → ② 注意力追踪 → ③ 厨房步骤。单项之间可随时休息，完成后生成综合认知评估报告。", _subtitleStyle);
            GUILayout.Space(10f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("参与者代号：", _bodyStyle, GUILayout.Width(110f), GUILayout.Height(34f));
            engine.PreferredParticipant = GUILayout.TextField(engine.PreferredParticipant ?? "P01", 24, GUILayout.Height(34f));
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("统一难度档位：", _bodyStyle);
            GUILayout.BeginHorizontal();
            foreach (var d in new[] { ("easy", "轻松"), ("normal", "标准"), ("hard", "挑战") })
            {
                if (GUILayout.Button(d.Item2, engine.PreferredDifficulty == d.Item1 ? _primaryBtnStyle : _secondaryBtnStyle, GUILayout.Height(38f)))
                    engine.PreferredDifficulty = d.Item1;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("1. 记忆花园 3D 场景：" + (YijingAssetLibrary.Scenes.FirstOrDefault(s => s.id == engine.SelectedMemorySceneId)?.title ?? ""), _bodyStyle);
            GUILayout.BeginHorizontal();
            foreach (var sc in YijingAssetLibrary.Scenes)
            {
                if (GUILayout.Button(sc.title.Split('·')[0].Trim(), engine.SelectedMemorySceneId == sc.id ? _primaryBtnStyle : _secondaryBtnStyle, GUILayout.Height(38f)))
                    engine.SelectedMemorySceneId = sc.id;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            var attTask = YijingAssetLibrary.Catalog.attention.FirstOrDefault(t => t.id == engine.SelectedAttentionTaskId) ?? YijingAssetLibrary.Catalog.attention[0];
            GUILayout.Label("2. 注意力追踪主题：" + attTask.name, _bodyStyle);
            GUILayout.BeginHorizontal();
            foreach (var t in YijingAssetLibrary.Catalog.attention.Take(5))
            {
                if (GUILayout.Button(t.name, engine.SelectedAttentionTaskId == t.id ? _primaryBtnStyle : _secondaryBtnStyle, GUILayout.Height(38f)))
                    engine.SelectedAttentionTaskId = t.id;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            var kitTask = YijingAssetLibrary.Catalog.kitchen.FirstOrDefault(t => t.id == engine.SelectedKitchenTaskId) ?? YijingAssetLibrary.Catalog.kitchen[0];
            GUILayout.Label("3. 厨房步骤任务：" + kitTask.name, _bodyStyle);
            GUILayout.BeginHorizontal();
            foreach (var t in YijingAssetLibrary.Catalog.kitchen.Take(5))
            {
                if (GUILayout.Button(t.name, engine.SelectedKitchenTaskId == t.id ? _primaryBtnStyle : _secondaryBtnStyle, GUILayout.Height(38f)))
                    engine.SelectedKitchenTaskId = t.id;
            }
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("▶ 准备好了，开始今日训练", _primaryBtnStyle, GUILayout.Height(46f)))
            {
                engine.ActiveDailySession = new DailyTrainingSession
                {
                    id = Guid.NewGuid().ToString("N"),
                    startedAt = DateTime.UtcNow.ToString("o"),
                    participant = engine.PreferredParticipant,
                    difficulty = engine.PreferredDifficulty,
                    sceneId = engine.SelectedMemorySceneId,
                    attentionTaskId = engine.SelectedAttentionTaskId,
                    kitchenTaskId = engine.SelectedKitchenTaskId
                };
                engine.IsDailyFlowActive = true;
                ResumeDailyTrainingNextItem();
            }
            if (GUILayout.Button("取消并返回首页", _secondaryBtnStyle, GUILayout.Width(170f), GUILayout.Height(46f)))
            {
                CurrentView = YijingViewMode.Home;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void ResumeDailyTrainingNextItem()
        {
            var engine = YijingGameEngine.Instance;
            if (engine.ActiveDailySession == null)
            {
                CurrentView = YijingViewMode.DailySetup;
                return;
            }

            var dailyRows = engine.GetLocalRecords().Where(r => r.dailyId == engine.ActiveDailySession.id && r.status == "completed").ToList();
            var doneGames = new HashSet<string>(dailyRows.Select(r => r.game ?? "memory"));

            engine.IsDailyFlowActive = true;
            if (!doneGames.Contains("memory"))
            {
                SelectedSetupGame = YijingGameType.Memory;
                engine.StartMemoryGarden(engine.ActiveDailySession.sceneId, engine.ActiveDailySession.difficulty, "formal", engine.ActiveDailySession.participant, engine.ActiveDailySession.id);
                CurrentView = YijingViewMode.Playing;
            }
            else if (!doneGames.Contains("attention"))
            {
                SelectedSetupGame = YijingGameType.Attention;
                engine.StartAttentionTracking(engine.ActiveDailySession.attentionTaskId, engine.ActiveDailySession.difficulty, "formal", engine.ActiveDailySession.participant, engine.ActiveDailySession.id);
                CurrentView = YijingViewMode.Playing;
            }
            else if (!doneGames.Contains("kitchen"))
            {
                SelectedSetupGame = YijingGameType.Kitchen;
                engine.StartKitchenSteps(engine.ActiveDailySession.kitchenTaskId, engine.ActiveDailySession.difficulty, "formal", engine.ActiveDailySession.participant, engine.ActiveDailySession.id);
                CurrentView = YijingViewMode.Playing;
            }
            else
            {
                CurrentView = YijingViewMode.DailySummary;
            }
        }

        private void DrawDailySummaryView(float sw, float sh)
        {
            var engine = YijingGameEngine.Instance;
            var daily = engine.ActiveDailySession;
            if (daily == null)
            {
                CurrentView = YijingViewMode.Home;
                return;
            }

            var rows = engine.GetLocalRecords().Where(r => r.dailyId == daily.id).ToList();
            var doneSet = new HashSet<string>(rows.Where(r => r.status == "completed").Select(r => r.game ?? "memory"));

            Rect box = new Rect(sw * 0.5f - 430f, 70f, 860f, sh - 90f);
            GUI.Box(box, "", _panelStyle);

            GUILayout.BeginArea(new Rect(box.x + 24f, box.y + 18f, box.width - 48f, box.height - 36f));
            GUILayout.Label(doneSet.Count == 3 ? "🎉 今日三合一训练全部完成！" : ("今日训练进度：已完成 " + doneSet.Count + " / 3 项"), _titleStyle);
            GUILayout.Label("参与者：" + daily.participant + " · 难度：" + YijingZones.DifficultyLabel(daily.difficulty) + " · 三种游戏独立记录，同时生成多维认知评估画像。", _subtitleStyle);
            GUILayout.Space(12f);

            GUILayout.BeginHorizontal();
            foreach (var g in new[] { ("memory", "记忆花园"), ("attention", "注意力追踪"), ("kitchen", "厨房步骤") })
            {
                var rec = rows.FirstOrDefault(r => (r.game ?? "memory") == g.Item1 && r.status == "completed")
                       ?? rows.FirstOrDefault(r => (r.game ?? "memory") == g.Item1);
                GUILayout.BeginVertical(_cardStyle, GUILayout.Width(258f), GUILayout.Height(148f));
                GUILayout.Label(g.Item2, _titleStyle);
                if (rec != null)
                {
                    GUILayout.Label("答对：" + rec.metrics.correct + " / " + rec.metrics.answered, _titleStyle);
                    GUILayout.Label("正确率：" + YijingCognitiveEvaluator.FormatAccuracy(rec.metrics.accuracy) + " · 用时：" + YijingCognitiveEvaluator.FormatReaction(rec.metrics.meanReactionMs), _bodyStyle);
                    GUILayout.Label(rec.status == "completed" ? "状态：已完成" : "状态：中途结束", _subtitleStyle);
                }
                else
                {
                    GUILayout.Label("待完成", _subtitleStyle);
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();

            if (rows.Count > 0)
            {
                var dailyProfile = YijingCognitiveEvaluator.EvaluateRecords(rows, daily.participant);
                GUILayout.Space(10f);
                GUILayout.BeginVertical(_cardStyle);
                GUILayout.Label("本次训练即时认知评估：" + dailyProfile.gradeLevel + "（综合指数 " + dailyProfile.compositeCognitiveIndex.ToString("F1") + "）", _titleStyle);
                GUILayout.Label(dailyProfile.trainingPrescription, _bodyStyle);
                GUILayout.EndVertical();
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (doneSet.Count < 3)
            {
                if (GUILayout.Button("▶ 继续下一项训练", _primaryBtnStyle, GUILayout.Height(44f)))
                {
                    ResumeDailyTrainingNextItem();
                }
            }
            if (GUILayout.Button("📄 导出本次认知评估报告 (HTML/PDF)", _primaryBtnStyle, GUILayout.Height(44f)))
            {
                string path = YijingCognitiveEvaluator.ExportHtmlAssessmentReport(rows, "今日三合一训练认知评估报告");
                ShowToast("报告已导出至：" + path);
            }
            if (GUILayout.Button("结束并回首页", _secondaryBtnStyle, GUILayout.Height(44f)))
            {
                if (doneSet.Count == 3) engine.ActiveDailySession = null;
                engine.IsDailyFlowActive = false;
                CurrentView = YijingViewMode.Home;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ====================================================================
        // 7. COGNITIVE ASSESSMENT & SCORE STATISTICS DASHBOARD (认知评估与分数统计中心)
        // ====================================================================
        private void DrawCognitiveAssessmentDashboard(float sw, float sh)
        {
            if (CachedProfile == null) RefreshAssessmentProfile();
            var p = CachedProfile;

            Rect area = new Rect(16f, 64f, sw - 32f, sh - 76f);
            GUI.Box(area, "", _panelStyle);

            GUILayout.BeginArea(new Rect(area.x + 18f, area.y + 12f, area.width - 36f, area.height - 24f));

            // Header & Data Source Switcher
            GUILayout.BeginHorizontal();
            GUILayout.Label("认知功能评估与游戏分数统计中心", _titleStyle);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(UseSimulationDataInRecords ? "当前数据源：450条模拟库 (点击切换实际记录)" : "当前数据源：实际训练记录 (点击切换450条模拟库)", _secondaryBtnStyle, GUILayout.Height(36f)))
            {
                UseSimulationDataInRecords = !UseSimulationDataInRecords;
                FilterParticipant = "*";
                RefreshAssessmentProfile();
            }
            if (GUILayout.Button("📄 导出认知评估报告 (HTML/PDF)", _primaryBtnStyle, GUILayout.Height(36f)))
            {
                string path = YijingCognitiveEvaluator.ExportHtmlAssessmentReport(GetFilteredRecords(), "五维认知功能评估与统计报告");
                ShowToast("已导出评估报告：" + path);
            }
            if (GUILayout.Button("📊 导出 CSV 统计表", _secondaryBtnStyle, GUILayout.Height(36f)))
            {
                string path = YijingCognitiveEvaluator.ExportCsvRecords(GetFilteredRecords());
                ShowToast("已导出 CSV 表格：" + path);
            }
            GUILayout.EndHorizontal();

            // Participant Filter Bar
            DrawParticipantFilterBar();

            _assessmentScroll = GUILayout.BeginScrollView(_assessmentScroll);

            // Card 1: Overall Cognitive Grade & 5-Domain Quantitative Bars
            GUILayout.BeginVertical(_cardStyle);
            GUILayout.Label(string.Format("一、五维认知能力评估画像 — {0}  【综合指数：{1:F1} / 100】", p.gradeLevel, p.compositeCognitiveIndex), _titleStyle);
            GUILayout.Label(string.Format("评估范围：{0} | 训练日期：{1} | 纳入记录：{2} 条（已完成 {3} 条） | 优势维度：{4} | 待强化维度：{5}",
                p.participantFilter, p.dateRangeText, p.totalRecords, p.completedRecords, p.strongestDomainName, p.weakestDomainName), _subtitleStyle);
            GUILayout.Space(8f);

            DrawDomainScoreBar("1. 空间位置记忆 (记忆花园·物品标点回忆)", p.spatialMemoryScore,
                string.Format("完成 {0} 次 | 正确率 {1} | 平均反应 {2} | 跳过 {3} 次",
                    p.memorySummary.completedSessions, YijingCognitiveEvaluator.FormatAccuracy(p.memorySummary.meanAccuracy),
                    YijingCognitiveEvaluator.FormatReaction(p.memorySummary.meanReactionMs), p.memorySummary.totalSkipped));

            DrawDomainScoreBar("2. 视觉选择性注意 (注意力追踪·动态目标搜索)", p.selectiveAttentionScore,
                string.Format("完成 {0} 次 | 命中率 {1} | 平均反应 {2} | 误选 {3} · 超时遗漏 {4}",
                    p.attentionSummary.completedSessions, YijingCognitiveEvaluator.FormatAccuracy(p.attentionSummary.meanAccuracy),
                    YijingCognitiveEvaluator.FormatReaction(p.attentionSummary.meanReactionMs), p.attentionSummary.totalErrors, p.attentionSummary.totalOmissions));

            DrawDomainScoreBar("3. 执行功能与序列规划 (厨房步骤·工序记忆排序)", p.executivePlanningScore,
                string.Format("完成 {0} 次 | 顺序正确率 {1} | 平均反应 {2} | 顺序错误 {3} 次",
                    p.kitchenSummary.completedSessions, YijingCognitiveEvaluator.FormatAccuracy(p.kitchenSummary.meanAccuracy),
                    YijingCognitiveEvaluator.FormatReaction(p.kitchenSummary.meanReactionMs), p.kitchenSummary.totalErrors));

            DrawDomainScoreBar("4. 信息加工与反应速度 (基于逆效率指数 IES = RT/Acc)", p.processingSpeedScore,
                "综合反映在保证作答准确率前提下的视觉搜索、空间提取与决策反应快慢");

            DrawDomainScoreBar("5. 反应时稳定性与任务依从性 (基于反应时变异系数 CV 与完成率)", p.behavioralStabilityScore,
                string.Format("任务完成率 {0}% | 反映连续认知操作过程中的注意力维持稳定度",
                    Mathf.RoundToInt(100f * p.completedRecords / Mathf.Max(1, p.totalRecords))));

            GUILayout.EndVertical();

            // Card 2: Biomarker Interpretation & One-Click Targeted Intervention Prescription
            GUILayout.BeginVertical(_cardStyle);
            GUILayout.Label("二、数字行为指标分析与个性化精准干预处方", _titleStyle);
            GUILayout.Label(p.biomarkerInterpretation, _bodyStyle);
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(p.trainingPrescription, _titleStyle);
            if (GUILayout.Button("▶ 按处方立即开始针对性强化训练", _primaryBtnStyle, GUILayout.Width(268f), GUILayout.Height(40f)))
            {
                if (p.weakestDomainName != null && p.weakestDomainName.Contains("空间"))
                    SelectedSetupGame = YijingGameType.Memory;
                else if (p.weakestDomainName != null && p.weakestDomainName.Contains("注意"))
                    SelectedSetupGame = YijingGameType.Attention;
                else
                    SelectedSetupGame = YijingGameType.Kitchen;
                CurrentView = YijingViewMode.GameSetup;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            // Card 3: Same-Condition Longitudinal Trend Analysis (同任务近期表现趋势)
            GUILayout.BeginVertical(_cardStyle);
            GUILayout.Label("三、同条件近期表现纵向趋势（仅比较同一参与者、任务、难度、题数与观察时间一致的已完成记录）", _titleStyle);
            if (p.trendGroups != null && p.trendGroups.Count > 0)
            {
                SelectedTrendGroupIndex = Mathf.Clamp(SelectedTrendGroupIndex, 0, p.trendGroups.Count - 1);
                GUILayout.BeginHorizontal();
                GUILayout.Label("选择比较条件组（共 " + p.trendGroups.Count + " 组）：", _bodyStyle, GUILayout.Width(195f), GUILayout.Height(34f));
                for (int i = 0; i < Mathf.Min(4, p.trendGroups.Count); i++)
                {
                    var grp = p.trendGroups[i];
                    if (GUILayout.Button(grp.participant + "·" + grp.taskTitle + "(" + grp.sessions.Count + "次)",
                        SelectedTrendGroupIndex == i ? _primaryBtnStyle : _secondaryBtnStyle, GUILayout.Height(34f)))
                    {
                        SelectedTrendGroupIndex = i;
                    }
                }
                GUILayout.EndHorizontal();

                var activeGrp = p.trendGroups[SelectedTrendGroupIndex];
                GUILayout.Label("当前组：" + activeGrp.groupTitle + "（展示最近 " + Mathf.Min(5, activeGrp.sessions.Count) + " 次训练表现）", _subtitleStyle);
                foreach (var s in activeGrp.sessions.Skip(Mathf.Max(0, activeGrp.sessions.Count - 5)))
                {
                    float acc = Mathf.Clamp01(s.metrics.accuracy);
                    DrawDomainScoreBar(
                        YijingCognitiveEvaluator.ExtractDateDay(s.startedAt) + " (" + s.metrics.correct + "/" + s.metrics.answered + ")",
                        acc * 100f,
                        "平均反应：" + YijingCognitiveEvaluator.FormatReaction(s.metrics.meanReactionMs));
                }
            }
            else
            {
                GUILayout.Label("当前筛选条件下暂无已完成的同条件记录组。", _subtitleStyle);
            }
            GUILayout.EndVertical();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawDomainScoreBar(string label, float score0To100, string rightNote)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _bodyStyle, GUILayout.Width(350f), GUILayout.Height(26f));

            Rect barRect = GUILayoutUtility.GetRect(220f, 24f, GUILayout.Width(220f));
            GUI.DrawTexture(new Rect(barRect.x, barRect.y + 5f, barRect.width, 14f), _texBarBg);
            float fillW = barRect.width * Mathf.Clamp01(score0To100 / 100f);
            GUI.DrawTexture(new Rect(barRect.x, barRect.y + 5f, fillW, 14f), _texBarFill);

            GUILayout.Label(score0To100.ToString("F1") + " 分", _bodyStyle, GUILayout.Width(72f), GUILayout.Height(26f));
            GUILayout.Label(rightNote, _subtitleStyle, GUILayout.Height(26f));
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }

        private void DrawParticipantFilterBar()
        {
            var engine = YijingGameEngine.Instance;
            var allSource = UseSimulationDataInRecords ? engine.GetSimulationRecords() : engine.GetLocalRecords();
            var participants = allSource.Select(r => r.participant ?? "").Where(x => !string.IsNullOrEmpty(x)).Distinct().Take(10).ToList();

            GUILayout.BeginHorizontal(_cardStyle);
            GUILayout.Label("按参与者筛选：", _bodyStyle, GUILayout.Width(108f), GUILayout.Height(34f));
            if (GUILayout.Button("全部(" + allSource.Count + ")", FilterParticipant == "*" ? _primaryBtnStyle : _secondaryBtnStyle, GUILayout.Height(34f)))
            {
                FilterParticipant = "*";
                RefreshAssessmentProfile();
            }
            foreach (var person in participants)
            {
                if (GUILayout.Button(person, FilterParticipant == person ? _primaryBtnStyle : _secondaryBtnStyle, GUILayout.Height(34f)))
                {
                    FilterParticipant = person;
                    RefreshAssessmentProfile();
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label("游戏筛选：", _bodyStyle, GUILayout.Width(80f), GUILayout.Height(34f));
            foreach (var g in new[] { ("", "全部"), ("memory", "记忆花园"), ("attention", "注意力"), ("kitchen", "厨房") })
            {
                if (GUILayout.Button(g.Item2, FilterGame == g.Item1 ? _primaryBtnStyle : _secondaryBtnStyle, GUILayout.Height(34f)))
                {
                    FilterGame = g.Item1;
                    RefreshAssessmentProfile();
                }
            }
            GUILayout.EndHorizontal();
        }

        // ====================================================================
        // 8. TRAINING RECORDS LIST & DETAIL VIEW (训练记录与模拟数据浏览)
        // ====================================================================
        private void DrawRecordsListView(float sw, float sh)
        {
            Rect area = new Rect(16f, 64f, sw - 32f, sh - 76f);
            GUI.Box(area, "", _panelStyle);

            GUILayout.BeginArea(new Rect(area.x + 18f, area.y + 12f, area.width - 36f, area.height - 24f));
            GUILayout.BeginHorizontal();
            GUILayout.Label(UseSimulationDataInRecords ? "模拟训练记录库（450条虚拟受试者数据 · 非真实实测）" : "本地训练记录管理", _titleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(UseSimulationDataInRecords ? "切换至：实际训练记录" : "切换至：模拟演示记录 (450条)", _secondaryBtnStyle, GUILayout.Height(36f)))
            {
                UseSimulationDataInRecords = !UseSimulationDataInRecords;
                FilterParticipant = "*";
                RefreshAssessmentProfile();
            }
            if (GUILayout.Button("查看当前筛选的认知评估画像 →", _primaryBtnStyle, GUILayout.Height(36f)))
            {
                RefreshAssessmentProfile();
                CurrentView = YijingViewMode.Assessment;
            }
            GUILayout.EndHorizontal();

            DrawParticipantFilterBar();

            var filtered = GetFilteredRecords();
            GUILayout.Label(string.Format("共筛选出 {0} 条记录 · 已完成 {1} 条", filtered.Count, filtered.Count(r => r.status == "completed")), _subtitleStyle);

            _mainScroll = GUILayout.BeginScrollView(_mainScroll);
            foreach (var r in filtered.Take(60))
            {
                GUILayout.BeginHorizontal(_cardStyle);
                string ageNote = r.synthetic ? (" · 虚拟年龄 " + r.simulatedAge + "岁") : "";
                GUILayout.Label(YijingCognitiveEvaluator.ExtractDateDay(r.startedAt) + "  " + (r.participant ?? "未填写") + ageNote, _bodyStyle, GUILayout.Width(215f), GUILayout.Height(32f));
                GUILayout.Label(r.gameName + " · " + (!string.IsNullOrEmpty(r.taskName) ? r.taskName : r.sceneTitle), _bodyStyle, GUILayout.Width(240f), GUILayout.Height(32f));
                GUILayout.Label(YijingZones.DifficultyLabel(r.difficulty, r.game == "memory") + " / " + (r.mode == "formal" ? "正式" : "练习"), _bodyStyle, GUILayout.Width(115f), GUILayout.Height(32f));
                GUILayout.Label("答对 " + r.metrics.correct + "/" + r.metrics.answered + " (" + YijingCognitiveEvaluator.FormatAccuracy(r.metrics.accuracy) + ")", _bodyStyle, GUILayout.Width(155f), GUILayout.Height(32f));
                GUILayout.Label("反应 " + YijingCognitiveEvaluator.FormatReaction(r.metrics.meanReactionMs), _bodyStyle, GUILayout.Width(125f), GUILayout.Height(32f));

                if (GUILayout.Button("查看逐题详情", _secondaryBtnStyle, GUILayout.Width(118f), GUILayout.Height(34f)))
                {
                    SelectedDetailRecord = r;
                    CurrentView = YijingViewMode.RecordDetail;
                }
                if (!r.synthetic)
                {
                    if (GUILayout.Button("删除", _secondaryBtnStyle, GUILayout.Width(68f), GUILayout.Height(34f)))
                    {
                        YijingGameEngine.Instance.DeleteLocalRecord(r.id);
                        RefreshAssessmentProfile();
                        break;
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawRecordDetailView(float sw, float sh)
        {
            DrawResultView(sw, sh);
        }

        // ====================================================================
        // 9. 3D SCENE & ANNOTATION STUDIO (纯 3D 场景预览与物品直标工作台)
        // ====================================================================
        private void DrawSceneStudioView(float sw, float sh)
        {
            var scenes = YijingAssetLibrary.Scenes;
            _studioSceneIndex = Mathf.Clamp(_studioSceneIndex, 0, scenes.Count - 1);
            var scene = scenes[_studioSceneIndex];

            // Project crisp on-object markers onto the right 3D viewport
            DrawScenePreviewObjectBadges(scene.annotations, sw, sh, 470f);

            float panelW = 445f;
            Rect leftPanel = new Rect(16f, 64f, panelW, sh - 78f);
            GUI.Box(leftPanel, "", _panelStyle);

            GUILayout.BeginArea(new Rect(leftPanel.x + 16f, leftPanel.y + 14f, leftPanel.width - 32f, leftPanel.height - 28f));
            GUILayout.Label("纯 3D 开源场景建模与物品直标管理", _titleStyle);
            GUILayout.Label("右侧实时展示纯 3D 建模场景（已移除背景图与九宫格），所有选择标点直接锚定在 3D 物品模型上。", _subtitleStyle);
            GUILayout.Space(8f);

            GUILayout.BeginHorizontal();
            for (int i = 0; i < scenes.Count; i++)
            {
                if (GUILayout.Button(scenes[i].title.Split('·')[0].Trim(), _studioSceneIndex == i ? _primaryBtnStyle : _secondaryBtnStyle, GUILayout.Height(36f)))
                {
                    _studioSceneIndex = i;
                    scene = scenes[i];
                    Yijing3DWorldBuilder.Instance?.ShowMemoryStage(scene.theme, scene.textureResourceName);
                    Yijing3DWorldBuilder.Instance?.SetMemoryObserveMarkers(scene.annotations, true);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("当前场景：" + scene.title + "（共 " + scene.annotations.Count + " 个 3D 物品直标点）", _bodyStyle);

            _mainScroll = GUILayout.BeginScrollView(_mainScroll, GUILayout.Height(250f));
            for (int i = 0; i < scene.annotations.Count; i++)
            {
                var a = scene.annotations[i];
                GUILayout.BeginHorizontal(_cardStyle);
                GUILayout.Label(string.Format("◉ {0}号标点：{1}  (3D坐标: {2:F1}, {3:F1}, {4:F1})",
                    a.zone + 1, a.label, a.worldLocalPos.x, a.worldLocalPos.y, a.worldLocalPos.z), _bodyStyle, GUILayout.Height(30f));
                if (scene.annotations.Count > 3)
                {
                    if (GUILayout.Button("移除", _secondaryBtnStyle, GUILayout.Width(68f), GUILayout.Height(32f)))
                    {
                        scene.annotations.RemoveAt(i);
                        scene.revision++;
                        Yijing3DWorldBuilder.Instance?.SetMemoryObserveMarkers(scene.annotations, true);
                        break;
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            if (scene.annotations.Count < 8)
            {
                GUILayout.Space(6f);
                GUILayout.Label("添加新标注物品（最多 8 个）：", _bodyStyle);
                GUILayout.BeginHorizontal();
                _newAnnotationLabel = GUILayout.TextField(_newAnnotationLabel, 16, GUILayout.Width(140f), GUILayout.Height(34f));
                if (GUILayout.Button("位置:" + YijingZones.ShortZoneNames[_newAnnotationZone], _secondaryBtnStyle, GUILayout.Height(34f)))
                {
                    _newAnnotationZone = (_newAnnotationZone + 1) % 9;
                }
                if (GUILayout.Button("+ 添加标注", _primaryBtnStyle, GUILayout.Height(34f)))
                {
                    if (!string.IsNullOrWhiteSpace(_newAnnotationLabel) && scene.annotations.All(x => x.label != _newAnnotationLabel.Trim()))
                    {
                        float cx = ((_newAnnotationZone % 3) + 0.5f) / 3f;
                        float cy = ((_newAnnotationZone / 3) + 0.5f) / 3f;
                        scene.annotations.Add(new MemoryAnnotationDef
                        {
                            id = Guid.NewGuid().ToString("N").Substring(0, 6),
                            label = _newAnnotationLabel.Trim(),
                            x = cx,
                            y = cy,
                            zone = _newAnnotationZone
                        });
                        scene.revision++;
                        Yijing3DWorldBuilder.Instance?.SetMemoryObserveMarkers(scene.annotations, true);
                        ShowToast("已添加物品标注：" + _newAnnotationLabel);
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("▶ 使用当前 3D 场景开始【记忆花园】训练", _primaryBtnStyle, GUILayout.Height(44f)))
            {
                YijingGameEngine.Instance.SelectedMemorySceneId = scene.id;
                SelectedSetupGame = YijingGameType.Memory;
                CurrentView = YijingViewMode.GameSetup;
            }
            GUILayout.EndArea();
        }

        // ====================================================================
        // HELPER: DRAW SLICED ICON TEXTURE
        // ====================================================================
        private static void DrawIconSliceRect(Rect rect, IconAtlasSlice slice)
        {
            if (slice.sheetTexture == null) return;
            GUI.DrawTextureWithTexCoords(rect, slice.sheetTexture, slice.uvRect, true);
        }

        // ====================================================================
        // SEED INITIAL REALISTIC TRAINING SESSIONS FOR IMMEDIATE ASSESSMENT DEMO
        // ====================================================================
        private void SeedSampleLocalRecords()
        {
            var engine = YijingGameEngine.Instance;
            if (engine == null) return;

            // Create a complete 3-game Daily Training set for P01 so the local assessment dashboard is rich right away
            string dailyId = Guid.NewGuid().ToString("N");
            engine.StartMemoryGarden("courtyard-main", "normal", "formal", "P01", dailyId);
            engine.BeginQuestionsFromObserve();
            int memTotal = engine.CurrentRun.totalQuestions;
            for (int i = 0; i < memTotal; i++)
            {
                int correctZ = engine.CurrentRun.memoryOrder[i].zone;
                engine.SubmitMemoryAnswer(i == memTotal - 1 ? (correctZ + 1) % 9 : correctZ);
            }

            engine.StartAttentionTracking("fruit", "normal", "formal", "P01", dailyId);
            int attTotal = engine.CurrentRun.totalQuestions;
            for (int i = 0; i < attTotal; i++)
            {
                string cid = engine.CurrentRun.currentCorrectOptionId;
                engine.SubmitAttentionAnswer(i == 2 ? null : cid, false);
            }

            engine.StartKitchenSteps("tea", "normal", "formal", "P01", dailyId);
            engine.BeginQuestionsFromObserve();
            var steps = new List<KitchenStepDef>(engine.CurrentRun.kitchenRecipe);
            foreach (var st in steps)
            {
                engine.SubmitKitchenAnswer(st.id);
            }
        }
    }
}
