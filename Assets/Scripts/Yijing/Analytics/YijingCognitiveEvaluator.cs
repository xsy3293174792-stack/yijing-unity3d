using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Yijing.Core;
using Yijing.Data;

namespace Yijing.Analytics
{
    [Serializable]
    public class GameDomainSummary
    {
        public string gameCode;
        public string gameName;
        public int totalSessions;
        public int completedSessions;
        public int abortedSessions;
        public int totalCorrect;
        public int totalAnswered;
        public int totalPlanned;
        public int totalErrors;
        public int totalOmissions;
        public int totalSkipped;
        public float meanAccuracy = -1f;
        public int meanReactionMs = -1;
        public float cvReaction = 0f;
        public float domainScore = 0f; // 0..100
    }

    [Serializable]
    public class TrendGroupSummary
    {
        public string groupTitle;
        public string participant;
        public string gameCode;
        public string taskTitle;
        public string difficulty;
        public string mode;
        public List<TrainingSessionRecord> sessions = new List<TrainingSessionRecord>();
    }

    [Serializable]
    public class CognitiveAssessmentProfile
    {
        public string participantFilter;
        public bool isSimulationData;
        public int totalRecords;
        public int completedRecords;
        public string dateRangeText;

        // 5-Domain Cognitive Scores (0..100)
        public float spatialMemoryScore;      // 空间位置记忆 (记忆花园)
        public float selectiveAttentionScore; // 视觉选择性注意 (注意力追踪)
        public float executivePlanningScore;  // 执行功能与序列规划 (厨房步骤)
        public float processingSpeedScore;    // 信息加工与反应速度 (IES逆效率指数)
        public float behavioralStabilityScore;// 反应时稳定性与依从性 (CV变异系数与完成率)
        public float compositeCognitiveIndex; // 综合认知表现指数 (0..100)

        public string gradeLevel;             // A / B / C / D 分级
        public string weakestDomainName;
        public string strongestDomainName;
        public string biomarkerInterpretation;
        public string trainingPrescription;

        public GameDomainSummary memorySummary = new GameDomainSummary();
        public GameDomainSummary attentionSummary = new GameDomainSummary();
        public GameDomainSummary kitchenSummary = new GameDomainSummary();
        public List<TrendGroupSummary> trendGroups = new List<TrendGroupSummary>();
    }

