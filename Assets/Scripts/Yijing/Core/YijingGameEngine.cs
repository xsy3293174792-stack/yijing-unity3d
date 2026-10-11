using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Yijing.Analytics;
using Yijing.Data;
using Yijing.World3D;

namespace Yijing.Core
{
    public class ActiveGameRunState
    {
        public string runId;
        public string dailyId;
        public YijingGameType gameType;
        public string gameCode;
        public string gameName;
        public string difficulty = "easy";
        public string mode = "practice";
        public string participant = "P01";
        public YijingRunPhase phase = YijingRunPhase.Idle;

        public string sceneId;
        public string sceneTitle;
        public MemorySceneDef sceneSnapshot;
        public List<MemoryAnnotationDef> memoryOrder = new List<MemoryAnnotationDef>();

        public string taskId;
        public string taskName;
        public List<AttentionItemDef> attentionThemeItems = new List<AttentionItemDef>();
        public List<KitchenStepDef> kitchenRecipe = new List<KitchenStepDef>();
        public List<string> kitchenSelectedIds = new List<string>();

        public int totalQuestions;
        public int currentIndex; // 1-based
        public int observationSeconds;
        public float observeEndTime;
        public int deadlineSeconds;
        public float questionStartTime;
        public float questionDeadlineTime;
        public float runStartTime;
        public string startedAtIso;

        public AttentionItemDef currentAttentionTarget;
        public string currentCorrectOptionId;
        public List<QuestionOptionRuntime> currentOptions = new List<QuestionOptionRuntime>();

        public List<QuestionResponseRecord> responses = new List<QuestionResponseRecord>();
        public List<SessionEventRecord> events = new List<SessionEventRecord>();
        public string lastFeedbackMessage = "";
        public float lastFeedbackUntil = 0f;
    }

    public class YijingGameEngine : MonoBehaviour
    {
        public static YijingGameEngine Instance { get; private set; }

        public ActiveGameRunState CurrentRun { get; private set; }
        public DailyTrainingSession ActiveDailySession { get; set; }
        public bool IsDailyFlowActive { get; set; }

        // User preferences shared across games
        public string PreferredParticipant = "P01";
        public string PreferredDifficulty = "normal";
        public string PreferredMode = "formal";
        public string SelectedMemorySceneId = "courtyard-main";
        public string SelectedAttentionTaskId = "fruit";
        public string SelectedKitchenTaskId = "tea";
        public bool VoiceEnabled = true;
        public string LastSpokenPrompt = "欢迎来到忆境 3D 认知训练与评估系统";

        private readonly List<TrainingSessionRecord> _localRecords = new List<TrainingSessionRecord>();
        private readonly HashSet<string> _deletedRecordIds = new HashSet<string>();
        private Vector3 _lastMousePos;
        private float _accumulatedCursorPathPx;
        private System.Random _rng = new System.Random();

        public event Action<TrainingSessionRecord> OnRunFinished;
        public event Action<string> OnToastMessage;

        private void Awake()
        {
            Instance = this;
            YijingAssetLibrary.Initialize();
            LoadLocalSessionRecords();
        }

        private void Start()
        {
            if (Yijing3DWorldBuilder.Instance != null)
            {
                Yijing3DWorldBuilder.Instance.OnMemoryItemClicked3D += Handle3DMemoryItemClick;
                Yijing3DWorldBuilder.Instance.OnAttentionTokenClicked3D += Handle3DAttentionClick;
                Yijing3DWorldBuilder.Instance.OnKitchenStepClicked3D += Handle3DKitchenClick;
            }
            _lastMousePos = Input.mousePosition;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (CurrentRun != null && CurrentRun.phase != YijingRunPhase.Finished && CurrentRun.events.Count < 1000)
            {
                CurrentRun.events.Add(new SessionEventRecord
                {
                    type = hasFocus ? "window_visible" : "window_hidden",
                    at = DateTime.UtcNow.ToString("o")
                });
            }
        }

