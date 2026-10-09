using System.Text.Json;
using System.Text.Json.Nodes;
using ClassIsland.Automation.Core.Models;
using ClassIsland.Shared;
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

                case "get_schedule_details":
                    return ExecuteGetScheduleDetails(toolCall.Id, args);

                case "upsert_subjects":
                    return ExecuteUpsertSubjects(toolCall.Id, args);

                case "create_time_layout":
                    return ExecuteCreateTimeLayout(toolCall.Id, args);

                case "set_class_plan":
                    return ExecuteSetClassPlan(toolCall.Id, args);

                case "setup_temp_class_plan":
                    return ExecuteSetupTempClassPlan(toolCall.Id, args);

                case "get_app_settings":
                    return ExecuteGetAppSettings(toolCall.Id);

                case "update_app_settings":
                    return ExecuteUpdateAppSettings(toolCall.Id, args);

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
            ["classPlans"] = new JsonArray(p.ClassPlans.Values.Select(c => (JsonNode)c.Name).ToArray()),
            ["isOverlayEnabled"] = p.IsOverlayClassPlanEnabled,
            ["activeOverlayId"] = p.OverlayClassPlanId?.ToString() ?? "无"
        };

        return new ToolExecutionResult
        {
            ToolCallId = toolId,
            Name = "get_profile_summary",
            Success = true,
            Result = summary.ToJsonString()
        };
    }

    private ToolExecutionResult ExecuteGetScheduleDetails(string toolId, JsonObject args)
    {
        if (_profileService?.Profile == null)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "get_schedule_details", Success = false, Result = "Profile 服务未就绪" };
        }

        var profile = _profileService.Profile;
        var planNameQuery = args["planName"]?.GetValue<string>()?.Trim();
        var targetDateQuery = args["targetDate"]?.GetValue<string>()?.Trim();

        var subjects = profile.Subjects;
        var resultObj = new JsonObject();
        var plansArray = new JsonArray();

        var queryPlans = profile.ClassPlans.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(planNameQuery))
        {
            queryPlans = queryPlans.Where(kv =>
                kv.Value.Name.Contains(planNameQuery, StringComparison.OrdinalIgnoreCase) ||
                NormalizeWeekDayName(kv.Value.Name).Contains(NormalizeWeekDayName(planNameQuery)));
        }

        foreach (var kv in queryPlans)
        {
            var plan = kv.Value;
            var planObj = new JsonObject
            {
                ["id"] = kv.Key.ToString(),
                ["name"] = plan.Name,
                ["isEnabled"] = plan.IsEnabled,
                ["isOverlay"] = plan.IsOverlay,
                ["timeRuleWeekDay"] = GetChineseWeekDay((DayOfWeek)plan.TimeRule.WeekDay)
            };

            TimeLayout? layout = null;
            if (profile.TimeLayouts.TryGetValue(plan.TimeLayoutId, out var tl))
            {
                layout = tl;
                planObj["timeLayoutName"] = tl.Name;
            }

            var classesArray = new JsonArray();
            var validLessons = layout?.Layouts.Where(l => l.TimeType == 0).ToList() ?? new List<TimeLayoutItem>();

            for (int i = 0; i < plan.Classes.Count; i++)
            {
                var c = plan.Classes[i];
                var subName = subjects.TryGetValue(c.SubjectId, out var s) ? s.Name : "未设置科目";
                var teacherName = subjects.TryGetValue(c.SubjectId, out var s2) ? s2.TeacherName : "";
                var timeSlot = i < validLessons.Count ? $"{validLessons[i].StartTime:hh\\:mm} - {validLessons[i].EndTime:hh\\:mm}" : "";

                var itemObj = new JsonObject
                {
                    ["index"] = i + 1, // 1 表示第 1 节课
                    ["subject"] = subName,
                    ["teacher"] = teacherName,
                    ["time"] = timeSlot,
                    ["isChanged"] = c.IsChangedClass
                };
                classesArray.Add(itemObj);
            }
            planObj["classes"] = classesArray;
            plansArray.Add(planObj);
        }

        resultObj["classPlans"] = plansArray;

        var ordersArray = new JsonArray();
        foreach (var ord in profile.OrderedSchedules)
        {
            var targetPlanName = profile.ClassPlans.TryGetValue(ord.Value.ClassPlanId, out var cp) ? cp.Name : "未知课表";
            ordersArray.Add(new JsonObject
            {
                ["date"] = ord.Key.ToString("yyyy-MM-dd"),
                ["planName"] = targetPlanName
            });
        }
        resultObj["orderedSchedules"] = ordersArray;

        return new ToolExecutionResult
        {
            ToolCallId = toolId,
            Name = "get_schedule_details",
            Success = true,
            Result = resultObj.ToJsonString()
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

        int detectedWeekDay = ParseWeekDay(planName);
        if (detectedWeekDay >= 0)
        {
            plan.TimeRule.WeekDay = detectedWeekDay;
        }

        for (int i = 0; i < clsArray.Count; i++)
        {
            var subName = clsArray[i]?.GetValue<string>()?.Trim() ?? "";
            Guid subId = Guid.Empty;
            if (!string.IsNullOrWhiteSpace(subName))
            {
                if (!subMap.TryGetValue(subName, out subId))
                {
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
            Result = $"课表【{planName}】已配置完毕，共安排 {plan.Classes.Count} 节课（对应：{(detectedWeekDay >= 0 ? GetChineseWeekDay((DayOfWeek)detectedWeekDay) : "未指定固定星期")}）。"
        };
    }

    private ToolExecutionResult ExecuteSetupTempClassPlan(string toolId, JsonObject args)
    {
        if (_profileService == null || _profileService.Profile == null)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "setup_temp_class_plan", Success = false, Result = "Profile 服务未就绪" };
        }

        var profile = _profileService.Profile;

        var planName = args["sourcePlanName"]?.GetValue<string>()?.Trim()
                       ?? args["dayOfWeek"]?.GetValue<string>()?.Trim()
                       ?? args["plan"]?.GetValue<string>()?.Trim()
                       ?? "";

        var dateQuery = args["targetDate"]?.GetValue<string>()?.Trim()
                        ?? args["date"]?.GetValue<string>()?.Trim();

        int? classIndex = null;
        if (args.TryGetPropertyValue("classIndex", out var ciNode))
        {
            if (ciNode != null && int.TryParse(ciNode.ToString(), out var parsedCi))
            {
                classIndex = parsedCi;
            }
        }

        var subjectName = args["subject"]?.GetValue<string>()?.Trim()
                          ?? args["targetSubject"]?.GetValue<string>()?.Trim();

        var targetDate = ResolveTargetDate(dateQuery, planName);

        var sourcePlanEntry = FindSourcePlan(profile, planName, targetDate);
        if (sourcePlanEntry.Value == null)
        {
            return new ToolExecutionResult
            {
                ToolCallId = toolId,
                Name = "setup_temp_class_plan",
                Success = false,
                Result = $"未能匹配到适合的基础课表【{planName}】。当前已有课表：{string.Join(", ", profile.ClassPlans.Values.Select(c => c.Name))}"
            };
        }

        var sourceKey = sourcePlanEntry.Key;
        var sourcePlan = sourcePlanEntry.Value;

        ClassPlan? tempPlan = null;
        Guid tempId = Guid.Empty;

        if (profile.OrderedSchedules.TryGetValue(targetDate.Date, out var existingOrdered)
            && profile.ClassPlans.TryGetValue(existingOrdered.ClassPlanId, out var existingOverlay)
            && existingOverlay.IsOverlay)
        {
            tempId = existingOrdered.ClassPlanId;
            tempPlan = existingOverlay;
        }
        else
        {
            var createdId = _profileService.CreateTempClassPlan(sourceKey, null, targetDate.Date);
            if (createdId != null && profile.ClassPlans.TryGetValue(createdId.Value, out var cp))
            {
                tempId = createdId.Value;
                tempPlan = cp;
            }
            else
            {
                try
                {
                    var serialized = JsonSerializer.Serialize(sourcePlan);
                    var newPlan = JsonSerializer.Deserialize<ClassPlan>(serialized) ?? new ClassPlan();
                    newPlan.IsOverlay = true;
                    newPlan.OverlaySourceId = sourceKey;
                    newPlan.Name = sourcePlan.Name + "（临时层）";
                    newPlan.OverlaySetupTime = targetDate.Date;
                    tempId = Guid.NewGuid();
                    profile.ClassPlans[tempId] = newPlan;
                    profile.OrderedSchedules[targetDate.Date] = new OrderedSchedule { ClassPlanId = tempId };
                    tempPlan = newPlan;
                }
                catch (Exception ex)
                {
                    return new ToolExecutionResult { ToolCallId = toolId, Name = "setup_temp_class_plan", Success = false, Result = $"复制创建临时课表异常: {ex.Message}" };
                }
            }
        }

        if (tempPlan == null)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "setup_temp_class_plan", Success = false, Result = "创建或获取临时课表对象失败" };
        }

        string changeDescription = "";
        var subMap = profile.Subjects.ToDictionary(kv => kv.Value.Name, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

        var targetIndices = new List<int>();
        if (args.TryGetPropertyValue("classIndices", out var cisNode) && cisNode is JsonArray cisArr)
        {
            foreach (var item in cisArr)
            {
                if (item != null && int.TryParse(item.ToString(), out var ci))
                {
                    targetIndices.Add(ci);
                }
            }
        }
        else if (classIndex.HasValue)
        {
            targetIndices.Add(classIndex.Value);
        }

        if (targetIndices.Count > 0 && !string.IsNullOrWhiteSpace(subjectName))
        {
            if (!subMap.TryGetValue(subjectName, out var targetSubGuid))
            {
                targetSubGuid = Guid.NewGuid();
                var newSub = new Subject { Name = subjectName, Initial = subjectName.Substring(0, 1) };
                profile.Subjects[targetSubGuid] = newSub;
                subMap[subjectName] = targetSubGuid;
            }

            var changedSlots = new List<string>();
            foreach (var ci in targetIndices)
            {
                int targetIdx = ci == -1 ? tempPlan.Classes.Count - 1 : (ci > 0 ? ci - 1 : 0);
                if (targetIdx >= 0 && targetIdx < tempPlan.Classes.Count)
                {
                    var oldSubName = profile.Subjects.TryGetValue(tempPlan.Classes[targetIdx].SubjectId, out var oldSub) ? oldSub.Name : "未定";
                    tempPlan.Classes[targetIdx].SubjectId = targetSubGuid;
                    tempPlan.Classes[targetIdx].IsChangedClass = true;
                    changedSlots.Add($"第 {targetIdx + 1} 节（原【{oldSubName}】）");
                }
            }

            changeDescription = $"已将 {string.Join(" 与 ", changedSlots)} 变更为【{subjectName}】";
        }
        else if (args.TryGetPropertyValue("modifiedClasses", out var modNode) && modNode is JsonArray modArr)
        {
            int changedCount = 0;
            for (int i = 0; i < Math.Min(modArr.Count, tempPlan.Classes.Count); i++)
            {
                var sName = modArr[i]?.GetValue<string>()?.Trim();
                if (string.IsNullOrWhiteSpace(sName)) continue;

                if (!subMap.TryGetValue(sName, out var sGuid))
                {
                    sGuid = Guid.NewGuid();
                    var newSub = new Subject { Name = sName, Initial = sName.Substring(0, 1) };
                    profile.Subjects[sGuid] = newSub;
                    subMap[sName] = sGuid;
                }

                tempPlan.Classes[i].SubjectId = sGuid;
                tempPlan.Classes[i].IsChangedClass = true;
                changedCount++;
            }
            changeDescription = $"已更新 {changedCount} 节课程安排";
        }
        else
        {
            changeDescription = "已同步生成临时层";
        }

        profile.IsOverlayClassPlanEnabled = true;
        profile.OverlayClassPlanId = tempId;
        profile.TempClassPlanId = tempId;
        profile.TempClassPlanSetupTime = targetDate.Date;
        profile.OrderedSchedules[targetDate.Date] = new OrderedSchedule { ClassPlanId = tempId };

        _profileService.SaveProfile();

        return new ToolExecutionResult
        {
            ToolCallId = toolId,
            Name = "setup_temp_class_plan",
            Success = true,
            Result = $"临时调课已生效！\n" +
                     $"• 生效日期：{targetDate:yyyy-MM-dd}（{GetChineseWeekDay(targetDate.DayOfWeek)}）\n" +
                     $"• 基础课表：【{sourcePlan.Name}】\n" +
                     $"• 调整内容：{changeDescription}\n" +
                     $"• 状态：已即时激活并在 ClassIsland 中生效显示。"
        };
    }

    private ToolExecutionResult ExecuteGetAppSettings(string toolId)
    {
        var settingsObj = GetAppHostSettingsObject();
        if (settingsObj == null)
        {
            return new ToolExecutionResult
            {
                ToolCallId = toolId,
                Name = "get_app_settings",
                Success = false,
                Result = "当前宿主设置服务未就绪或未找到"
            };
        }

        var resultObj = new JsonObject();
        var type = settingsObj.GetType();
        string[] keyProps = [
            "IsNotificationEnabled", "Theme", "Opacity", "Scale",
            "HideOnClass", "HideOnFullscreen", "HideOnMaxWindow",
            "ShowDate", "IsDebugEnabled", "SelectedProfile",
            "IsClassPrepareNotificationEnabled", "IsClassOffNotificationEnabled",
            "IsClassChangingNotificationEnabled"
        ];

        foreach (var propName in keyProps)
        {
            var p = type.GetProperty(propName);
            if (p != null)
            {
                var val = p.GetValue(settingsObj);
                if (val is bool b) resultObj[propName] = b;
                else if (val is int i) resultObj[propName] = i;
                else if (val is double d) resultObj[propName] = d;
                else if (val != null) resultObj[propName] = val.ToString();
            }
        }

        return new ToolExecutionResult
        {
            ToolCallId = toolId,
            Name = "get_app_settings",
            Success = true,
            Result = resultObj.ToJsonString()
        };
    }

    private ToolExecutionResult ExecuteUpdateAppSettings(string toolId, JsonObject args)
    {
        var settingsObj = GetAppHostSettingsObject();
        if (settingsObj == null)
        {
            return new ToolExecutionResult
            {
                ToolCallId = toolId,
                Name = "update_app_settings",
                Success = false,
                Result = "当前宿主设置服务未就绪，无法通过接口修改设置"
            };
        }

        var type = settingsObj.GetType();
        var updatedList = new List<string>();

        void TrySetProp(string propName, object value)
        {
            var p = type.GetProperty(propName);
            if (p != null && p.CanWrite)
            {
                var converted = Convert.ChangeType(value, p.PropertyType);
                p.SetValue(settingsObj, converted);
                updatedList.Add($"{propName} = {value}");
            }
        }

        if (args.TryGetPropertyValue("isNotificationEnabled", out var notifNode) && notifNode != null)
        {
            TrySetProp("IsNotificationEnabled", notifNode.GetValue<bool>());
        }
        if (args.TryGetPropertyValue("theme", out var themeNode) && themeNode != null)
        {
            int t = themeNode.GetValue<int>();
            TrySetProp("Theme", t);
            try
            {
                var themeServiceType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                    .FirstOrDefault(x => x.FullName == "ClassIsland.Core.Abstractions.Services.IThemeService");
                if (themeServiceType != null)
                {
                    var ts = IAppHost.Host?.Services.GetService(themeServiceType);
                    var setMethod = themeServiceType.GetMethod("SetTheme");
                    setMethod?.Invoke(ts, new object?[] { t, null });
                }
            }
            catch { }
        }
        if (args.TryGetPropertyValue("opacity", out var opNode) && opNode != null)
        {
            TrySetProp("Opacity", opNode.GetValue<double>());
        }
        if (args.TryGetPropertyValue("scale", out var scNode) && scNode != null)
        {
            TrySetProp("Scale", scNode.GetValue<double>());
        }
        if (args.TryGetPropertyValue("hideOnClass", out var hcNode) && hcNode != null)
        {
            TrySetProp("HideOnClass", hcNode.GetValue<bool>());
        }
        if (args.TryGetPropertyValue("hideOnFullscreen", out var hfNode) && hfNode != null)
        {
            TrySetProp("HideOnFullscreen", hfNode.GetValue<bool>());
        }
        if (args.TryGetPropertyValue("isClassPrepareNotificationEnabled", out var cpNode) && cpNode != null)
        {
            TrySetProp("IsClassPrepareNotificationEnabled", cpNode.GetValue<bool>());
        }
        if (args.TryGetPropertyValue("isClassOffNotificationEnabled", out var coNode) && coNode != null)
        {
            TrySetProp("IsClassOffNotificationEnabled", coNode.GetValue<bool>());
        }

        if (args.TryGetPropertyValue("customSettings", out var customNode) && customNode is JsonObject customObj)
        {
            foreach (var kv in customObj)
            {
                if (kv.Value != null)
                {
                    TrySetProp(kv.Key, kv.Value.ToString());
                }
            }
        }

        if (updatedList.Count == 0)
        {
            return new ToolExecutionResult
            {
                ToolCallId = toolId,
                Name = "update_app_settings",
                Success = false,
                Result = "未提供或未匹配到需要修改的有效设置项"
            };
        }

        return new ToolExecutionResult
        {
            ToolCallId = toolId,
            Name = "update_app_settings",
            Success = true,
            Result = $"设置修改成功且已即时生效（无需用户手动点击）：\n• " + string.Join("\n• ", updatedList)
        };
    }

    private static object? GetAppHostSettingsObject()
    {
        try
        {
            var host = IAppHost.Host;
            if (host == null) return null;

            var serviceType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(t => t.FullName == "ClassIsland.Services.SettingsService");

            if (serviceType == null) return null;

            var serviceInstance = host.Services.GetService(serviceType);
            if (serviceInstance == null) return null;

            var settingsProp = serviceType.GetProperty("Settings");
            return settingsProp?.GetValue(serviceInstance);
        }
        catch
        {
            return null;
        }
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

    private static DateTime ResolveTargetDate(string? dateQuery, string? planName)
    {
        var today = DateTime.Today;
        if (!string.IsNullOrWhiteSpace(dateQuery))
        {
            var q = dateQuery.Trim().ToLowerInvariant();
            if (q.Contains("明") || q.Contains("tomorrow"))
            {
                return today.AddDays(1);
            }
            if (q.Contains("后天"))
            {
                return today.AddDays(2);
            }
            if (q.Contains("今") || q.Contains("today"))
            {
                return today;
            }
            if (DateTime.TryParse(dateQuery, out var parsedDate))
            {
                return parsedDate.Date;
            }
            int targetWd = ParseWeekDay(dateQuery);
            if (targetWd >= 0)
            {
                int diff = (targetWd - (int)today.DayOfWeek + 7) % 7;
                return today.AddDays(diff);
            }
        }

        if (!string.IsNullOrWhiteSpace(planName))
        {
            int planWd = ParseWeekDay(planName);
            if (planWd >= 0)
            {
                int diff = (planWd - (int)today.DayOfWeek + 7) % 7;
                return today.AddDays(diff);
            }
        }

        return today;
    }

    private static KeyValuePair<Guid, ClassPlan> FindSourcePlan(Profile profile, string planName, DateTime targetDate)
    {
        if (!string.IsNullOrWhiteSpace(planName))
        {
            var exactMatch = profile.ClassPlans.FirstOrDefault(kv => !kv.Value.IsOverlay && string.Equals(kv.Value.Name, planName, StringComparison.OrdinalIgnoreCase));
            if (exactMatch.Value != null) return exactMatch;

            var normPlan = NormalizeWeekDayName(planName);
            var fuzzyMatch = profile.ClassPlans.FirstOrDefault(kv => !kv.Value.IsOverlay && NormalizeWeekDayName(kv.Value.Name).Contains(normPlan));
            if (fuzzyMatch.Value != null) return fuzzyMatch;
        }

        int targetDayInt = (int)targetDate.DayOfWeek;
        var ruleMatch = profile.ClassPlans.FirstOrDefault(kv => !kv.Value.IsOverlay && kv.Value.TimeRule.WeekDay == targetDayInt);
        if (ruleMatch.Value != null) return ruleMatch;

        var dayChar = GetChineseWeekDayChar(targetDate.DayOfWeek);
        var nameMatch = profile.ClassPlans.FirstOrDefault(kv => !kv.Value.IsOverlay && kv.Value.Name.Contains(dayChar));
        if (nameMatch.Value != null) return nameMatch;

        var fallback = profile.ClassPlans.FirstOrDefault(kv => !kv.Value.IsOverlay);
        return fallback;
    }

    public static int ParseWeekDay(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return -1;
        var s = name.Replace("礼拜", "周").Replace("星期", "周");
        if (s.Contains("周一") || s.Contains("星期一") || s.Contains("一")) return (int)DayOfWeek.Monday;
        if (s.Contains("周二") || s.Contains("星期二") || s.Contains("二")) return (int)DayOfWeek.Tuesday;
        if (s.Contains("周三") || s.Contains("星期三") || s.Contains("三")) return (int)DayOfWeek.Wednesday;
        if (s.Contains("周四") || s.Contains("星期四") || s.Contains("四")) return (int)DayOfWeek.Thursday;
        if (s.Contains("周五") || s.Contains("星期五") || s.Contains("五")) return (int)DayOfWeek.Friday;
        if (s.Contains("周六") || s.Contains("星期六") || s.Contains("六")) return (int)DayOfWeek.Saturday;
        if (s.Contains("周日") || s.Contains("周天") || s.Contains("星期日") || s.Contains("星期天") || s.Contains("日") || s.Contains("天")) return (int)DayOfWeek.Sunday;
        return -1;
    }

    public static string NormalizeWeekDayName(string name)
    {
        return name.Replace("星期", "周").Replace("礼拜", "周").Trim();
    }

    public static string GetChineseWeekDay(DayOfWeek dow)
    {
        return dow switch
        {
            DayOfWeek.Monday => "周一",
            DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五",
            DayOfWeek.Saturday => "周六",
            DayOfWeek.Sunday => "周日",
            _ => "未知"
        };
    }

    public static string GetChineseWeekDayChar(DayOfWeek dow)
    {
        return dow switch
        {
            DayOfWeek.Monday => "一",
            DayOfWeek.Tuesday => "二",
            DayOfWeek.Wednesday => "三",
            DayOfWeek.Thursday => "四",
            DayOfWeek.Friday => "五",
            DayOfWeek.Saturday => "六",
            DayOfWeek.Sunday => "日",
            _ => ""
        };
    }
}