    public static class YijingCognitiveEvaluator
    {
        public static CognitiveAssessmentProfile EvaluateRecords(List<TrainingSessionRecord> records, string participantFilter = "*")
        {
            var profile = new CognitiveAssessmentProfile
            {
                participantFilter = string.IsNullOrEmpty(participantFilter) || participantFilter == "*" ? "全部参与者" : participantFilter,
                isSimulationData = records != null && records.Any(r => r.synthetic),
                totalRecords = records != null ? records.Count : 0,
                completedRecords = records != null ? records.Count(r => r.status == "completed") : 0
            };

            if (records == null || records.Count == 0)
            {
                profile.dateRangeText = "暂无记录";
                profile.gradeLevel = "待评估（暂无训练数据）";
                profile.biomarkerInterpretation = "请先完成至少一次游戏练习，或切换至“模拟演示数据（450条）”查看完整认知评估画像。";
                profile.trainingPrescription = "建议从【今日训练 · 基础/轻松档】开始，依次完成记忆花园、注意力追踪与厨房步骤。";
                return profile;
            }

            foreach (var r in records)
            {
                YijingAssetLibrary.NormalizeRecordMetrics(r);
            }

            var sortedByDate = records.OrderBy(r => r.startedAt ?? "").ToList();
            string firstDay = ExtractDateDay(sortedByDate.First().startedAt);
            string lastDay = ExtractDateDay(sortedByDate.Last().startedAt);
            profile.dateRangeText = firstDay == lastDay ? firstDay : (firstDay + " 至 " + lastDay);

            profile.memorySummary = BuildGameSummary(records, "memory", "记忆花园");
            profile.attentionSummary = BuildGameSummary(records, "attention", "注意力追踪");
            profile.kitchenSummary = BuildGameSummary(records, "kitchen", "厨房步骤");

            // 1. Domain 1: Spatial Working Memory Score (空间位置记忆)
            profile.spatialMemoryScore = ComputeMemoryDomainScore(profile.memorySummary, records.Where(r => r.game == "memory" && r.status == "completed").ToList());
            profile.memorySummary.domainScore = profile.spatialMemoryScore;

            // 2. Domain 2: Selective Visual Attention Score (视觉选择性注意)
            profile.selectiveAttentionScore = ComputeAttentionDomainScore(profile.attentionSummary, records.Where(r => r.game == "attention" && r.status == "completed").ToList());
            profile.attentionSummary.domainScore = profile.selectiveAttentionScore;

            // 3. Domain 3: Executive Function & Sequential Planning Score (执行功能与序列规划)
            profile.executivePlanningScore = ComputeKitchenDomainScore(profile.kitchenSummary, records.Where(r => r.game == "kitchen" && r.status == "completed").ToList());
            profile.kitchenSummary.domainScore = profile.executivePlanningScore;

            // 4. Domain 4: Information Processing Speed Score (信息加工速度)
            var allValidRts = records
                .Where(r => r.status == "completed" && r.responses != null)
                .SelectMany(r => r.responses)
                .Where(resp => !string.IsNullOrEmpty(resp.choice) && !resp.omitted && resp.reactionMs > 0)
                .Select(resp => resp.reactionMs)
                .ToList();

            float overallAcc = 0f;
            int totalAns = profile.memorySummary.totalAnswered + profile.attentionSummary.totalAnswered + profile.kitchenSummary.totalAnswered;
            int totalCor = profile.memorySummary.totalCorrect + profile.attentionSummary.totalCorrect + profile.kitchenSummary.totalCorrect;
            if (totalAns > 0) overallAcc = (float)totalCor / totalAns;

            if (allValidRts.Count > 0)
            {
                double meanRt = allValidRts.Average();
                // Inverse Efficiency Score (IES) = meanRT / accuracy
                double ies = meanRt / Math.Max(0.25, overallAcc);
                // Calibrate: IES <= 2500ms -> 95+, IES ~ 6000ms -> 75, IES >= 14000ms -> 35
                float speedRaw = Mathf.Clamp(108f - (float)(ies / 160.0), 25f, 98f);
                profile.processingSpeedScore = Mathf.Round(speedRaw * 10f) / 10f;

                // 5. Domain 5: Behavioral Stability & Adherence Score (反应时稳定性与任务依从性)
                double variance = allValidRts.Sum(t => (t - meanRt) * (t - meanRt)) / allValidRts.Count;
                double cv = meanRt > 1.0 ? Math.Sqrt(variance) / meanRt : 0.3;
                float adherence = (float)profile.completedRecords / Mathf.Max(1, profile.totalRecords);
                int windowSwitches = records.Sum(r => r.events != null ? r.events.Count(e => e.type == "window_hidden") : 0);
                float focusPenalty = Mathf.Min(15f, windowSwitches * 2.5f);
                float stabilityRaw = Mathf.Clamp((1f - Mathf.Clamp01((float)cv - 0.15f)) * 65f + adherence * 35f - focusPenalty, 20f, 98f);
                profile.behavioralStabilityScore = Mathf.Round(stabilityRaw * 10f) / 10f;
            }
            else
            {
                profile.processingSpeedScore = 50f;
                profile.behavioralStabilityScore = 50f;
            }

            // Composite index across active domains
            List<float> activeDomains = new List<float>();
            if (profile.memorySummary.completedSessions > 0) activeDomains.Add(profile.spatialMemoryScore);
            if (profile.attentionSummary.completedSessions > 0) activeDomains.Add(profile.selectiveAttentionScore);
            if (profile.kitchenSummary.completedSessions > 0) activeDomains.Add(profile.executivePlanningScore);
            activeDomains.Add(profile.processingSpeedScore);
            activeDomains.Add(profile.behavioralStabilityScore);

            profile.compositeCognitiveIndex = Mathf.Round(activeDomains.Average() * 10f) / 10f;

            // Grade classification
            if (profile.compositeCognitiveIndex >= 82f)
                profile.gradeLevel = "A级 · 认知任务表现优良（各项指标稳定）";
            else if (profile.compositeCognitiveIndex >= 68f)
                profile.gradeLevel = "B级 · 认知任务表现良好（常规水平）";
            else if (profile.compositeCognitiveIndex >= 54f)
                profile.gradeLevel = "C级 · 局部认知维度存在波动（建议针对性强化训练）";
            else
                profile.gradeLevel = "D级 · 多项任务正确率或反应时偏低（建议陪同指导与规律复查）";

            // Identify strongest & weakest among the 3 core task domains
            var coreTriplet = new List<(string name, float score, string code)>
            {
                ("空间位置记忆（记忆花园）", profile.spatialMemoryScore, "memory"),
                ("视觉选择性注意（注意力追踪）", profile.selectiveAttentionScore, "attention"),
                ("执行与序列规划（厨房步骤）", profile.executivePlanningScore, "kitchen")
            };
            var orderedCore = coreTriplet.OrderBy(x => x.score).ToList();
            profile.weakestDomainName = orderedCore.First().name;
            profile.strongestDomainName = orderedCore.Last().name;

            // Build behavioral biomarker interpretation
            StringBuilder bio = new StringBuilder();
            bio.AppendFormat(
                "1. 空间记忆维度：{0:F1}分（完成 {1} 次，正确率 {2}，主动跳过 {3} 题，位置误选 {4} 题）；\n",
                profile.spatialMemoryScore,
                profile.memorySummary.completedSessions,
                FormatAccuracy(profile.memorySummary.meanAccuracy),
                profile.memorySummary.totalSkipped,
                profile.memorySummary.totalErrors);
            bio.AppendFormat(
                "2. 视觉注意维度：{0:F1}分（完成 {1} 次，命中率 {2}，干扰项误选 {3} 次，超时遗漏 {4} 次，平均反应 {5}）；\n",
                profile.selectiveAttentionScore,
                profile.attentionSummary.completedSessions,
                FormatAccuracy(profile.attentionSummary.meanAccuracy),
                profile.attentionSummary.totalErrors,
                profile.attentionSummary.totalOmissions,
                FormatReaction(profile.attentionSummary.meanReactionMs));
            bio.AppendFormat(
                "3. 执行规划维度：{0:F1}分（完成 {1} 次，步骤顺序正确率 {2}，序列顺序错误 {3} 次，平均决策 {4}）；\n",
                profile.executivePlanningScore,
                profile.kitchenSummary.completedSessions,
                FormatAccuracy(profile.kitchenSummary.meanAccuracy),
                profile.kitchenSummary.totalErrors,
                FormatReaction(profile.kitchenSummary.meanReactionMs));
            bio.AppendFormat(
                "4. 运动学与反应节律：信息加工速度 {0:F1}分，反应稳定性与依从性 {1:F1}分（任务完成率 {2}%）。",
                profile.processingSpeedScore,
                profile.behavioralStabilityScore,
                Mathf.RoundToInt(100f * profile.completedRecords / Mathf.Max(1, profile.totalRecords)));
            profile.biomarkerInterpretation = bio.ToString();

            // Build personalized intervention prescription
            string weakCode = orderedCore.First().code;
            if (weakCode == "memory")
            {
                profile.trainingPrescription =
                    "【个性化干预建议】当前薄弱环节为「空间位置记忆」。建议优先选择【记忆花园 · 标准档（5物品/20秒）】，练习前开启语音播报，采用“物品名称+九宫格方位”双重编码策略降低遗忘跳过率。";
            }
            else if (weakCode == "attention")
            {
                bool highOmission = profile.attentionSummary.totalOmissions > profile.attentionSummary.totalErrors;
                profile.trainingPrescription = highOmission
                    ? "【个性化干预建议】当前薄弱环节为「视觉选择性注意（超时漏报偏多）」。建议选择【注意力追踪 · 轻松/标准档】并勾选“减少动态效果”或放慢节奏，训练视线扫视效率。"
                    : "【个性化干预建议】当前薄弱环节为「视觉选择性注意（同类干扰项误选偏多）」。建议选择【注意力追踪 · 标准档（同主题4图）】集中辨识同类物品细节特征。";
            }
            else
            {
                profile.trainingPrescription =
                    "【个性化干预建议】当前薄弱环节为「执行功能与步骤顺序规划」。建议选择【厨房步骤 · 标准档（4步，观察24秒）】（如泡一杯茶、蔬菜三明治），在观察阶段口头复述先后步骤以强化执行序列记忆。";
            }

            profile.trendGroups = BuildTrendGroups(records);
            return profile;
        }

