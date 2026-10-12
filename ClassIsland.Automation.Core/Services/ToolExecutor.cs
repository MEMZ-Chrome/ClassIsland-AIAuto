using System.Text.Json;
using System.Text.Json.Nodes;
using ClassIsland.Automation.Core.Models;
using ClassIsland.Shared;
using ClassIsland.Shared.IPC.Abstractions.Services;
using ClassIsland.Shared.Models.Profile;

namespace ClassIsland.Automation.Core.Services;

public class ToolExecutor
{
    /// <summary>
    /// 鍙緵 AI 璇诲彇涓庝慨鏀圭殑 ClassIsland 瀹夸富璁剧疆椤圭櫧鍚嶅崟銆?    /// 浠呭寘鍚爣閲忕被鍨嬶紝閬垮厤璇敼闆嗗悎绫昏缃鑷村簲鐢ㄥ紓甯搞€?    /// </summary>
    private static readonly string[] AppSettingPropertyNames =
    [
        // 鏃堕棿涓庤鏃?        "TimeOffsetSeconds", "IsTimeAutoAdjustEnabled", "TimeAutoAdjustInterval",
        // 閫氱煡
        "IsNotificationEnabled", "IsClassPrepareNotificationEnabled",
        "IsClassOffNotificationEnabled", "IsClassChangingNotificationEnabled",
        "IsWeatherNotificationEnabled", "IsAfterClassNotificationEnabled",
        // 澶栬
        "Theme", "Opacity", "Scale", "ShowDate", "ShowWeekDay",
        "ShowSecond", "IsClockSecondsVisible", "IsMainWindowVisible",
        // 绐楀彛琛屼负
        "HideOnClass", "HideOnFullscreen", "HideOnMaxWindow",
        "IsMainWindowTopmost", "IsMousePenetrationEnabled",
        // 鍚姩涓庡叧闂?        "IsAutoStartupEnabled", "IsSentryEnabled", "IsDebugEnabled",
        "IsStartupUpdateCheckEnabled",
        // 妗ｆ
        "SelectedProfile", "IsSleepModeEnabled",
    ];

    private readonly IPublicProfileService? _profileService;
    private readonly IPublicUriNavigationService? _uriService;
    private readonly PluginSettings? _pluginSettings;
    private readonly Action? _onSavePluginSettings;

