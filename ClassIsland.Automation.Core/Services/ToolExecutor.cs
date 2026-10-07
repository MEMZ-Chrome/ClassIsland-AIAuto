using System.Text.Json;
using System.Text.Json.Nodes;
using ClassIsland.Automation.Core.Models;
using ClassIsland.Shared.IPC.Abstractions.Services;
using ClassIsland.Shared.Models.Profile;

namespace ClassIsland.Automation.Core.Services;

public class ToolExecutor
{
    private readonly IPublicProfileService? _profileService;
    private readonly IPublicUriNavigationService? _uriService;

    public ToolExecutor(IPublicProfileService? profileService, IPublicUriNavigationService? uriService = null)
    {
        _profileService = profileService;
        _uriService = uriService;
    }

    public ToolExecutionResult Execute(ToolCallInfo toolCall)
    {
        try
        {
            var args = toolCall.Arguments;
            switch (toolCall.Name)
            {
                case "get_profile_summary":
                    return ExecuteGetProfileSummary(toolCall.Id);

                case "upsert_subjects":
                    return ExecuteUpsertSubjects(toolCall.Id, args);

                case "create_time_layout":
                    return ExecuteCreateTimeLayout(toolCall.Id, args);

                case "set_class_plan":
                    return ExecuteSetClassPlan(toolCall.Id, args);

                case "setup_temp_class_plan":
                    return ExecuteSetupTempClassPlan(toolCall.Id, args);

                case "navigate_app_page":
                    return ExecuteNavigateAppPage(toolCall.Id, args);

                default:
                    return new ToolExecutionResult
                    {
                        ToolCallId = toolCall.Id,
                        Name = toolCall.Name,
                        Success = false,
                        Result = $"未知的工具名称: {toolCall.Name}"
                    };
            }
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult
            {
                ToolCallId = toolCall.Id,
                Name = toolCall.Name,
                Success = false,
                Result = $"执行失败: {ex.Message}"
            };
        }
    }