        private static GameDomainSummary BuildGameSummary(List<TrainingSessionRecord> all, string gameCode, string gameName)
        {
            var group = all.Where(r => (r.game ?? "memory") == gameCode).ToList();
            var completed = group.Where(r => r.status == "completed").ToList();

            var summary = new GameDomainSummary
            {
                gameCode = gameCode,
                gameName = gameName,
                totalSessions = group.Count,
                completedSessions = completed.Count,
                abortedSessions = group.Count - completed.Count,
                totalCorrect = completed.Sum(r => r.metrics.correct),
                totalAnswered = completed.Sum(r => r.metrics.answered),
                totalPlanned = completed.Sum(r => r.total),
                totalErrors = completed.Sum(r => r.metrics.errors),
                totalOmissions = completed.Sum(r => r.metrics.omissions),
                totalSkipped = completed.Sum(r => r.metrics.skipped)
            };

            if (summary.totalAnswered > 0)
            {
                summary.meanAccuracy = (float)summary.totalCorrect / summary.totalAnswered;
            }

            var validResponses = completed
                .Where(r => r.responses != null)
                .SelectMany(r => r.responses)
                .Where(resp => !string.IsNullOrEmpty(resp.choice) && !resp.omitted && resp.reactionMs > 0)
                .Select(resp => resp.reactionMs)
                .ToList();

            if (validResponses.Count > 0)
            {
                double mean = validResponses.Average();
                summary.meanReactionMs = Mathf.RoundToInt((float)mean);
                if (validResponses.Count > 1)
                {
                    double variance = validResponses.Sum(t => (t - mean) * (t - mean)) / validResponses.Count;
                    summary.cvReaction = mean > 1.0 ? (float)(Math.Sqrt(variance) / mean) : 0f;
                }
            }
            return summary;
        }