        private void Update()
        {
            // Track cursor path length during active questions
            Vector3 mp = Input.mousePosition;
            if (CurrentRun != null && CurrentRun.phase == YijingRunPhase.Question)
            {
                _accumulatedCursorPathPx += Vector2.Distance(mp, _lastMousePos);
            }
            _lastMousePos = mp;

            if (CurrentRun == null || CurrentRun.phase == YijingRunPhase.Finished) return;

            if (CurrentRun.phase == YijingRunPhase.Observe)
            {
                if (Time.realtimeSinceStartup >= CurrentRun.observeEndTime)
                {
                    BeginQuestionsFromObserve();
                }
            }
            else if (CurrentRun.phase == YijingRunPhase.Question && CurrentRun.gameType == YijingGameType.Attention)
            {
                if (Time.realtimeSinceStartup >= CurrentRun.questionDeadlineTime)
                {
                    SubmitAttentionAnswer(null, true);
                }
            }
        }

        public List<TrainingSessionRecord> GetLocalRecords()
        {
            return _localRecords
                .Where(r => !_deletedRecordIds.Contains(r.id))
                .OrderByDescending(r => r.startedAt ?? "")
                .ToList();
        }

        public List<TrainingSessionRecord> GetSimulationRecords()
        {
            return YijingAssetLibrary.SimulationRecords;
        }

        public void DeleteLocalRecord(string recordId)
        {
            if (string.IsNullOrEmpty(recordId)) return;
            _deletedRecordIds.Add(recordId);
            _localRecords.RemoveAll(r => r.id == recordId);
        }

        // ====================================================================
        // 1. START MEMORY GARDEN (记忆花园)
        // ====================================================================
        public ActiveGameRunState StartMemoryGarden(string sceneId, string difficulty, string mode, string participant, string dailyId = "")
        {
            var scene = YijingAssetLibrary.Scenes.FirstOrDefault(s => s.id == sceneId) ?? YijingAssetLibrary.Scenes[0];
            if (!scene.reviewed || scene.annotations.Count < 3)
            {
                throw new InvalidOperationException("该场景需至少包含 3 个已审核标注物品");
            }

            int maxCount = difficulty == "hard" ? 8 : (difficulty == "normal" ? 5 : 3);
            int observeSec = difficulty == "hard" ? 12 : (difficulty == "normal" ? 20 : 30);

            var pool = scene.annotations.OrderBy(_ => _rng.Next()).Take(Mathf.Min(maxCount, scene.annotations.Count)).ToList();
            if (mode == "practice")
            {
                pool = pool.Take(Mathf.Min(3, pool.Count)).ToList();
            }

            float now = Time.realtimeSinceStartup;
            CurrentRun = new ActiveGameRunState
            {
                runId = Guid.NewGuid().ToString("N"),
                dailyId = dailyId ?? "",
                gameType = YijingGameType.Memory,
                gameCode = "memory",
                gameName = "记忆花园",
                difficulty = difficulty,
                mode = mode,
                participant = string.IsNullOrWhiteSpace(participant) ? "P01" : participant.Trim(),
                phase = YijingRunPhase.Observe,
                sceneId = scene.id,
                sceneTitle = scene.title,
                sceneSnapshot = scene,
                memoryOrder = pool,
                totalQuestions = pool.Count,
                currentIndex = 1,
                observationSeconds = observeSec,
                observeEndTime = now + observeSec,
                runStartTime = now,
                startedAtIso = DateTime.UtcNow.ToString("o")
            };

            if (Yijing3DWorldBuilder.Instance != null)
            {
                Yijing3DWorldBuilder.Instance.ShowMemoryStage(scene.theme, scene.textureResourceName);
                Yijing3DWorldBuilder.Instance.SetMemoryObserveMarkers(pool, true);
            }

            SpeakPrompt("请仔细观察3D场景中直接标在物品上的选择点，记住每个物品所在的位置。");
            return CurrentRun;
        }