    private ToolExecutionResult ExecuteGetProfileSummary(string toolId)
    {
        if (_profileService?.Profile == null)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "get_profile_summary", Success = false, Result = "当前 Profile 服务未就绪" };
        }

        var p = _profileService.Profile;
        var summary = new JsonObject
        {
            ["profileName"] = p.Name,
            ["subjectCount"] = p.Subjects.Count,
            ["subjects"] = new JsonArray(p.Subjects.Values.Select(s => (JsonNode)s.Name).ToArray()),
            ["timeLayouts"] = new JsonArray(p.TimeLayouts.Values.Select(t => (JsonNode)t.Name).ToArray()),
            ["classPlans"] = new JsonArray(p.ClassPlans.Values.Select(c => (JsonNode)c.Name).ToArray())
        };

        return new ToolExecutionResult
        {
            ToolCallId = toolId,
            Name = "get_profile_summary",
            Success = true,
            Result = summary.ToJsonString()
        };
    }

    private ToolExecutionResult ExecuteUpsertSubjects(string toolId, JsonObject args)
    {
        if (_profileService?.Profile == null)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "upsert_subjects", Success = false, Result = "Profile 服务未就绪" };
        }

        var profile = _profileService.Profile;
        if (!args.TryGetPropertyValue("subjects", out var subsNode) || subsNode is not JsonArray subsArray)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "upsert_subjects", Success = false, Result = "缺少 subjects 列表参数" };
        }

        int addedCount = 0;
        int updatedCount = 0;

        foreach (var subNode in subsArray)
        {
            if (subNode is not JsonObject sObj) continue;
            var name = sObj["name"]?.GetValue<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var initial = sObj["initial"]?.GetValue<string>()?.Trim();
            var teacher = sObj["teacherName"]?.GetValue<string>()?.Trim() ?? "";
            var isOutDoor = sObj["isOutDoor"]?.GetValue<bool>() ?? false;

            // 查找是否已存在同名科目
            var existingEntry = profile.Subjects.FirstOrDefault(kv => string.Equals(kv.Value.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existingEntry.Value != null)
            {
                existingEntry.Value.TeacherName = teacher;
                existingEntry.Value.IsOutDoor = isOutDoor;
                if (!string.IsNullOrWhiteSpace(initial)) existingEntry.Value.Initial = initial;
                updatedCount++;
            }
            else
            {
                var newSub = new Subject
                {
                    Name = name,
                    Initial = string.IsNullOrWhiteSpace(initial) ? name.Substring(0, 1) : initial,
                    TeacherName = teacher,
                    IsOutDoor = isOutDoor
                };
                profile.Subjects[Guid.NewGuid()] = newSub;
                addedCount++;
            }
        }

        _profileService.SaveProfile();
        return new ToolExecutionResult
        {
            ToolCallId = toolId,
            Name = "upsert_subjects",
            Success = true,
            Result = $"成功添加 {addedCount} 个科目，更新 {updatedCount} 个科目。"
        };
    }

    private ToolExecutionResult ExecuteCreateTimeLayout(string toolId, JsonObject args)
    {
        if (_profileService?.Profile == null)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "create_time_layout", Success = false, Result = "Profile 服务未就绪" };
        }

        var profile = _profileService.Profile;
        var layoutName = args["layoutName"]?.GetValue<string>()?.Trim() ?? "时间表";
        if (!args.TryGetPropertyValue("items", out var itemsNode) || itemsNode is not JsonArray itemsArray)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "create_time_layout", Success = false, Result = "缺少 items 节点列表" };
        }

        // 查找或新建时间表
        var existingLayout = profile.TimeLayouts.FirstOrDefault(kv => string.Equals(kv.Value.Name, layoutName, StringComparison.OrdinalIgnoreCase));
        TimeLayout layout;
        Guid layoutId;
        if (existingLayout.Value != null)
        {
            layout = existingLayout.Value;
            layoutId = existingLayout.Key;
            layout.Layouts.Clear();
        }
        else
        {
            layout = new TimeLayout { Name = layoutName };
            layoutId = Guid.NewGuid();
            profile.TimeLayouts[layoutId] = layout;
        }

        foreach (var itemNode in itemsArray)
        {
            if (itemNode is not JsonObject itObj) continue;
            var startStr = itObj["startTime"]?.GetValue<string>() ?? "00:00:00";
            var endStr = itObj["endTime"]?.GetValue<string>() ?? "00:00:00";
            var timeType = itObj["timeType"]?.GetValue<int>() ?? 0;
            var breakName = itObj["breakName"]?.GetValue<string>() ?? "";

            TimeSpan.TryParse(startStr, out var startTime);
            TimeSpan.TryParse(endStr, out var endTime);

            layout.Layouts.Add(new TimeLayoutItem
            {
                StartTime = startTime,
                EndTime = endTime,
                TimeType = timeType,
                BreakName = breakName
            });
        }

        _profileService.SaveProfile();
        return new ToolExecutionResult
        {
            ToolCallId = toolId,
            Name = "create_time_layout",
            Success = true,
            Result = $"时间表【{layoutName}】已创建并保存，包含 {layout.Layouts.Count} 个节点。"
        };
    }

    private ToolExecutionResult ExecuteSetClassPlan(string toolId, JsonObject args)
    {
        if (_profileService?.Profile == null)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "set_class_plan", Success = false, Result = "Profile 服务未就绪" };
        }

        var profile = _profileService.Profile;
        var planName = args["planName"]?.GetValue<string>()?.Trim() ?? "新课表";
        var timeLayoutName = args["timeLayoutName"]?.GetValue<string>()?.Trim() ?? "";

        if (!args.TryGetPropertyValue("classes", out var clsNode) || clsNode is not JsonArray clsArray)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "set_class_plan", Success = false, Result = "缺少 classes 课程列表" };
        }

        // 匹配关联时间表
        Guid timeLayoutId = Guid.Empty;
        if (!string.IsNullOrWhiteSpace(timeLayoutName))
        {
            var match = profile.TimeLayouts.FirstOrDefault(kv => string.Equals(kv.Value.Name, timeLayoutName, StringComparison.OrdinalIgnoreCase));
            if (match.Value != null) timeLayoutId = match.Key;
        }
        if (timeLayoutId == Guid.Empty && profile.TimeLayouts.Count > 0)
        {
            timeLayoutId = profile.TimeLayouts.Keys.First();
        }

        // 建立科目名称索引
        var subMap = profile.Subjects.ToDictionary(kv => kv.Value.Name, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

        var existingPlan = profile.ClassPlans.FirstOrDefault(kv => string.Equals(kv.Value.Name, planName, StringComparison.OrdinalIgnoreCase));
        ClassPlan plan;
        if (existingPlan.Value != null)
        {
            plan = existingPlan.Value;
            plan.Classes.Clear();
            if (timeLayoutId != Guid.Empty) plan.TimeLayoutId = timeLayoutId;
        }
        else
        {
            plan = new ClassPlan
            {
                Name = planName,
                TimeLayoutId = timeLayoutId,
                IsEnabled = true
            };
            profile.ClassPlans[Guid.NewGuid()] = plan;
        }

        for (int i = 0; i < clsArray.Count; i++)
        {
            var subName = clsArray[i]?.GetValue<string>()?.Trim() ?? "";
            Guid subId = Guid.Empty;
            if (!string.IsNullOrWhiteSpace(subName))
            {
                if (!subMap.TryGetValue(subName, out subId))
                {
                    // 自动补充创建缺失科目
                    subId = Guid.NewGuid();
                    var newSub = new Subject { Name = subName, Initial = subName.Substring(0, 1) };
                    profile.Subjects[subId] = newSub;
                    subMap[subName] = subId;
                }
            }

            plan.Classes.Add(new ClassInfo
            {
                Index = i,
                SubjectId = subId,
                IsEnabled = true
            });
        }

        _profileService.SaveProfile();
        return new ToolExecutionResult
        {
            ToolCallId = toolId,
            Name = "set_class_plan",
            Success = true,
            Result = $"课表【{planName}】已配置完毕，共安排 {plan.Classes.Count} 节课。"
        };
    }

    private ToolExecutionResult ExecuteSetupTempClassPlan(string toolId, JsonObject args)
    {
        if (_profileService == null)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "setup_temp_class_plan", Success = false, Result = "Profile 服务未就绪" };
        }

        var sourcePlanName = args["sourcePlanName"]?.GetValue<string>()?.Trim() ?? "";
        var profile = _profileService.Profile;
        var sourcePlanEntry = profile.ClassPlans.FirstOrDefault(kv => string.Equals(kv.Value.Name, sourcePlanName, StringComparison.OrdinalIgnoreCase));
        if (sourcePlanEntry.Value == null)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "setup_temp_class_plan", Success = false, Result = $"未找到基础课表【{sourcePlanName}】" };
        }

        var tempId = _profileService.CreateTempClassPlan(sourcePlanEntry.Key, null, DateTime.Now);
        if (tempId == null || !profile.ClassPlans.TryGetValue(tempId.Value, out var tempPlan))
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "setup_temp_class_plan", Success = false, Result = "创建临时课表失败" };
        }

        if (args.TryGetPropertyValue("modifiedClasses", out var modNode) && modNode is JsonArray modArr)
        {
            var subMap = profile.Subjects.ToDictionary(kv => kv.Value.Name, kv => kv.Key, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Math.Min(modArr.Count, tempPlan.Classes.Count); i++)
            {
                var sName = modArr[i]?.GetValue<string>()?.Trim();
                if (!string.IsNullOrWhiteSpace(sName) && subMap.TryGetValue(sName, out var sGuid))
                {
                    tempPlan.Classes[i].SubjectId = sGuid;
                    tempPlan.Classes[i].IsChangedClass = true;
                }
            }
        }

        _profileService.SaveProfile();
        return new ToolExecutionResult
        {
            ToolCallId = toolId,
            Name = "setup_temp_class_plan",
            Success = true,
            Result = $"临时调课已就绪，已成功替换并激活当天临时课表！"
        };
    }

    private ToolExecutionResult ExecuteNavigateAppPage(string toolId, JsonObject args)
    {
        var uriStr = args["pageUri"]?.GetValue<string>()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(uriStr))
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "navigate_app_page", Success = false, Result = "URI 不能为空" };
        }

        if (_uriService != null)
        {
            _uriService.NavigateWrapped(new Uri(uriStr));
            return new ToolExecutionResult { ToolCallId = toolId, Name = "navigate_app_page", Success = true, Result = $"已成功跳转到页面: {uriStr}" };
        }

        return new ToolExecutionResult { ToolCallId = toolId, Name = "navigate_app_page", Success = false, Result = "导航服务不可用" };
    }
}