        private static float ComputeMemoryDomainScore(GameDomainSummary sum, List<TrainingSessionRecord> completed)
        {
            if (completed.Count == 0 || sum.totalAnswered == 0) return 60f;
            float acc = Mathf.Clamp01(sum.meanAccuracy);
            float avgDiffBonus = completed.Average(r => r.difficulty == "hard" ? 1.08f : (r.difficulty == "normal" ? 1.0f : 0.92f));
            float skipRatio = (float)sum.totalSkipped / Mathf.Max(1, sum.totalAnswered);
            float speedScore = sum.meanReactionMs > 0 ? Mathf.Clamp01(1f - (sum.meanReactionMs - 2000f) / 14000f) : 0.6f;
            float score = (acc * 72f * avgDiffBonus) + (speedScore * 18f) + ((1f - skipRatio) * 10f);
            return Mathf.Round(Mathf.Clamp(score, 15f, 99f) * 10f) / 10f;
        }

        private static float ComputeAttentionDomainScore(GameDomainSummary sum, List<TrainingSessionRecord> completed)
        {
            if (completed.Count == 0 || sum.totalAnswered == 0) return 60f;
            float acc = Mathf.Clamp01(sum.meanAccuracy);
            float avgDiffBonus = completed.Average(r => r.difficulty == "hard" ? 1.08f : (r.difficulty == "normal" ? 1.0f : 0.92f));
            float omissionRatio = (float)sum.totalOmissions / Mathf.Max(1, sum.totalAnswered);
            float speedScore = sum.meanReactionMs > 0 ? Mathf.Clamp01(1f - (sum.meanReactionMs - 1500f) / 9500f) : 0.6f;
            float score = (acc * 68f * avgDiffBonus) + ((1f - omissionRatio) * 16f) + (speedScore * 16f);
            return Mathf.Round(Mathf.Clamp(score, 15f, 99f) * 10f) / 10f;
        }