        // ====================================================================
        // 2. START ATTENTION TRACKING (注意力追踪)
        // ====================================================================
        public ActiveGameRunState StartAttentionTracking(string taskId, string difficulty, string mode, string participant, string dailyId = "")
        {
            var catalog = YijingAssetLibrary.Catalog;
            var task = catalog.attention.FirstOrDefault(t => t.id == taskId) ?? catalog.attention[0];

            int deadlineSec = difficulty == "hard" ? 6 : (difficulty == "normal" ? 9 : 12);
            int totalQ = mode == "practice" ? 4 : (difficulty == "hard" ? 10 : (difficulty == "normal" ? 8 : 6));
            float now = Time.realtimeSinceStartup;

            CurrentRun = new ActiveGameRunState
            {
                runId = Guid.NewGuid().ToString("N"),
                dailyId = dailyId ?? "",
                gameType = YijingGameType.Attention,
                gameCode = "attention",
                gameName = "注意力追踪",
                difficulty = difficulty,
                mode = mode,
                participant = string.IsNullOrWhiteSpace(participant) ? "P01" : participant.Trim(),
                phase = YijingRunPhase.Question,
                taskId = task.id,
                taskName = task.name,
                attentionThemeItems = new List<AttentionItemDef>(task.items),
                totalQuestions = totalQ,
                currentIndex = 0,
                observationSeconds = 0,
                deadlineSeconds = deadlineSec,
                runStartTime = now,
                startedAtIso = DateTime.UtcNow.ToString("o")
            };

            AdvanceToNextQuestion();
            return CurrentRun;
        }

        // ====================================================================
        // 3. START KITCHEN STEPS (厨房步骤)
        // ====================================================================
        public ActiveGameRunState StartKitchenSteps(string taskId, string difficulty, string mode, string participant, string dailyId = "")
        {
            var catalog = YijingAssetLibrary.Catalog;
            var task = catalog.kitchen.FirstOrDefault(t => t.id == taskId) ?? catalog.kitchen[0];

            List<KitchenStepDef> recipe = task.steps.Select(s => new KitchenStepDef { id = s.id, label = s.label, icon = s.icon }).ToList();
            if (difficulty == "easy" && recipe.Count > 3)
            {
                recipe = recipe.Skip(recipe.Count - 3).ToList();
            }
            else if (difficulty == "hard")
            {
                var expanded = new List<KitchenStepDef>
                {
                    new KitchenStepDef { id = "care-wash", label = "先洗净双手", icon = "care-wash" }
                };
                expanded.AddRange(recipe);
                expanded.Add(new KitchenStepDef { id = "care-clean", label = "最后擦净台面", icon = "care-clean" });
                recipe = expanded;
            }

            int observeSec = difficulty == "hard" ? 30 : (difficulty == "normal" ? 24 : 20);
            float now = Time.realtimeSinceStartup;

            CurrentRun = new ActiveGameRunState
            {
                runId = Guid.NewGuid().ToString("N"),
                dailyId = dailyId ?? "",
                gameType = YijingGameType.Kitchen,
                gameCode = "kitchen",
                gameName = "厨房步骤",
                difficulty = difficulty,
                mode = mode,
                participant = string.IsNullOrWhiteSpace(participant) ? "P01" : participant.Trim(),
                phase = YijingRunPhase.Observe,
                taskId = task.id,
                taskName = task.name,
                kitchenRecipe = recipe,
                totalQuestions = recipe.Count,
                currentIndex = 0,
                observationSeconds = observeSec,
                observeEndTime = now + observeSec,
                deadlineSeconds = 0,
                runStartTime = now,
                startedAtIso = DateTime.UtcNow.ToString("o")
            };

            if (Yijing3DWorldBuilder.Instance != null)
            {
                Yijing3DWorldBuilder.Instance.ShowKitchenStage(recipe, true);
            }

            SpeakPrompt("请记住" + task.name + "的步骤顺序，稍后步骤打乱后按顺序选择。");
            return CurrentRun;
        }

