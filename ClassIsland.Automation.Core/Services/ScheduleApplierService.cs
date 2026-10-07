using System.Text.Json;
using ClassIsland.Automation.Core.Models;
using ClassIsland.Shared.IPC.Abstractions.Services;
using ClassIsland.Shared.Models.Profile;

namespace ClassIsland.Automation.Core.Services;

public class ScheduleApplierService
{
    /// <summary>
    /// 将 AI 识别的结果应用到 ClassIsland 的 Profile 中
    /// </summary>
    public static void ApplyToProfile(IPublicProfileService profileService, AiExtractedSchedule extracted)
    {
        var profile = profileService.Profile;
        if (profile == null)
            return;

        // 1. 处理科目
        var subjectMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        // 先建立已有科目字典
        foreach (var pair in profile.Subjects)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value.Name) && !subjectMap.ContainsKey(pair.Value.Name))
            {
                subjectMap[pair.Value.Name] = pair.Key;
            }
        }

        foreach (var aiSub in extracted.Subjects)
        {
            if (string.IsNullOrWhiteSpace(aiSub.Name))
                continue;

            if (!subjectMap.TryGetValue(aiSub.Name, out var subId))
            {
                subId = Guid.NewGuid();
                var sub = new Subject
                {
                    Name = aiSub.Name,
                    Initial = string.IsNullOrWhiteSpace(aiSub.Initial) ? aiSub.Name.Substring(0, 1) : aiSub.Initial,
                    TeacherName = aiSub.TeacherName,
                    IsOutDoor = aiSub.IsOutDoor
                };
                profile.Subjects[subId] = sub;
                subjectMap[aiSub.Name] = subId;
            }
            else
            {
                // 已有科目更新教师等
                var existing = profile.Subjects[subId];
                if (!string.IsNullOrWhiteSpace(aiSub.TeacherName))
                    existing.TeacherName = aiSub.TeacherName;
                existing.IsOutDoor = aiSub.IsOutDoor;
            }
        }

        // 2. 处理时间表 (TimeLayout)
        Guid timeLayoutId = Guid.NewGuid();
        if (extracted.TimeLayout != null && extracted.TimeLayout.Items.Count > 0)
        {
            var layout = new TimeLayout
            {
                Name = string.IsNullOrWhiteSpace(extracted.TimeLayout.Name) ? "AI智能时间表" : extracted.TimeLayout.Name
            };

            foreach (var item in extracted.TimeLayout.Items)
            {
                TimeSpan.TryParse(item.StartTime, out var start);
                TimeSpan.TryParse(item.EndTime, out var end);

                Guid defaultSubId = Guid.Empty;
                if (!string.IsNullOrWhiteSpace(item.DefaultSubject) && subjectMap.TryGetValue(item.DefaultSubject, out var matchedId))
                {
                    defaultSubId = matchedId;
                }

                var point = new TimeLayoutItem
                {
                    StartTime = start,
                    EndTime = end,
                    TimeType = item.TimeType,
                    BreakName = item.BreakName ?? "",
                    DefaultClassId = defaultSubId
                };
                layout.Layouts.Add(point);
            }

            profile.TimeLayouts[timeLayoutId] = layout;
        }
        else
        {
            // 如果 AI 未给出独立时间表，优先使用已有时间表首项
            if (profile.TimeLayouts.Count > 0)
            {
                timeLayoutId = profile.TimeLayouts.Keys.First();
            }
        }

        // 3. 处理课表 (ClassPlans)
        if (extracted.ClassPlans != null && extracted.ClassPlans.Count > 0)
        {
            foreach (var aiPlan in extracted.ClassPlans)
            {
                if (string.IsNullOrWhiteSpace(aiPlan.Name))
                    continue;

                var plan = new ClassPlan
                {
                    Name = aiPlan.Name,
                    TimeLayoutId = timeLayoutId,
                    IsEnabled = true
                };

                for (int i = 0; i < aiPlan.Classes.Count; i++)
                {
                    var className = aiPlan.Classes[i];
                    Guid subGuid = Guid.Empty;
                    if (!string.IsNullOrWhiteSpace(className) && subjectMap.TryGetValue(className, out var sId))
                    {
                        subGuid = sId;
                    }

                    plan.Classes.Add(new ClassInfo
                    {
                        Index = i,
                        SubjectId = subGuid,
                        IsEnabled = true
                    });
                }

                profile.ClassPlans[Guid.NewGuid()] = plan;
            }
        }

        // 4. 保存更改
        profileService.SaveProfile();
    }
}