        private static float ComputeKitchenDomainScore(GameDomainSummary sum, List<TrainingSessionRecord> completed)
        {
            if (completed.Count == 0 || sum.totalAnswered == 0) return 60f;
            float acc = Mathf.Clamp01(sum.meanAccuracy);
            float avgDiffBonus = completed.Average(r => r.difficulty == "hard" ? 1.08f : (r.difficulty == "normal" ? 1.0f : 0.92f));
            float speedScore = sum.meanReactionMs > 0 ? Mathf.Clamp01(1f - (sum.meanReactionMs - 1800f) / 12000f) : 0.6f;
            float score = (acc * 76f * avgDiffBonus) + (speedScore * 14f) + 10f * acc;
            return Mathf.Round(Mathf.Clamp(score, 15f, 99f) * 10f) / 10f;
        }

        public static List<TrendGroupSummary> BuildTrendGroups(List<TrainingSessionRecord> records)
        {
            var map = new Dictionary<string, TrendGroupSummary>();
            if (records == null) return new List<TrendGroupSummary>();

            foreach (var r in records.Where(x => x.status == "completed"))
            {
                string game = string.IsNullOrEmpty(r.game) ? "memory" : r.game;
                string taskKey = !string.IsNullOrEmpty(r.taskId) ? r.taskId : r.sceneId;
                string taskTitle = !string.IsNullOrEmpty(r.taskName) ? r.taskName : r.sceneTitle;
                string key = string.Format("{0}|{1}|{2}|{3}|{4}|{5}|{6}",
                    r.participant ?? "", game, taskKey ?? "", r.difficulty ?? "easy", r.mode ?? "practice", r.total, r.observationSeconds);

                if (!map.TryGetValue(key, out var group))
                {
                    group = new TrendGroupSummary
                    {
                        participant = string.IsNullOrEmpty(r.participant) ? "未填写代号" : r.participant,
                        gameCode = game,
                        taskTitle = taskTitle,
                        difficulty = r.difficulty ?? "easy",
                        mode = r.mode ?? "practice",
                        groupTitle = string.Format("{0} · {1} · {2} · {3}",
                            string.IsNullOrEmpty(r.participant) ? "未填写代号" : r.participant,
                            taskTitle,
                            YijingZones.DifficultyLabel(r.difficulty, game == "memory"),
                            r.mode == "formal" ? "正式" : "练习")
                    };
                    map[key] = group;
                }
                group.sessions.Add(r);
            }

            foreach (var g in map.Values)
            {
                g.sessions = g.sessions.OrderBy(s => s.startedAt ?? "").ToList();
            }

            return map.Values.OrderByDescending(g => g.sessions.Count).ToList();
        }