        // ====================================================================
        // PHASE TRANSITIONS & QUESTION GENERATION
        // ====================================================================
        public void BeginQuestionsFromObserve()
        {
            if (CurrentRun == null || CurrentRun.phase != YijingRunPhase.Observe) return;

            if (CurrentRun.gameType == YijingGameType.Memory)
            {
                CurrentRun.phase = YijingRunPhase.Question;
                CurrentRun.currentIndex = 1;
                CurrentRun.questionStartTime = Time.realtimeSinceStartup;
                _accumulatedCursorPathPx = 0f;

                var firstItem = CurrentRun.memoryOrder[0];
                if (Yijing3DWorldBuilder.Instance != null)
                {
                    Yijing3DWorldBuilder.Instance.SetMemoryQuestionMarkers(
                        CurrentRun.sceneSnapshot.annotations,
                        firstItem.id,
                        CurrentRun.mode == "practice");
                }

                SpeakPrompt("请直接点击3D场景中【" + firstItem.label + "】上的选择标点！");
            }
            else if (CurrentRun.gameType == YijingGameType.Kitchen)
            {
                AdvanceToNextQuestion();
            }
        }

        private void AdvanceToNextQuestion()
        {
            if (CurrentRun == null) return;
            CurrentRun.phase = YijingRunPhase.Question;
            CurrentRun.currentIndex = CurrentRun.responses.Count + 1;
            CurrentRun.questionStartTime = Time.realtimeSinceStartup;
            _accumulatedCursorPathPx = 0f;

            if (CurrentRun.gameType == YijingGameType.Attention)
            {
                int optionCount = CurrentRun.difficulty == "hard" ? 6 : (CurrentRun.difficulty == "normal" ? 4 : 3);
                var targetItem = CurrentRun.attentionThemeItems[_rng.Next(CurrentRun.attentionThemeItems.Count)];
                List<AttentionItemDef> distractors = new List<AttentionItemDef>();

                if (CurrentRun.difficulty == "easy")
                {
                    // Cross-theme distractors with distinct labels
                    var pool = YijingAssetLibrary.Catalog.attention
                        .Where(t => t.id != CurrentRun.taskId)
                        .SelectMany(t => t.items)
                        .Where(item => item.label != targetItem.label)
                        .OrderBy(_ => _rng.Next())
                        .ToList();

                    foreach (var cand in pool)
                    {
                        if (distractors.All(d => d.label != cand.label))
                        {
                            distractors.Add(cand);
                            if (distractors.Count == optionCount - 1) break;
                        }
                    }
                }
                else
                {
                    // Same-theme distractors
                    distractors = CurrentRun.attentionThemeItems
                        .Where(x => x.icon != targetItem.icon)
                        .OrderBy(_ => _rng.Next())
                        .Take(optionCount - 1)
                        .ToList();
                }

                var combined = new List<AttentionItemDef> { targetItem };
                combined.AddRange(distractors);
                combined = combined.OrderBy(_ => _rng.Next()).ToList();

                CurrentRun.currentOptions = combined.Select(x => new QuestionOptionRuntime
                {
                    id = Guid.NewGuid().ToString("N").Substring(0, 12),
                    icon = x.icon,
                    label = x.label
                }).ToList();

                var correctOpt = CurrentRun.currentOptions.First(x => x.icon == targetItem.icon);
                CurrentRun.currentAttentionTarget = targetItem;
                CurrentRun.currentCorrectOptionId = correctOpt.id;
                CurrentRun.questionDeadlineTime = CurrentRun.questionStartTime + CurrentRun.deadlineSeconds;

                if (Yijing3DWorldBuilder.Instance != null)
                {
                    Yijing3DWorldBuilder.Instance.ShowAttentionStage(targetItem.icon, CurrentRun.currentOptions, CurrentRun.difficulty);
                }

                SpeakPrompt("请找：" + targetItem.label);
            }
            else if (CurrentRun.gameType == YijingGameType.Kitchen)
            {
                var remainingSteps = CurrentRun.kitchenRecipe
                    .Where(s => !CurrentRun.kitchenSelectedIds.Contains(s.id))
                    .OrderBy(_ => _rng.Next())
                    .ToList();

                CurrentRun.currentOptions = remainingSteps.Select(s => new QuestionOptionRuntime
                {
                    id = s.id,
                    icon = s.icon,
                    label = s.label
                }).ToList();

                if (Yijing3DWorldBuilder.Instance != null)
                {
                    Yijing3DWorldBuilder.Instance.ShowKitchenStage(remainingSteps, false);
                }

                SpeakPrompt("第 " + CurrentRun.currentIndex + " 步做什么？");
            }
        }

