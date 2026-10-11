using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yijing.Data
{
    public enum YijingGameType
    {
        Memory = 0,
        Attention = 1,
        Kitchen = 2
    }

    public enum YijingDifficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2
    }

    public enum YijingMode
    {
        Practice = 0,
        Formal = 1
    }

    public enum YijingRunPhase
    {
        Idle = 0,
        Observe = 1,
        Question = 2,
        Finished = 3
    }

    [Serializable]
    public class AttentionItemDef
    {
        public string icon;
        public string label;
    }

    [Serializable]
    public class AttentionTaskDef
    {
        public string id;
        public string name;
        public List<AttentionItemDef> items = new List<AttentionItemDef>();
    }

    [Serializable]
    public class KitchenStepDef
    {
        public string id;
        public string label;
        public string icon;
    }

    [Serializable]
    public class KitchenTaskDef
    {
        public string id;
        public string name;
        public List<KitchenStepDef> steps = new List<KitchenStepDef>();
    }

    [Serializable]
    public class TaskCatalogRoot
    {
        public List<AttentionTaskDef> attention = new List<AttentionTaskDef>();
        public List<KitchenTaskDef> kitchen = new List<KitchenTaskDef>();
    }

    [Serializable]
    public class MemoryAnnotationDef
    {
        public string id;
        public string label;
        public float x;
        public float y;
        public int zone;               // 0..7 item slot index (kept compatible with session records)
        public string propKind;        // 3D model archetype in scene
        public string iconKey;         // Icon slice key for clear target display
        public Vector3 worldLocalPos;  // Exact 3D anchor point on the object itself
        public Vector3 markerOffset;   // Offset above worldLocalPos for the floating badge pin
    }

    [Serializable]
    public class MemorySceneDef
    {
        public string id;
        public string title;
        public string description;
        public string theme; // courtyard, living, bedroom, rural
        public string textureResourceName;
        public bool reviewed = true;
        public int revision = 1;
        public List<MemoryAnnotationDef> annotations = new List<MemoryAnnotationDef>();
    }

    [Serializable]
    public class QuestionOptionRuntime
    {
        public string id;
        public string icon;
        public string label;
    }

    [Serializable]
    public class QuestionResponseRecord
    {
        public int index;
        public string label;
        public int correctZone = -1; // Item slot index for memory garden
        public string choice;        // Chosen item id or slot index string, null if skipped/omitted
        public string choiceLabel;
        public bool correct;
        public int reactionMs;
        public bool omitted;         // True if timeout in Attention
        public string answeredAt;
        public float cursorPathPx;   // Kinematic cursor travel distance during question
    }

    [Serializable]
    public class SessionEventRecord
    {
        public string type;
        public string at;
    }

    [Serializable]
    public class SessionMetricsRecord
    {
        public int correct;
        public int answered;
        public int total;
        public float accuracy = -1f; // -1 represents null (no answers submitted)
        public int meanReactionMs = -1;
        public int sdReactionMs = 0;
        public float cvReactionMs = 0f;
        public int skipped;
        public int omissions;
        public int errors;
    }

    [Serializable]
    public class TrainingSessionRecord
    {
        public string id;
        public string dailyId;
        public string game;       // "memory", "attention", "kitchen"
        public string gameName;   // "记忆花园", "注意力追踪", "厨房步骤"
        public string taskId;
        public string taskName;
        public string sceneId;
        public string sceneTitle;
        public string participant;
        public string difficulty; // "easy", "normal", "hard"
        public string mode;       // "practice", "formal"
        public string status;     // "completed", "aborted"
        public string startedAt;
        public string endedAt;
        public int observationSeconds;
        public int total;
        public int durationMs;
        public bool synthetic;
        public int simulatedAge;
        public int sceneRevision = 1;
        public string artVersion = "unity3d-20261010-v2";
        public SessionMetricsRecord metrics = new SessionMetricsRecord();
        public List<QuestionResponseRecord> responses = new List<QuestionResponseRecord>();
        public List<SessionEventRecord> events = new List<SessionEventRecord>();
        public List<MemoryAnnotationDef> order = new List<MemoryAnnotationDef>();
    }

    [Serializable]
    public class TrainingRecordCollection
    {
        public List<TrainingSessionRecord> records = new List<TrainingSessionRecord>();
    }

    [Serializable]
    public class DailyTrainingSession
    {
        public string id;
        public string startedAt;
        public string participant = "P01";
        public string difficulty = "normal";
        public string sceneId = "courtyard-main";
        public string attentionTaskId = "fruit";
        public string kitchenTaskId = "tea";
    }

    public static class YijingZones
    {
        public static readonly string[] ZoneNames = new string[]
        {
            "1号标点", "2号标点", "3号标点",
            "4号标点", "5号标点", "6号标点",
            "7号标点", "8号标点", "9号标点"
        };

        public static readonly string[] ShortZoneNames = new string[]
        {
            "标点①", "标点②", "标点③",
            "标点④", "标点⑤", "标点⑥",
            "标点⑦", "标点⑧", "标点⑨"
        };

        public static int CalcZone(float x, float y)
        {
            int col = Mathf.Min(2, Mathf.Max(0, Mathf.FloorToInt(x * 3f)));
            int row = Mathf.Min(2, Mathf.Max(0, Mathf.FloorToInt(y * 3f)));
            return row * 3 + col;
        }

        public static string DifficultyToCode(YijingDifficulty diff)
        {
            switch (diff)
            {
                case YijingDifficulty.Easy: return "easy";
                case YijingDifficulty.Normal: return "normal";
                case YijingDifficulty.Hard: return "hard";
                default: return "easy";
            }
        }

        public static YijingDifficulty CodeToDifficulty(string code)
        {
            if (code == "normal") return YijingDifficulty.Normal;
            if (code == "hard") return YijingDifficulty.Hard;
            return YijingDifficulty.Easy;
        }

        public static string DifficultyLabel(string code, bool isMemory = false)
        {
            if (code == "normal") return "标准";
            if (code == "hard") return "挑战";
            return isMemory ? "基础" : "轻松";
        }

        public static string GameCodeToName(string game)
        {
            if (game == "attention") return "注意力追踪";
            if (game == "kitchen") return "厨房步骤";
            return "记忆花园";
        }
    }
}