        public static string ExportHtmlAssessmentReport(List<TrainingSessionRecord> rows, string title)
        {
            var profile = EvaluateRecords(rows);
            var sorted = rows.OrderByDescending(r => r.startedAt ?? "").ToList();
            var people = rows.Select(r => string.IsNullOrEmpty(r.participant) ? "未填写代号" : r.participant).Distinct().ToList();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\">");
            sb.AppendLine("<title>忆境 Unity3D · 认知评估与训练报告</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font:15px/1.6 'Microsoft YaHei',sans-serif;color:#273d35;max-width:960px;margin:28px auto;padding:24px;background:#f7f9f7}");
            sb.AppendLine(".card{background:#fff;border:1px solid #dce2dd;border-radius:12px;padding:20px;margin-bottom:18px}");
            sb.AppendLine("h1{font-size:28px;margin:0 0 8px 0}h2{font-size:20px;margin:12px 0 8px 0;color:#1e4d3b}");
            sb.AppendLine("table{border-collapse:collapse;width:100%;margin:12px 0;font-size:13px}");
            sb.AppendLine("td,th{text-align:left;border-bottom:1px solid #dce2dd;padding:8px}");
            sb.AppendLine(".bar-wrap{background:#e6ece8;height:14px;border-radius:7px;overflow:hidden;width:180px;display:inline-block;vertical-align:middle;margin-right:8px}");
            sb.AppendLine(".bar-fill{background:#2a6f54;height:100%}");
            sb.AppendLine("button{padding:10px 22px;background:#2a6f54;color:#fff;border:none;border-radius:8px;cursor:pointer;font-size:15px}");
            sb.AppendLine("@media print{@page{size:A4;margin:14mm}body{margin:0;padding:0;background:#fff}button{display:none}}");
            sb.AppendLine("</style><body>");
            sb.AppendLine("<button onclick=\"window.print()\">打印 / 另存为 PDF</button>");
            sb.AppendLine("<div class=\"card\">");
            sb.AppendFormat("<h1>忆境 Unity 3D · {0}</h1>\n", profile.isSimulationData ? "模拟演示认知评估报告" : "认知功能评估与训练报告");
            if (profile.isSimulationData)
            {
                sb.AppendLine("<p style=\"color:#9c4a1a\"><strong>注：当前包含模拟演示数据（非真实受试者实测），所列日期和年龄为虚拟字段，仅用于检验统计与评估流程。</strong></p>");
            }
            sb.AppendFormat("<p>{0} · 导出时间：{1}</p>\n", EscapeHtml(title), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendFormat("<p>参与者：{0} | 训练日期范围：{1} | 记录总数：{2} 条（已完成 {3} 条）</p>\n",
                EscapeHtml(string.Join("、", people.Take(12))) + (people.Count > 12 ? " 等" + people.Count + "人" : ""),
                EscapeHtml(profile.dateRangeText),
                profile.totalRecords,
                profile.completedRecords);
            sb.AppendLine("</div>");

            // Section 1: 5-Domain Cognitive Profile
            sb.AppendLine("<div class=\"card\"><h2>一、五维认知功能统计评估画像</h2>");
            sb.AppendFormat("<p><strong>综合表现评级：{0}（综合指数 {1:F1} / 100）</strong></p>\n", EscapeHtml(profile.gradeLevel), profile.compositeCognitiveIndex);
            sb.AppendLine("<table><thead><tr><th>认知评估维度</th><th>对应3D任务范式</th><th>量化得分 (0-100)</th><th>优势/薄弱状态</th></tr></thead><tbody>");
            AppendDomainRow(sb, "空间位置记忆", "记忆花园（3D庭院/居室九宫格回忆）", profile.spatialMemoryScore);
            AppendDomainRow(sb, "视觉选择性注意", "注意力追踪（3D动态展台目标搜寻）", profile.selectiveAttentionScore);
            AppendDomainRow(sb, "执行功能与序列规划", "厨房步骤（3D厨房工序记忆与排序）", profile.executivePlanningScore);
            AppendDomainRow(sb, "信息加工反应速度", "三项任务有效选择逆效率指数 (IES)", profile.processingSpeedScore);
            AppendDomainRow(sb, "反应时稳定性与依从性", "反应时变异系数 (CV) 与完成率", profile.behavioralStabilityScore);
            sb.AppendLine("</tbody></table>");
            sb.AppendFormat("<p><strong>行为指标解读：</strong><br>{0}</p>\n", EscapeHtml(profile.biomarkerInterpretation).Replace("\n", "<br>"));
            sb.AppendFormat("<p><strong>{0}</strong></p>\n", EscapeHtml(profile.trainingPrescription));
            sb.AppendLine("</div>");

            // Section 2: 3 Games Standard Metrics Table
            sb.AppendLine("<div class=\"card\"><h2>二、三项情境游戏分类统计表（按原始口径独立汇总）</h2>");
            sb.AppendLine("<table><thead><tr><th>游戏名称</th><th>完成 / 中途结束</th><th>已完成答对 / 计划总题数</th><th>正确率</th><th>平均作答时间</th><th>错误 / 遗漏 / 跳过</th></tr></thead><tbody>");
            foreach (var g in new[] { profile.memorySummary, profile.attentionSummary, profile.kitchenSummary })
            {
                sb.AppendFormat("<tr><td>{0}</td><td>{1} / {2}</td><td>{3} / {4}</td><td>{5}</td><td>{6}</td><td>误选/序错 {7} · 遗漏 {8} · 跳过 {9}</td></tr>\n",
                    EscapeHtml(g.gameName),
                    g.completedSessions,
                    g.abortedSessions,
                    g.totalCorrect,
                    g.totalPlanned,
                    FormatAccuracy(g.meanAccuracy),
                    FormatReaction(g.meanReactionMs),
                    g.totalErrors,
                    g.totalOmissions,
                    g.totalSkipped);
            }
            sb.AppendLine("</tbody></table></div>");

            // Section 3: Recent 8 Session Details
            sb.AppendLine("<div class=\"card\"><h2>三、近期训练明细（展示最近 8 条）</h2>");
            sb.AppendLine("<table><thead><tr><th>日期 / 代号</th><th>游戏与任务</th><th>难度 / 类型</th><th>状态</th><th>答对 / 已答</th><th>平均反应时</th></tr></thead><tbody>");
            foreach (var r in sorted.Take(8))
            {
                sb.AppendFormat("<tr><td>{0}<br><small>{1}</small></td><td>{2} · {3}</td><td>{4} / {5}</td><td>{6}</td><td>{7} / {8}</td><td>{9}</td></tr>\n",
                    EscapeHtml(ExtractDateDay(r.startedAt)),
                    EscapeHtml(string.IsNullOrEmpty(r.participant) ? "未填写代号" : r.participant),
                    EscapeHtml(r.gameName),
                    EscapeHtml(!string.IsNullOrEmpty(r.taskName) ? r.taskName : r.sceneTitle),
                    EscapeHtml(YijingZones.DifficultyLabel(r.difficulty, r.game == "memory")),
                    r.mode == "formal" ? "正式" : "练习",
                    r.status == "completed" ? "已完成" : "中途结束",
                    r.metrics.correct,
                    r.metrics.answered,
                    FormatReaction(r.metrics.meanReactionMs));
            }
            sb.AppendLine("</tbody></table>");
            sb.AppendLine("<small>口径说明：正确率 = 正确作答数 / 已提交题数（主动跳过和超时计入已提交题数但不计正确）；平均作答时间仅统计有效选择（含答错，不含跳过和超时）。评估分数用于训练表现量化追踪与干预处方推荐，不等同于临床医学诊断结论。</small>");
            sb.AppendLine("</div></body></html>");

            string exportDir = Path.Combine(Application.dataPath, "../../导出报告");
            try
            {
                Directory.CreateDirectory(exportDir);
                string fileName = (profile.isSimulationData ? "忆境3D_模拟评估报告_" : "忆境3D_认知评估报告_") + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html";
                string fullPath = Path.GetFullPath(Path.Combine(exportDir, fileName));
                File.WriteAllText(fullPath, sb.ToString(), Encoding.UTF8);
                return fullPath;
            }
            catch
            {
                string fallback = Path.Combine(Application.persistentDataPath, "Yijing_Report.html");
                File.WriteAllText(fallback, sb.ToString(), Encoding.UTF8);
                return fallback;
            }
        }