        // ====================================================================
        // SUBMIT ANSWERS FOR EACH GAME
        // ====================================================================
        public void SubmitMemoryAnswerByItem(string chosenItemId)
        {
            if (CurrentRun == null || CurrentRun.phase != YijingRunPhase.Question || CurrentRun.gameType != YijingGameType.Memory)
                return;

            if (string.IsNullOrEmpty(chosenItemId))
            {
                SubmitMemoryAnswer(null);
                return;
            }

            var chosenItem = CurrentRun.sceneSnapshot.annotations.FirstOrDefault(a => a.id == chosenItemId);
            if (chosenItem != null)
            {
                SubmitMemoryAnswer(chosenItem.zone);
            }
        }

        public void SubmitMemoryAnswer(int? zoneChoice)
        {
            if (CurrentRun == null || CurrentRun.phase != YijingRunPhase.Question || CurrentRun.gameType != YijingGameType.Memory)
                return;

            int idx = CurrentRun.responses.Count;
            if (idx >= CurrentRun.memoryOrder.Count) return;

            var targetItem = CurrentRun.memoryOrder[idx];
            int rtMs = Mathf.Max(120, Mathf.RoundToInt((Time.realtimeSinceStartup - CurrentRun.questionStartTime) * 1000f));
            bool isCorrect = zoneChoice.HasValue && zoneChoice.Value == targetItem.zone;

            var chosenAnnotation = zoneChoice.HasValue
                ? CurrentRun.sceneSnapshot.annotations.FirstOrDefault(a => a.zone == zoneChoice.Value)
                : null;
            string chosenText = chosenAnnotation != null
                ? (chosenAnnotation.label + " (标点" + (chosenAnnotation.zone + 1) + ")")
                : (zoneChoice.HasValue ? YijingZones.ZoneNames[Mathf.Clamp(zoneChoice.Value, 0, 8)] : "不记得");

            CurrentRun.responses.Add(new QuestionResponseRecord
            {
                index = idx + 1,
                label = targetItem.label,
                correctZone = targetItem.zone,
                choice = zoneChoice.HasValue ? zoneChoice.Value.ToString() : null,
                choiceLabel = chosenText,
                correct = isCorrect,
                reactionMs = rtMs,
                omitted = false,
                answeredAt = DateTime.UtcNow.ToString("o"),
                cursorPathPx = _accumulatedCursorPathPx
            });

            if (!isCorrect)
            {
                SetFeedback("没关系，刚才【" + targetItem.label + "】在" + (targetItem.zone + 1) + "号标点，继续看下一题。");
            }
            else
            {
                SetFeedback("✓ 点选正确！已找到【" + targetItem.label + "】！");
            }

            if (CurrentRun.responses.Count >= CurrentRun.totalQuestions)
            {
                if (Yijing3DWorldBuilder.Instance != null)
                {
                    Yijing3DWorldBuilder.Instance.SetMemoryObserveMarkers(CurrentRun.sceneSnapshot.annotations, true);
                }
                FinishCurrentRun("completed");
                return;
            }

            CurrentRun.currentIndex = CurrentRun.responses.Count + 1;
            CurrentRun.questionStartTime = Time.realtimeSinceStartup;
            _accumulatedCursorPathPx = 0f;
            var nextItem = CurrentRun.memoryOrder[CurrentRun.responses.Count];

            if (Yijing3DWorldBuilder.Instance != null)
            {
                Yijing3DWorldBuilder.Instance.SetMemoryQuestionMarkers(
                    CurrentRun.sceneSnapshot.annotations,
                    nextItem.id,
                    CurrentRun.mode == "practice");
            }

            SpeakPrompt("请直接点击3D场景中【" + nextItem.label + "】上的选择标点！");
        }