    public ToolExecutor(
        IPublicProfileService? profileService,
        IPublicUriNavigationService? uriService = null,
        PluginSettings? pluginSettings = null,
        Action? onSavePluginSettings = null)
    {
        _profileService = profileService;
        _uriService = uriService;
        _pluginSettings = pluginSettings;
        _onSavePluginSettings = onSavePluginSettings;
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

                case "get_current_time":
                    return ExecuteGetCurrentTime(toolCall.Id);

                case "update_memory":
                    return ExecuteUpdateMemory(toolCall.Id, args);

                case "execute_command":
                    return ExecuteCommand(toolCall.Id, args);

                case "get_plugin_settings":
                    return ExecuteGetPluginSettings(toolCall.Id);

                case "update_plugin_settings":
                    return ExecuteUpdatePluginSettings(toolCall.Id, args);

                default:
                    return new ToolExecutionResult
                    {
                        ToolCallId = toolCall.Id,
                        Name = toolCall.Name,
                        Success = false,
                        Result = $"鏈煡鐨勫伐鍏峰悕绉? {toolCall.Name}"
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
                Result = $"鎵ц澶辫触: {ex.Message}"
            };
        }
    }

    private ToolExecutionResult ExecuteGetProfileSummary(string toolId)
    {
        if (_profileService?.Profile == null)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "get_profile_summary", Success = false, Result = "褰撳墠 Profile 鏈嶅姟鏈氨缁? };
        }

        var p = _profileService.Profile;
        var now = DateTime.Now;
        var summary = new JsonObject
        {
            ["currentTime"] = now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["currentDayOfWeek"] = GetChineseWeekDay(now.DayOfWeek),
            ["profileName"] = p.Name,
            ["subjectCount"] = p.Subjects.Count,
            ["subjects"] = new JsonArray(p.Subjects.Values.Select(s => (JsonNode)s.Name).ToArray()),
            ["timeLayouts"] = new JsonArray(p.TimeLayouts.Values.Select(t => (JsonNode)t.Name).ToArray()),
            ["classPlans"] = new JsonArray(p.ClassPlans.Values.Select(c => (JsonNode)c.Name).ToArray()),
            ["isOverlayEnabled"] = p.IsOverlayClassPlanEnabled,
            ["activeOverlayId"] = p.OverlayClassPlanId?.ToString() ?? "鏃?
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
            return new ToolExecutionResult { ToolCallId = toolId, Name = "get_schedule_details", Success = false, Result = "Profile 鏈嶅姟鏈氨缁? };
        }

        var profile = _profileService.Profile;
        var planNameQuery = args["planName"]?.GetValue<string>()?.Trim();
        var targetDateQuery = args["targetDate"]?.GetValue<string>()?.Trim();

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
            if (ProfileCompat.TryGetTimeLayout(profile, ProfileCompat.GetTimeLayoutId(plan), out var tl) && tl != null)
            {
                layout = tl;
                planObj["timeLayoutName"] = tl.Name;
            }

            var classesArray = new JsonArray();
            var validLessons = layout?.Layouts.Where(l => l.TimeType == 0).ToList() ?? new List<TimeLayoutItem>();

            for (int i = 0; i < plan.Classes.Count; i++)
            {
                var c = plan.Classes[i];
                var subName = ProfileCompat.TryGetSubject(profile, ProfileCompat.GetSubjectId(c), out var s) && s != null ? s.Name : "鏈缃鐩?;
                var teacherName = ProfileCompat.TryGetSubject(profile, ProfileCompat.GetSubjectId(c), out var s2) && s2 != null ? s2.TeacherName : "";
                var timeSlot = i < validLessons.Count
                    ? $"{ProfileCompat.GetStart(validLessons[i]):hh\\:mm} - {ProfileCompat.GetEnd(validLessons[i]):hh\\:mm}"
                    : "";

                var itemObj = new JsonObject
                {
                    ["index"] = i + 1, // 1 琛ㄧず绗?1 鑺傝
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
            var targetPlanName = profile.ClassPlans.TryGetValue(ord.Value.ClassPlanId, out var cp) ? cp.Name : "鏈煡璇捐〃";
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
            return new ToolExecutionResult { ToolCallId = toolId, Name = "upsert_subjects", Success = false, Result = "Profile 鏈嶅姟鏈氨缁? };
        }

        var profile = _profileService.Profile;
        if (!args.TryGetPropertyValue("subjects", out var subsNode) || subsNode is not JsonArray subsArray)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "upsert_subjects", Success = false, Result = "缂哄皯 subjects 鍒楄〃鍙傛暟" };
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
                ProfileCompat.SetByKey(profile.Subjects, Guid.NewGuid().ToString(), newSub);
                addedCount++;
            }
        }

        _profileService.SaveProfile();
        return new ToolExecutionResult
        {
            ToolCallId = toolId,
            Name = "upsert_subjects",
            Success = true,
            Result = $"鎴愬姛娣诲姞 {addedCount} 涓鐩紝鏇存柊 {updatedCount} 涓鐩€?
        };
    }

    private ToolExecutionResult ExecuteCreateTimeLayout(string toolId, JsonObject args)
    {
        if (_profileService?.Profile == null)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "create_time_layout", Success = false, Result = "Profile 鏈嶅姟鏈氨缁? };
        }

        var profile = _profileService.Profile;
        var layoutName = args["layoutName"]?.GetValue<string>()?.Trim() ?? "鏃堕棿琛?;
        if (!args.TryGetPropertyValue("items", out var itemsNode) || itemsNode is not JsonArray itemsArray)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "create_time_layout", Success = false, Result = "缂哄皯 items 鑺傜偣鍒楄〃" };
        }

        var existingLayout = profile.TimeLayouts.FirstOrDefault(kv => string.Equals(kv.Value.Name, layoutName, StringComparison.OrdinalIgnoreCase));
        TimeLayout layout;
        if (existingLayout.Value != null)
        {
            layout = existingLayout.Value;
            layout.Layouts.Clear();
        }
        else
        {
            layout = new TimeLayout { Name = layoutName };
            ProfileCompat.SetByKey(profile.TimeLayouts, Guid.NewGuid().ToString(), layout);
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

            var timeItem = new TimeLayoutItem
            {
                TimeType = timeType,
                BreakName = breakName
            };
            ProfileCompat.SetTimeRange(timeItem, startTime, endTime);
            layout.Layouts.Add(timeItem);
        }

        _profileService.SaveProfile();
        return new ToolExecutionResult
        {
            ToolCallId = toolId,
            Name = "create_time_layout",
            Success = true,
            Result = $"鏃堕棿琛ㄣ€恵layoutName}銆戝凡鍒涘缓骞朵繚瀛橈紝鍖呭惈 {layout.Layouts.Count} 涓妭鐐广€?
        };
    }

    private ToolExecutionResult ExecuteSetClassPlan(string toolId, JsonObject args)
    {
        if (_profileService?.Profile == null)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "set_class_plan", Success = false, Result = "Profile 鏈嶅姟鏈氨缁? };
        }

        var profile = _profileService.Profile;
        var planName = args["planName"]?.GetValue<string>()?.Trim() ?? "鏂拌琛?;
        var timeLayoutName = args["timeLayoutName"]?.GetValue<string>()?.Trim() ?? "";

        if (!args.TryGetPropertyValue("classes", out var clsNode) || clsNode is not JsonArray clsArray)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "set_class_plan", Success = false, Result = "缂哄皯 classes 璇剧▼鍒楄〃" };
        }

        var timeLayoutId = "";
        if (!string.IsNullOrWhiteSpace(timeLayoutName))
        {
            var match = profile.TimeLayouts.FirstOrDefault(kv => string.Equals(kv.Value.Name, timeLayoutName, StringComparison.OrdinalIgnoreCase));
            if (match.Value != null) timeLayoutId = match.Key.ToString() ?? "";
        }
        if (string.IsNullOrEmpty(timeLayoutId) && profile.TimeLayouts.Count > 0)
        {
            timeLayoutId = ProfileCompat.FirstTimeLayoutId(profile);
        }

        var subMap = profile.Subjects.ToDictionary(kv => kv.Value.Name, kv => kv.Key.ToString() ?? "", StringComparer.OrdinalIgnoreCase);

        var existingPlan = profile.ClassPlans.FirstOrDefault(kv => string.Equals(kv.Value.Name, planName, StringComparison.OrdinalIgnoreCase));
        ClassPlan plan;
        if (existingPlan.Value != null)
        {
            plan = existingPlan.Value;
            plan.Classes.Clear();
            if (!string.IsNullOrEmpty(timeLayoutId)) ProfileCompat.SetTimeLayoutId(plan, timeLayoutId);
        }
        else
        {
            plan = new ClassPlan
            {
                Name = planName,
                IsEnabled = true
            };
            ProfileCompat.SetTimeLayoutId(plan, timeLayoutId);
            ProfileCompat.SetByKey(profile.ClassPlans, Guid.NewGuid().ToString(), plan);
        }

        int detectedWeekDay = ParseWeekDay(planName);
        if (detectedWeekDay >= 0)
        {
            plan.TimeRule.WeekDay = detectedWeekDay;
        }

        for (int i = 0; i < clsArray.Count; i++)
        {
            var subName = clsArray[i]?.GetValue<string>()?.Trim() ?? "";
            var subId = "";
            if (!string.IsNullOrWhiteSpace(subName))
            {
                if (!subMap.TryGetValue(subName, out subId))
                {
                    subId = Guid.NewGuid().ToString();
                    var newSub = new Subject { Name = subName, Initial = subName.Substring(0, 1) };
                    ProfileCompat.SetByKey(profile.Subjects, subId, newSub);
                    subMap[subName] = subId;
                }
            }

            var classInfo = new ClassInfo
            {
                Index = i,
                IsEnabled = true
            };
            ProfileCompat.SetSubjectId(classInfo, subId);
            plan.Classes.Add(classInfo);
        }

        _profileService.SaveProfile();
        return new ToolExecutionResult
        {
            ToolCallId = toolId,
            Name = "set_class_plan",
            Success = true,
            Result = $"璇捐〃銆恵planName}銆戝凡閰嶇疆瀹屾瘯锛屽叡瀹夋帓 {plan.Classes.Count} 鑺傝锛堝搴旓細{(detectedWeekDay >= 0 ? GetChineseWeekDay((DayOfWeek)detectedWeekDay) : "鏈寚瀹氬浐瀹氭槦鏈?)}锛夈€?
        };
    }

    private ToolExecutionResult ExecuteSetupTempClassPlan(string toolId, JsonObject args)
    {
        if (_profileService == null || _profileService.Profile == null)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "setup_temp_class_plan", Success = false, Result = "Profile 鏈嶅姟鏈氨缁? };
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
                Result = $"鏈兘鍖归厤鍒伴€傚悎鐨勫熀纭€璇捐〃銆恵planName}銆戙€傚綋鍓嶅凡鏈夎琛細{string.Join(", ", profile.ClassPlans.Values.Select(c => c.Name))}"
            };
        }

        var sourceKey = sourcePlanEntry.Key;
        var sourcePlan = sourcePlanEntry.Value;

        ClassPlan? tempPlan = null;
        var tempId = "";

        if (profile.OrderedSchedules.TryGetValue(targetDate.Date, out var existingOrdered)
            && ProfileCompat.TryGetClassPlan(profile, ProfileCompat.GetClassPlanId(existingOrdered), out var existingOverlay)
            && existingOverlay != null
            && existingOverlay.IsOverlay)
        {
            tempId = ProfileCompat.GetClassPlanId(existingOrdered);
            tempPlan = existingOverlay;
        }
        else
        {
            var createdId = ProfileCompat.CreateTempClassPlan(_profileService, sourceKey, targetDate.Date);
            if (!string.IsNullOrEmpty(createdId)
                && ProfileCompat.TryGetClassPlan(profile, createdId, out var cp)
                && cp != null)
            {
                tempId = createdId;
                tempPlan = cp;
            }
            else
            {
                try
                {
                    var serialized = JsonSerializer.Serialize(sourcePlan);
                    var newPlan = JsonSerializer.Deserialize<ClassPlan>(serialized) ?? new ClassPlan();
                    newPlan.IsOverlay = true;
                    newPlan.Name = sourcePlan.Name + "锛堜复鏃跺眰锛?;
                    ProfileCompat.SetOverlaySourceId(newPlan, sourceKey);
                    newPlan.OverlaySetupTime = targetDate.Date;
                    tempId = Guid.NewGuid().ToString();
                    ProfileCompat.SetByKey(profile.ClassPlans, tempId, newPlan);
                    var schedule = new OrderedSchedule();
                    ProfileCompat.SetClassPlanId(schedule, tempId);
                    profile.OrderedSchedules[targetDate.Date] = schedule;
                    tempPlan = newPlan;
                }
                catch (Exception ex)
                {
                    return new ToolExecutionResult { ToolCallId = toolId, Name = "setup_temp_class_plan", Success = false, Result = $"澶嶅埗鍒涘缓涓存椂璇捐〃寮傚父: {ex.Message}" };
                }
            }
        }

        if (tempPlan == null)
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "setup_temp_class_plan", Success = false, Result = "鍒涘缓鎴栬幏鍙栦复鏃惰琛ㄥ璞″け璐? };
        }

        string changeDescription = "";
        var subMap = profile.Subjects.ToDictionary(kv => kv.Value.Name, kv => kv.Key.ToString() ?? "", StringComparer.OrdinalIgnoreCase);

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
                targetSubGuid = Guid.NewGuid().ToString();
                var newSub = new Subject { Name = subjectName, Initial = subjectName.Substring(0, 1) };
                ProfileCompat.SetByKey(profile.Subjects, targetSubGuid, newSub);
                subMap[subjectName] = targetSubGuid;
            }

            var changedSlots = new List<string>();
            foreach (var ci in targetIndices)
            {
                int targetIdx = ci == -1 ? tempPlan.Classes.Count - 1 : (ci > 0 ? ci - 1 : 0);
                if (targetIdx >= 0 && targetIdx < tempPlan.Classes.Count)
                {
                    var oldSubName = ProfileCompat.TryGetSubject(profile, ProfileCompat.GetSubjectId(tempPlan.Classes[targetIdx]), out var oldSub) && oldSub != null ? oldSub.Name : "鏈畾";
                    ProfileCompat.SetSubjectId(tempPlan.Classes[targetIdx], targetSubGuid);
                    tempPlan.Classes[targetIdx].IsChangedClass = true;
                    changedSlots.Add($"绗?{targetIdx + 1} 鑺傦紙鍘熴€恵oldSubName}銆戯級");
                }
            }

            changeDescription = $"宸插皢 {string.Join(" 涓?", changedSlots)} 鍙樻洿涓恒€恵subjectName}銆?;
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
                    sGuid = Guid.NewGuid().ToString();
                    var newSub = new Subject { Name = sName, Initial = sName.Substring(0, 1) };
                    ProfileCompat.SetByKey(profile.Subjects, sGuid, newSub);
                    subMap[sName] = sGuid;
                }

                ProfileCompat.SetSubjectId(tempPlan.Classes[i], sGuid);
                tempPlan.Classes[i].IsChangedClass = true;
                changedCount++;
            }
            changeDescription = $"宸叉洿鏂?{changedCount} 鑺傝绋嬪畨鎺?;
        }
        else
        {
            changeDescription = "宸插悓姝ョ敓鎴愪复鏃跺眰";
        }

        profile.IsOverlayClassPlanEnabled = true;
        ProfileCompat.SetOverlayClassPlanId(profile, tempId);
        ProfileCompat.SetTempClassPlanId(profile, tempId);
        profile.TempClassPlanSetupTime = targetDate.Date;
        var orderedSchedule = new OrderedSchedule();
        ProfileCompat.SetClassPlanId(orderedSchedule, tempId);
        profile.OrderedSchedules[targetDate.Date] = orderedSchedule;

        _profileService.SaveProfile();

        return new ToolExecutionResult
        {
            ToolCallId = toolId,
            Name = "setup_temp_class_plan",
            Success = true,
            Result = $"涓存椂璋冭宸茬敓鏁堬紒\n" +
                     $"鈥?鐢熸晥鏃ユ湡锛歿targetDate:yyyy-MM-dd}锛坽GetChineseWeekDay(targetDate.DayOfWeek)}锛塡n" +
                     $"鈥?鍩虹璇捐〃锛氥€恵sourcePlan.Name}銆慭n" +
                     $"鈥?璋冩暣鍐呭锛歿changeDescription}\n" +
                     $"鈥?鐘舵€侊細宸插嵆鏃舵縺娲诲苟鍦?ClassIsland 涓敓鏁堟樉绀恒€?
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
                Result = "褰撳墠瀹夸富璁剧疆鏈嶅姟鏈氨缁垨鏈壘鍒?
            };
        }

        var resultObj = new JsonObject();
        var type = settingsObj.GetType();

        foreach (var propName in AppSettingPropertyNames)
        {
            var p = type.GetProperty(propName);
            if (p == null) continue;

            object? val;
            try { val = p.GetValue(settingsObj); }
            catch { continue; }

            switch (val)
            {
                case null:
                    break;
                case bool b:
                    resultObj[propName] = b;
                    break;
                case int i:
                    resultObj[propName] = i;
                    break;
                case double d:
                    resultObj[propName] = d;
                    break;
                case string s:
                    resultObj[propName] = s;
                    break;
                case Enum e:
                    resultObj[propName] = Convert.ToInt32(e);
                    break;
                case System.Collections.IEnumerable:
                    break;
                default:
                    resultObj[propName] = val.ToString();
                    break;
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
                Result = "褰撳墠瀹夸富璁剧疆鏈嶅姟鏈氨缁紝鏃犳硶閫氳繃鎺ュ彛淇敼璁剧疆"
            };
        }

        var type = settingsObj.GetType();
        var updatedList = new List<string>();

        bool TrySetProp(string propName, JsonNode? node)
        {
            if (node == null) return false;
            var p = type.GetProperty(propName);
            if (p == null || !p.CanWrite) return false;

            try
            {
                var raw = node.ToString().Trim().Trim('"');
                object converted;
                if (p.PropertyType == typeof(bool))
                    converted = bool.TryParse(raw, out var b)
                        ? b
                        : Convert.ToBoolean(double.TryParse(raw, out var bn) && bn != 0);
                else if (p.PropertyType == typeof(int))
                    converted = int.TryParse(raw, out var i) ? i : Convert.ToInt32(double.Parse(raw));
                else if (p.PropertyType == typeof(double))
                    converted = double.Parse(raw);
                else if (p.PropertyType == typeof(float))
                    converted = float.Parse(raw);
                else if (p.PropertyType == typeof(string))
                    converted = raw;
                else if (p.PropertyType.IsEnum)
                    converted = Enum.ToObject(p.PropertyType, int.Parse(raw));
                else
                    converted = Convert.ChangeType(raw, p.PropertyType);

                p.SetValue(settingsObj, converted);
                updatedList.Add($"{propName} = {converted}");
                return true;
            }
            catch
            {
                return false;
            }
        }

        // 1) 鎸夌櫧鍚嶅崟鍖归厤鍏峰悕鍙傛暟锛堟敮鎸?camelCase / PascalCase 涓ょ鍐欐硶锛?        foreach (var propName in AppSettingPropertyNames)
        {
            var camel = char.ToLowerInvariant(propName[0]) + propName[1..];
            if (args.TryGetPropertyValue(camel, out var node) || args.TryGetPropertyValue(propName, out node))
            {
                TrySetProp(propName, node);
            }
        }

        // 2) 甯歌鍒悕
        if (args.TryGetPropertyValue("timeOffset", out var toAlias) || args.TryGetPropertyValue("offset", out toAlias))
        {
            TrySetProp("TimeOffsetSeconds", toAlias);
        }

        // 3) 涓婚鍒囨崲闇€瑕侀€氱煡涓婚鏈嶅姟鍒锋柊锛屼粎鏀瑰睘鎬т笉浼氱珛鍗抽噸缁?        if (updatedList.Any(s => s.StartsWith("Theme = ")))
        {
            try
            {
                var themeServiceType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                    .FirstOrDefault(x => x.FullName == "ClassIsland.Core.Abstractions.Services.IThemeService");
                if (themeServiceType != null)
                {
                    var ts = IAppHost.Host?.Services.GetService(themeServiceType);
                    var setMethod = themeServiceType.GetMethod("SetTheme");
                    setMethod?.Invoke(ts, new object?[] { type.GetProperty("Theme")?.GetValue(settingsObj), null });
                }
            }
            catch { }
        }

        // 4) 浠绘剰閿€煎鍏滃簳锛堜粛鍙楃櫧鍚嶅崟绾︽潫锛岄槻姝㈣鏀归泦鍚堢被璁剧疆锛?        if (args.TryGetPropertyValue("customSettings", out var customNode) && customNode is JsonObject customObj)
        {
            foreach (var kv in customObj)
            {
                var matched = AppSettingPropertyNames.FirstOrDefault(
                    n => n.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                {
                    TrySetProp(matched, kv.Value);
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
                Result = "鏈彁渚涙垨鏈尮閰嶅埌闇€瑕佷慨鏀圭殑鏈夋晥璁剧疆椤?
            };
        }

        return new ToolExecutionResult
        {
            ToolCallId = toolId,
            Name = "update_app_settings",
            Success = true,
            Result = $"璁剧疆淇敼鎴愬姛涓斿凡鍗虫椂鐢熸晥锛堟棤闇€鐢ㄦ埛鎵嬪姩鐐瑰嚮锛夛細\n鈥?" + string.Join("\n鈥?", updatedList)
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

    private ToolExecutionResult ExecuteGetPluginSettings(string toolId)
    {
        if (_pluginSettings == null)
        {
            return new ToolExecutionResult
            {
                ToolCallId = toolId,
                Name = "get_plugin_settings",
                Success = false,
                Result = "鎻掍欢璁剧疆鏈氨缁?
            };
        }

        _pluginSettings.EnsureProvidersInitialized();

        var providers = new JsonArray();
        foreach (var p in _pluginSettings.Providers)
        {
            providers.Add(new JsonObject
            {
                ["id"] = p.Id,
                ["name"] = p.Name,
                ["baseUrl"] = p.BaseUrl,
                ["hasApiKey"] = !string.IsNullOrWhiteSpace(p.ApiKey),
                ["models"] = new JsonArray(p.Models.Select(m => (JsonNode)new JsonObject
                {
                    ["name"] = m.Name,
                    ["isEnabled"] = m.IsEnabled
                }).ToArray())
            });
        }

        var result = new JsonObject
        {
            ["providers"] = providers,
            ["activeProviderId"] = _pluginSettings.ActiveProviderId,
            ["activeModelName"] = _pluginSettings.ActiveModelName,
            ["isMemoryEnabled"] = _pluginSettings.IsMemoryEnabled,
            ["isCommandExecutionEnabled"] = _pluginSettings.IsCommandExecutionEnabled,
            ["customPrompt"] = _pluginSettings.CustomPrompt,
            ["customMemory"] = _pluginSettings.CustomMemory,
            ["hasPassword"] = !string.IsNullOrEmpty(_pluginSettings.PasswordHash),
            ["isTotpEnabled"] = !string.IsNullOrWhiteSpace(_pluginSettings.TotpSecret),
            ["isTotpPending"] = string.IsNullOrWhiteSpace(_pluginSettings.TotpSecret)
                                 && !string.IsNullOrWhiteSpace(_pluginSettings.PendingTotpSecret)
        };

        return new ToolExecutionResult
        {
            ToolCallId = toolId,
            Name = "get_plugin_settings",
            Success = true,
            Result = result.ToJsonString()
        };
    }

    private ToolExecutionResult ExecuteUpdatePluginSettings(string toolId, JsonObject args)
    {
        if (_pluginSettings == null)
        {
            return new ToolExecutionResult
            {
                ToolCallId = toolId,
                Name = "update_plugin_settings",
                Success = false,
                Result = "鎻掍欢璁剧疆鏈氨缁?
            };
        }

        _pluginSettings.EnsureProvidersInitialized();
        var changes = new List<string>();
        var action = args["action"]?.ToString().Trim().ToLowerInvariant() ?? "";

        // ---------- 渚涘簲鍟嗗鍒犳敼 ----------
        switch (action)
        {
            case "add_provider":
            {
                var name = args["providerName"]?.ToString().Trim() ?? "";
                var baseUrl = args["baseUrl"]?.ToString().Trim() ?? "";
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(baseUrl))
                {
                    return new ToolExecutionResult
                    {
                        ToolCallId = toolId, Name = "update_plugin_settings", Success = false,
                        Result = "add_provider 闇€瑕?providerName 涓?baseUrl"
                    };
                }

                if (_pluginSettings.Providers.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    return new ToolExecutionResult
                    {
                        ToolCallId = toolId, Name = "update_plugin_settings", Success = false,
                        Result = $"渚涘簲鍟嗐€寋name}銆嶅凡瀛樺湪锛岃鏀圭敤 set_provider 淇敼"
                    };
                }

                var provider = new AiProviderConfig
                {
                    Name = name,
                    BaseUrl = baseUrl,
                    ApiKey = args["apiKey"]?.ToString().Trim() ?? "",
                    Models = new List<AiModelItem>()
                };
                if (args["models"] is JsonArray modelArr)
                {
                    foreach (var m in modelArr)
                    {
                        var mn = m?.ToString().Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(mn)) provider.Models.Add(new AiModelItem(mn, true));
                    }
                }

                _pluginSettings.Providers.Add(provider);
                if (string.IsNullOrWhiteSpace(_pluginSettings.ActiveProviderId))
                {
                    _pluginSettings.ActiveProviderId = provider.Id;
                }
                changes.Add($"宸叉柊澧炰緵搴斿晢銆寋name}銆嶏紙{baseUrl}锛夛紝妯″瀷 {provider.Models.Count} 涓?);
                break;
            }

            case "set_provider":
            {
                var target = ResolveProvider(args["providerName"]?.ToString().Trim() ?? "");
                if (target == null)
                {
                    return new ToolExecutionResult
                    {
                        ToolCallId = toolId, Name = "update_plugin_settings", Success = false,
                        Result = "鏈壘鍒版寚瀹氫緵搴斿晢锛屽彲鍏堢敤 get_plugin_settings 鏌ョ湅鐜版湁渚涘簲鍟?
                    };
                }

                var oldName = target.Name;
                if (args["newName"] != null) target.Name = args["newName"]!.ToString().Trim();
                if (args["baseUrl"] != null) target.BaseUrl = args["baseUrl"]!.ToString().Trim();
                if (args["apiKey"] != null) target.ApiKey = args["apiKey"]!.ToString().Trim();
                changes.Add($"宸叉洿鏂颁緵搴斿晢銆寋oldName}銆?);
                break;
            }

            case "delete_provider":
            {
                var target = ResolveProvider(args["providerName"]?.ToString().Trim() ?? "");
                if (target == null)
                {
                    return new ToolExecutionResult
                    {
                        ToolCallId = toolId, Name = "update_plugin_settings", Success = false,
                        Result = "鏈壘鍒版寚瀹氫緵搴斿晢"
                    };
                }
                if (_pluginSettings.Providers.Count <= 1)
                {
                    return new ToolExecutionResult
                    {
                        ToolCallId = toolId, Name = "update_plugin_settings", Success = false,
                        Result = "鑷冲皯闇€淇濈暀涓€涓緵搴斿晢锛屾棤娉曞垹闄?
                    };
                }

                _pluginSettings.Providers.Remove(target);
                if (_pluginSettings.ActiveProviderId == target.Id)
                {
                    _pluginSettings.ActiveProviderId = _pluginSettings.Providers.FirstOrDefault()?.Id ?? "";
                }
                changes.Add($"宸插垹闄や緵搴斿晢銆寋target.Name}銆?);
                break;
            }

            case "add_model":
            case "remove_model":
            case "enable_model":
            case "disable_model":
            {
                var target = ResolveProvider(args["providerName"]?.ToString().Trim() ?? "");
                if (target == null)
                {
                    return new ToolExecutionResult
                    {
                        ToolCallId = toolId, Name = "update_plugin_settings", Success = false,
                        Result = "鏈壘鍒版寚瀹氫緵搴斿晢"
                    };
                }

                var modelName = args["modelName"]?.ToString().Trim() ?? "";
                if (string.IsNullOrWhiteSpace(modelName))
                {
                    return new ToolExecutionResult
                    {
                        ToolCallId = toolId, Name = "update_plugin_settings", Success = false,
                        Result = "缂哄皯 modelName"
                    };
                }

                var existing = target.Models.FirstOrDefault(m => m.Name == modelName);
                if (action == "add_model")
                {
                    if (existing == null)
                    {
                        target.Models.Add(new AiModelItem(modelName, true));
                        changes.Add($"宸蹭负銆寋target.Name}銆嶆坊鍔犳ā鍨?{modelName}锛堝凡鍚敤锛?);
                    }
                    else
                    {
                        existing.IsEnabled = true;
                        changes.Add($"妯″瀷 {modelName} 宸插瓨鍦紝宸茬疆涓哄惎鐢?);
                    }
                }
                else if (action == "remove_model")
                {
                    if (existing == null)
                    {
                        return new ToolExecutionResult
                        {
                            ToolCallId = toolId, Name = "update_plugin_settings", Success = false,
                            Result = $"渚涘簲鍟嗐€寋target.Name}銆嶄笅娌℃湁妯″瀷 {modelName}"
                        };
                    }
                    target.Models.Remove(existing);
                    changes.Add($"宸蹭粠銆寋target.Name}銆嶇Щ闄ゆā鍨?{modelName}");
                }
                else
                {
                    if (existing == null)
                    {
                        return new ToolExecutionResult
                        {
                            ToolCallId = toolId, Name = "update_plugin_settings", Success = false,
                            Result = $"渚涘簲鍟嗐€寋target.Name}銆嶄笅娌℃湁妯″瀷 {modelName}"
                        };
                    }
                    existing.IsEnabled = action == "enable_model";
                    changes.Add($"妯″瀷 {modelName} 宸瞷(existing.IsEnabled ? "鍚敤" : "鍋滅敤")}");
                }
                break;
            }

            case "set_active_model":
            {
                var target = ResolveProvider(args["providerName"]?.ToString().Trim() ?? "");
                var modelName = args["modelName"]?.ToString().Trim() ?? "";
                if (target == null || string.IsNullOrWhiteSpace(modelName))
                {
                    return new ToolExecutionResult
                    {
                        ToolCallId = toolId, Name = "update_plugin_settings", Success = false,
                        Result = "set_active_model 闇€瑕?providerName 涓?modelName"
                    };
                }

                var model = target.Models.FirstOrDefault(m => m.Name == modelName);
                if (model == null)
                {
                    target.Models.Add(new AiModelItem(modelName, true));
                }
                else
                {
                    model.IsEnabled = true;
                }

                _pluginSettings.SelectActiveModel(target.Id, modelName);
                changes.Add($"褰撳墠妯″瀷宸插垏鎹负 [{target.Name}] {modelName}");
                break;
            }

            case "":
                break; // 绾瓧娈垫洿鏂帮紝鏃犻渶 action

            default:
                return new ToolExecutionResult
                {
                    ToolCallId = toolId, Name = "update_plugin_settings", Success = false,
                    Result = $"鏈煡鎿嶄綔绫诲瀷: {action}"
                };
        }

        // ---------- 绾瓧娈垫洿鏂?----------
        if (args["isMemoryEnabled"] is JsonNode memNode)
        {
            _pluginSettings.IsMemoryEnabled = memNode.GetValue<bool>();
            changes.Add($"闀挎湡璁板繂宸瞷(_pluginSettings.IsMemoryEnabled ? "鍚敤" : "鍋滅敤")}");
        }
        if (args["isCommandExecutionEnabled"] is JsonNode cmdNode)
        {
            var enabled = cmdNode.GetValue<bool>();
            _pluginSettings.IsCommandExecutionEnabled = enabled;
            changes.Add($"鍛戒护琛屾墽琛屾潈闄愬凡{(enabled ? "寮€鍚? : "鍏抽棴")}");
        }
        if (args["customPrompt"] is JsonNode promptNode)
        {
            _pluginSettings.CustomPrompt = promptNode.ToString();
            changes.Add("鑷畾涔夌郴缁熸彁绀鸿瘝宸叉洿鏂?);
        }
        if (args["maxToolIterations"] is JsonNode iterNode)
        {
            var raw = iterNode.ToString().Trim().Trim('"');
            if (int.TryParse(raw, out var parsedIter))
            {
                _pluginSettings.MaxToolIterations = Math.Clamp(parsedIter, 1, 50);
                changes.Add($"宸ュ叿璋冪敤杞暟涓婇檺宸茶涓?{_pluginSettings.MaxToolIterations}");
            }
            else
            {
                changes.Add("宸ュ叿璋冪敤杞暟涓婇檺鏈慨鏀癸紙闇€瑕?1鈥?0 鐨勬暣鏁帮級");
            }
        }
        if (args["customMemory"] is JsonNode memoryNode)
        {
            _pluginSettings.CustomMemory = memoryNode.ToString();
            changes.Add("闀挎湡璁板繂鍐呭宸叉洿鏂?);
        }

        if (changes.Count == 0)
        {
            return new ToolExecutionResult
            {
                ToolCallId = toolId, Name = "update_plugin_settings", Success = false,
                Result = "鏈彁渚涗换浣曟湁鏁堢殑鎻掍欢璁剧疆淇敼椤?
            };
        }

        try { _onSavePluginSettings?.Invoke(); }
        catch { }

        return new ToolExecutionResult
        {
            ToolCallId = toolId,
            Name = "update_plugin_settings",
            Success = true,
            Result = "鎻掍欢璁剧疆宸叉洿鏂板苟淇濆瓨锛歕n鈥?" + string.Join("\n鈥?", changes)
        };
    }

    private AiProviderConfig? ResolveProvider(string nameOrId)
    {
        if (_pluginSettings == null || string.IsNullOrWhiteSpace(nameOrId)) return null;

        return _pluginSettings.Providers.FirstOrDefault(p => p.Id == nameOrId)
               ?? _pluginSettings.Providers.FirstOrDefault(p => p.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase))
               ?? _pluginSettings.Providers.FirstOrDefault(p => p.Name.Contains(nameOrId, StringComparison.OrdinalIgnoreCase));
    }

    private ToolExecutionResult ExecuteNavigateAppPage(string toolId, JsonObject args)    {
        var uriStr = args["pageUri"]?.GetValue<string>()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(uriStr))
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "navigate_app_page", Success = false, Result = "URI 涓嶈兘涓虹┖" };
        }

        if (_uriService != null)
        {
            _uriService.NavigateWrapped(new Uri(uriStr));
            return new ToolExecutionResult { ToolCallId = toolId, Name = "navigate_app_page", Success = true, Result = $"宸叉垚鍔熻烦杞埌椤甸潰: {uriStr}" };
        }

        return new ToolExecutionResult { ToolCallId = toolId, Name = "navigate_app_page", Success = false, Result = "瀵艰埅鏈嶅姟涓嶅彲鐢? };
    }

    private ToolExecutionResult ExecuteGetCurrentTime(string toolId)
    {
        var now = DateTime.Now;
        var offsetSeconds = 0.0;

        try
        {
            var settingsObj = GetAppHostSettingsObject();
            if (settingsObj != null)
            {
                var prop = settingsObj.GetType().GetProperty("TimeOffsetSeconds");
                if (prop != null && prop.GetValue(settingsObj) is double d)
                {
                    offsetSeconds = d;
                }
            }
        }
        catch { }

        var exactNow = now.AddSeconds(offsetSeconds);
        var todayChinese = GetChineseWeekDay(exactNow.DayOfWeek);
        var tomorrow = exactNow.AddDays(1);
        var tomorrowChinese = GetChineseWeekDay(tomorrow.DayOfWeek);

        var result = new JsonObject
        {
            ["currentTime"] = exactNow.ToString("yyyy-MM-dd HH:mm:ss"),
            ["currentDate"] = exactNow.ToString("yyyy-MM-dd"),
            ["currentDayOfWeek"] = todayChinese,
            ["dayOfWeekNumber"] = (int)exactNow.DayOfWeek,
            ["tomorrowDate"] = tomorrow.ToString("yyyy-MM-dd"),
            ["tomorrowDayOfWeek"] = tomorrowChinese,
            ["isWeekend"] = exactNow.DayOfWeek == DayOfWeek.Saturday || exactNow.DayOfWeek == DayOfWeek.Sunday,
            ["timeOffsetSeconds"] = offsetSeconds
        };

        return new ToolExecutionResult
        {
            ToolCallId = toolId,
            Name = "get_current_time",
            Success = true,
            Result = result.ToJsonString()
        };
    }

    private ToolExecutionResult ExecuteUpdateMemory(string toolId, JsonObject args)
    {
        if (_pluginSettings == null)
        {
            return new ToolExecutionResult
            {
                ToolCallId = toolId,
                Name = "update_memory",
                Success = false,
                Result = "璁板繂绯荤粺瀛樺偍閰嶇疆鏈氨缁?
            };
        }

        if (!_pluginSettings.IsMemoryEnabled)
        {
            return new ToolExecutionResult
            {
                ToolCallId = toolId,
                Name = "update_memory",
                Success = false,
                Result = "鐢ㄦ埛宸插湪璁剧疆涓叧闂簡 AI 璁板繂绯荤粺锛屾棤娉曞啓鍏ユ垨鏇存柊鏂拌蹇嗐€?
            };
        }

        var action = args["action"]?.GetValue<string>()?.ToLowerInvariant() ?? "add";
        var content = args["content"]?.GetValue<string>()?.Trim() ?? "";

        switch (action)
        {
            case "add":
                if (string.IsNullOrWhiteSpace(content))
                {
                    return new ToolExecutionResult { ToolCallId = toolId, Name = "update_memory", Success = false, Result = "璁板繂鍐呭涓嶈兘涓虹┖" };
                }
                var currentMemory = _pluginSettings.CustomMemory?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(currentMemory))
                {
                    _pluginSettings.CustomMemory = $"- {content}";
                }
                else
                {
                    _pluginSettings.CustomMemory = $"{currentMemory}\n- {content}";
                }
                _onSavePluginSettings?.Invoke();
                SettingsManager.Save(_pluginSettings);
                SettingsManager.NotifySettingsChanged(_pluginSettings);
                return new ToolExecutionResult
                {
                    ToolCallId = toolId,
                    Name = "update_memory",
                    Success = true,
                    Result = $"宸叉垚鍔熸坊鍔犺蹇嗭細鈥渰content}鈥濄€傚綋鍓嶈蹇嗗凡鏇存柊骞跺凡鎸佷箙鍖栦繚瀛樸€?
                };

            case "set":
                _pluginSettings.CustomMemory = content;
                _onSavePluginSettings?.Invoke();
                SettingsManager.Save(_pluginSettings);
                SettingsManager.NotifySettingsChanged(_pluginSettings);
                return new ToolExecutionResult
                {
                    ToolCallId = toolId,
                    Name = "update_memory",
                    Success = true,
                    Result = "宸叉垚鍔熻鐩栧苟鏇存柊鍏ㄩ儴璁板繂搴撳唴瀹广€?
                };

            case "clear":
                _pluginSettings.CustomMemory = "";
                _onSavePluginSettings?.Invoke();
                SettingsManager.Save(_pluginSettings);
                SettingsManager.NotifySettingsChanged(_pluginSettings);
                return new ToolExecutionResult
                {
                    ToolCallId = toolId,
                    Name = "update_memory",
                    Success = true,
                    Result = "宸叉竻绌烘墍鏈夐暱鏈熻蹇嗐€?
                };

            default:
                return new ToolExecutionResult
                {
                    ToolCallId = toolId,
                    Name = "update_memory",
                    Success = false,
                    Result = $"涓嶆敮鎸佺殑鎿嶄綔绫诲瀷: {action}锛屾敮鎸佺殑鎿嶄綔涓?add, set, clear銆?
                };
        }
    }

    private ToolExecutionResult ExecuteCommand(string toolId, JsonObject args)
    {
        if (_pluginSettings?.IsCommandExecutionEnabled != true)
        {
            return new ToolExecutionResult
            {
                ToolCallId = toolId,
                Name = "execute_command",
                Success = false,
                Result = "鐢ㄦ埛鏈湪銆愯缃?-> IslandAgent璁剧疆 -> 楂樼骇鍋忓ソ銆戜腑寮€鍚€滃厑璁告墽琛屽懡浠よ鍛戒护鈥濇潈闄愶紝鍛戒护宸茶绯荤粺鎷掔粷鎵ц銆?
            };
        }

        var cmd = args["command"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(cmd))
        {
            return new ToolExecutionResult { ToolCallId = toolId, Name = "execute_command", Success = false, Result = "鍛戒护涓嶈兘涓虹┖" };
        }

        var timeoutSec = 15;
        if (args.TryGetPropertyValue("timeoutSeconds", out var toNode) && toNode != null)
        {
            if (int.TryParse(toNode.ToString(), out var parsedSec) && parsedSec > 0)
            {
                timeoutSec = Math.Min(parsedSec, 60);
            }
        }

        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {cmd}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.Default,
                StandardErrorEncoding = System.Text.Encoding.Default
            };

            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeoutSec * 1000))
            {
                try { process.Kill(); } catch { }
                return new ToolExecutionResult
                {
                    ToolCallId = toolId,
                    Name = "execute_command",
                    Success = false,
                    Result = $"鍛戒护鎵ц瓒呮椂锛堣秴杩?{timeoutSec} 绉掞級锛岃繘绋嬪凡缁堟銆?
                };
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            var exitCode = process.ExitCode;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"閫€鍑轰唬鐮? {exitCode}");
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                sb.AppendLine("銆愭爣鍑嗚緭鍑恒€?");
                sb.AppendLine(stdout.Trim());
            }
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                sb.AppendLine("銆愰敊璇緭鍑恒€?");
                sb.AppendLine(stderr.Trim());
            }

            return new ToolExecutionResult
            {
                ToolCallId = toolId,
                Name = "execute_command",
                Success = exitCode == 0,
                Result = sb.ToString().Trim()
            };
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult
            {
                ToolCallId = toolId,
                Name = "execute_command",
                Success = false,
                Result = $"鎵ц寮傚父: {ex.Message}"
            };
        }
    }

    private static DateTime ResolveTargetDate(string? dateQuery, string? planName)
    {
        var today = DateTime.Today;
        if (!string.IsNullOrWhiteSpace(dateQuery))
        {
            var q = dateQuery.Trim().ToLowerInvariant();
            if (q.Contains("鏄?) || q.Contains("tomorrow"))
            {
                return today.AddDays(1);
            }
            if (q.Contains("鍚庡ぉ"))
            {
                return today.AddDays(2);
            }
            if (q.Contains("浠?) || q.Contains("today"))
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

    /// <summary>
    /// 按名称/星期查找基础课表（排除临时层）。返回键值对，键在两代宿主下类型不同，
    /// 调用方一律通过 <c>Key.ToString()</c> 取字符串形式。
    /// </summary>
    private static KeyValuePair<string, ClassPlan> FindSourcePlan(Profile profile, string planName, DateTime targetDate)
    {
        if (!string.IsNullOrWhiteSpace(planName))
        {
            var exactMatch = profile.ClassPlans.FirstOrDefault(kv => !kv.Value.IsOverlay && string.Equals(kv.Value.Name, planName, StringComparison.OrdinalIgnoreCase));
            if (exactMatch.Value != null) return ToStringKeyed(exactMatch);

            var normPlan = NormalizeWeekDayName(planName);
            var fuzzyMatch = profile.ClassPlans.FirstOrDefault(kv => !kv.Value.IsOverlay && NormalizeWeekDayName(kv.Value.Name).Contains(normPlan));
            if (fuzzyMatch.Value != null) return ToStringKeyed(fuzzyMatch);
        }

        int targetDayInt = (int)targetDate.DayOfWeek;
        var ruleMatch = profile.ClassPlans.FirstOrDefault(kv => !kv.Value.IsOverlay && kv.Value.TimeRule.WeekDay == targetDayInt);
        if (ruleMatch.Value != null) return ToStringKeyed(ruleMatch);

        var dayChar = GetChineseWeekDayChar(targetDate.DayOfWeek);
        var nameMatch = profile.ClassPlans.FirstOrDefault(kv => !kv.Value.IsOverlay && kv.Value.Name.Contains(dayChar));
        if (nameMatch.Value != null) return ToStringKeyed(nameMatch);

        return ToStringKeyed(profile.ClassPlans.FirstOrDefault(kv => !kv.Value.IsOverlay));
    }

    /// <summary>
    /// 把课表字典项统一成字符串键，屏蔽 1.x(string) 与 2.x(Guid) 的键类型差异。
    /// </summary>
    private static KeyValuePair<string, ClassPlan> ToStringKeyed<TKey>(KeyValuePair<TKey, ClassPlan> pair)
        => new(Convert.ToString(pair.Key) ?? string.Empty, pair.Value);

    public static int ParseWeekDay(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return -1;
        var s = name.Replace("绀兼嫓", "鍛?).Replace("鏄熸湡", "鍛?);
        if (s.Contains("鍛ㄤ竴") || s.Contains("鏄熸湡涓€") || s.Contains("涓€")) return (int)DayOfWeek.Monday;
        if (s.Contains("鍛ㄤ簩") || s.Contains("鏄熸湡浜?) || s.Contains("浜?)) return (int)DayOfWeek.Tuesday;
        if (s.Contains("鍛ㄤ笁") || s.Contains("鏄熸湡涓?) || s.Contains("涓?)) return (int)DayOfWeek.Wednesday;
        if (s.Contains("鍛ㄥ洓") || s.Contains("鏄熸湡鍥?) || s.Contains("鍥?)) return (int)DayOfWeek.Thursday;
        if (s.Contains("鍛ㄤ簲") || s.Contains("鏄熸湡浜?) || s.Contains("浜?)) return (int)DayOfWeek.Friday;
        if (s.Contains("鍛ㄥ叚") || s.Contains("鏄熸湡鍏?) || s.Contains("鍏?)) return (int)DayOfWeek.Saturday;
        if (s.Contains("鍛ㄦ棩") || s.Contains("鍛ㄥぉ") || s.Contains("鏄熸湡鏃?) || s.Contains("鏄熸湡澶?) || s.Contains("鏃?) || s.Contains("澶?)) return (int)DayOfWeek.Sunday;
        return -1;
    }

    public static string NormalizeWeekDayName(string name)
    {
        return name.Replace("鏄熸湡", "鍛?).Replace("绀兼嫓", "鍛?).Trim();
    }

    public static string GetChineseWeekDay(DayOfWeek dow)
    {
        return dow switch
        {
            DayOfWeek.Monday => "鍛ㄤ竴",
            DayOfWeek.Tuesday => "鍛ㄤ簩",
            DayOfWeek.Wednesday => "鍛ㄤ笁",
            DayOfWeek.Thursday => "鍛ㄥ洓",
            DayOfWeek.Friday => "鍛ㄤ簲",
            DayOfWeek.Saturday => "鍛ㄥ叚",
            DayOfWeek.Sunday => "鍛ㄦ棩",
            _ => "鏈煡"
        };
    }

    public static string GetChineseWeekDayChar(DayOfWeek dow)
    {
        return dow switch
        {
            DayOfWeek.Monday => "涓€",
            DayOfWeek.Tuesday => "浜?,
            DayOfWeek.Wednesday => "涓?,
            DayOfWeek.Thursday => "鍥?,
            DayOfWeek.Friday => "浜?,
            DayOfWeek.Saturday => "鍏?,
            DayOfWeek.Sunday => "鏃?,
            _ => ""
        };
    }
}