        public static string ExportCsvRecords(List<TrainingSessionRecord> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('\ufeff');
            sb.AppendLine("\"记录编号\",\"参与者代号\",\"游戏类型\",\"任务或场景\",\"难度\",\"模式\",\"状态\",\"答对数\",\"已答题数\",\"正确率\",\"平均反应毫秒\",\"误选或序错数\",\"超时遗漏数\",\"主动跳过数\",\"开始时间\"");
            foreach (var r in rows)
            {
                YijingAssetLibrary.NormalizeRecordMetrics(r);
                sb.AppendFormat("\"{0}\",\"{1}\",\"{2}\",\"{3}\",\"{4}\",\"{5}\",\"{6}\",\"{7}\",\"{8}\",\"{9}\",\"{10}\",\"{11}\",\"{12}\",\"{13}\",\"{14}\"\r\n",
                    r.id,
                    r.participant ?? "",
                    r.gameName ?? "",
                    !string.IsNullOrEmpty(r.taskName) ? r.taskName : r.sceneTitle,
                    YijingZones.DifficultyLabel(r.difficulty, r.game == "memory"),
                    r.mode == "formal" ? "正式训练" : "练习",
                    r.status == "completed" ? "已完成" : "中途结束",
                    r.metrics.correct,
                    r.metrics.answered,
                    FormatAccuracy(r.metrics.accuracy),
                    r.metrics.meanReactionMs > 0 ? r.metrics.meanReactionMs.ToString() : "",
                    r.metrics.errors,
                    r.metrics.omissions,
                    r.metrics.skipped,
                    r.startedAt ?? "");
            }

            string exportDir = Path.Combine(Application.dataPath, "../../导出报告");
            Directory.CreateDirectory(exportDir);
            string fullPath = Path.GetFullPath(Path.Combine(exportDir, "忆境3D_训练统计汇总_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv"));
            File.WriteAllText(fullPath, sb.ToString(), Encoding.UTF8);
            return fullPath;
        }

        private static void AppendDomainRow(StringBuilder sb, string domain, string taskDesc, float score)
        {
            string status = score >= 80f ? "表现优良" : (score >= 65f ? "表现平稳" : "建议强化");
            sb.AppendFormat("<tr><td><strong>{0}</strong></td><td>{1}</td><td><span class=\"bar-wrap\"><span class=\"bar-fill\" style=\"width:{2}%\"></span></span><strong>{3:F1}</strong></td><td>{4}</td></tr>\n",
                EscapeHtml(domain), EscapeHtml(taskDesc), Mathf.Clamp(Mathf.RoundToInt(score), 0, 100), score, status);
        }

        public static string FormatAccuracy(float acc)
        {
            if (acc < 0f) return "—";
            return Mathf.RoundToInt(acc * 100f) + "%";
        }

        public static string FormatReaction(int ms)
        {
            if (ms <= 0) return "—";
            return (ms / 1000f).ToString("F1") + " 秒";
        }

        public static string ExtractDateDay(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return DateTime.Now.ToString("yyyy-MM-dd");
            if (iso.Length >= 10) return iso.Substring(0, 10);
            return iso;
        }

        private static string EscapeHtml(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