        public void SubmitAttentionAnswer(string optionId, bool timeout = false)
        {
            if (CurrentRun == null || CurrentRun.phase != YijingRunPhase.Question || CurrentRun.gameType != YijingGameType.Attention)
                return;

            bool expired = timeout || (Time.realtimeSinceStartup > CurrentRun.questionDeadlineTime + 0.1f);
            if (expired) optionId = null;

            int rtMs = Mathf.Max(120, Mathf.RoundToInt((Time.realtimeSinceStartup - CurrentRun.questionStartTime) * 1000f));
            bool isCorrect = !string.IsNullOrEmpty(optionId) && optionId == CurrentRun.currentCorrectOptionId;
            var chosenOpt = CurrentRun.currentOptions.FirstOrDefault(o => o.id == optionId);

            CurrentRun.responses.Add(new QuestionResponseRecord
            {
                index = CurrentRun.responses.Count + 1,
                label = CurrentRun.currentAttentionTarget.label,
                correctZone = -1,
                choice = optionId,
                choiceLabel = chosenOpt != null ? chosenOpt.label : "未选择",
                correct = isCorrect,
                reactionMs = rtMs,
                omitted = expired,
                answeredAt = DateTime.UtcNow.ToString("o"),
                cursorPathPx = _accumulatedCursorPathPx
            });

            if (!isCorrect)
            {
                SetFeedback(expired ? "没关系，慢慢找下一张。" : "没关系，接着试下一项。");
            }
            else
            {
                SetFeedback("找对了！");
            }

            if (CurrentRun.responses.Count >= CurrentRun.totalQuestions)
            {
                FinishCurrentRun("completed");
                return;
            }

            AdvanceToNextQuestion();
        }

        public void SubmitKitchenAnswer(string stepId)
        {
            if (CurrentRun == null || CurrentRun.phase != YijingRunPhase.Question || CurrentRun.gameType != YijingGameType.Kitchen)
                return;
            if (string.IsNullOrEmpty(stepId)) return;

            int stepIdx = CurrentRun.responses.Count;
            if (stepIdx >= CurrentRun.kitchenRecipe.Count) return;

            var expectedStep = CurrentRun.kitchenRecipe[stepIdx];
            var chosenOpt = CurrentRun.currentOptions.FirstOrDefault(o => o.id == stepId);
            if (chosenOpt == null) return;

            int rtMs = Mathf.Max(120, Mathf.RoundToInt((Time.realtimeSinceStartup - CurrentRun.questionStartTime) * 1000f));
            bool isCorrect = stepId == expectedStep.id;

            CurrentRun.kitchenSelectedIds.Add(stepId);
            CurrentRun.responses.Add(new QuestionResponseRecord
            {
                index = stepIdx + 1,
                label = expectedStep.label,
                correctZone = -1,
                choice = stepId,
                choiceLabel = chosenOpt.label,
                correct = isCorrect,
                reactionMs = rtMs,
                omitted = false,
                answeredAt = DateTime.UtcNow.ToString("o"),
                cursorPathPx = _accumulatedCursorPathPx
            });

            if (!isCorrect)
            {
                SetFeedback("没关系，接着选择下一步。");
            }
            else
            {
                SetFeedback("步骤顺序正确！");
            }

            if (CurrentRun.responses.Count >= CurrentRun.totalQuestions)
            {
                FinishCurrentRun("completed");
                return;
            }

            AdvanceToNextQuestion();
        }

        public TrainingSessionRecord AbortCurrentRun()
        {
            if (CurrentRun == null || CurrentRun.phase == YijingRunPhase.Finished) return null;
            return FinishCurrentRun("aborted");
        }

        private TrainingSessionRecord FinishCurrentRun(string status)
        {
            var run = CurrentRun;
            run.phase = YijingRunPhase.Finished;

            var record = new TrainingSessionRecord
            {
                id = run.runId,
                dailyId = run.dailyId ?? "",
                game = run.gameCode,
                gameName = run.gameName,
                taskId = run.taskId ?? run.sceneId,
                taskName = !string.IsNullOrEmpty(run.taskName) ? run.taskName : run.sceneTitle,
                sceneId = run.sceneId ?? "",
                sceneTitle = !string.IsNullOrEmpty(run.sceneTitle) ? run.sceneTitle : (run.gameName + " · " + run.taskName),
                participant = run.participant,
                difficulty = run.difficulty,
                mode = run.mode,
                status = status,
                startedAt = run.startedAtIso,
                endedAt = DateTime.UtcNow.ToString("o"),
                observationSeconds = run.observationSeconds,
                total = run.totalQuestions,
                durationMs = Mathf.Max(500, Mathf.RoundToInt((Time.realtimeSinceStartup - run.runStartTime) * 1000f)),
                synthetic = false,
                sceneRevision = run.sceneSnapshot != null ? run.sceneSnapshot.revision : 1,
                responses = new List<QuestionResponseRecord>(run.responses),
                events = new List<SessionEventRecord>(run.events),
                order = new List<MemoryAnnotationDef>(run.memoryOrder)
            };

            YijingAssetLibrary.NormalizeRecordMetrics(record);
            _localRecords.Insert(0, record);
            SaveSessionRecordToDisk(record);

            CurrentRun = null;
            OnRunFinished?.Invoke(record);
            return record;
        }

        private void Handle3DMemoryItemClick(string itemId)
        {
            if (CurrentRun != null && CurrentRun.gameType == YijingGameType.Memory && CurrentRun.phase == YijingRunPhase.Question)
            {
                SubmitMemoryAnswerByItem(itemId);
            }
        }

        private void Handle3DAttentionClick(string optionId)
        {
            if (CurrentRun != null && CurrentRun.gameType == YijingGameType.Attention && CurrentRun.phase == YijingRunPhase.Question)
            {
                SubmitAttentionAnswer(optionId, false);
            }
        }

        private void Handle3DKitchenClick(string optionId)
        {
            if (CurrentRun != null && CurrentRun.gameType == YijingGameType.Kitchen && CurrentRun.phase == YijingRunPhase.Question)
            {
                SubmitKitchenAnswer(optionId);
            }
        }

        private void SetFeedback(string msg)
        {
            if (CurrentRun != null)
            {
                CurrentRun.lastFeedbackMessage = msg;
                CurrentRun.lastFeedbackUntil = Time.realtimeSinceStartup + 2.8f;
            }
            OnToastMessage?.Invoke(msg);
        }

        public void SpeakPrompt(string text)
        {
            LastSpokenPrompt = text;
        }

        private void LoadLocalSessionRecords()
        {
            _localRecords.Clear();
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "YijingSessions");
                if (Directory.Exists(dir))
                {
                    foreach (var file in Directory.GetFiles(dir, "*.json"))
                    {
                        var rec = JsonUtility.FromJson<TrainingSessionRecord>(File.ReadAllText(file));
                        if (rec != null && !string.IsNullOrEmpty(rec.id))
                        {
                            YijingAssetLibrary.NormalizeRecordMetrics(rec);
                            _localRecords.Add(rec);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Load local records note: " + e.Message);
            }
        }

        private void SaveSessionRecordToDisk(TrainingSessionRecord record)
        {
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "YijingSessions");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, record.id + ".json");
                File.WriteAllText(path, JsonUtility.ToJson(record, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("Save session note: " + e.Message);
            }
        }
    }
}
