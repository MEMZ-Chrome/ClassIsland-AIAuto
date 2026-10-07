using System.Text.Json.Nodes;

namespace ClassIsland.Automation.Core.Services;

public static class AiToolDefinitions
{
    public static JsonArray GetAvailableTools()
    {
        return new JsonArray
        {
            // 1. 获取当前档案概述
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "get_profile_summary",
                    ["description"] = "获取当前 ClassIsland 档案中的科目列表、时间表和课表概况",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject(),
                        ["required"] = new JsonArray()
                    }
                }
            },

            // 2. 添加或更新科目
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "upsert_subjects",
                    ["description"] = "批量添加或更新科目信息（如科目全称、简称、任课老师姓名、是否为室外课等）",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["subjects"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["description"] = "科目列表",
                                ["items"] = new JsonObject
                                {
                                    ["type"] = "object",
                                    ["properties"] = new JsonObject
                                    {
                                        ["name"] = new JsonObject { ["type"] = "string", ["description"] = "科目名称，如 语文、数学、体育" },
                                        ["initial"] = new JsonObject { ["type"] = "string", ["description"] = "科目简称或单字简称，如 文、数、体" },
                                        ["teacherName"] = new JsonObject { ["type"] = "string", ["description"] = "任课老师姓名，可留空" },
                                        ["isOutDoor"] = new JsonObject { ["type"] = "boolean", ["description"] = "是否为室外课，如体育课设为 true" }
                                    },
                                    ["required"] = new JsonArray { "name" }
                                }
                            }
                        },
                        ["required"] = new JsonArray { "subjects" }
                    }
                }
            },

            // 3. 创建或更新时间表
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "create_time_layout",
                    ["description"] = "创建或替换作息时间表（配置各节课及课间起止时间）",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["layoutName"] = new JsonObject { ["type"] = "string", ["description"] = "时间表名称，例如：夏季作息时间表、标准时间表" },
                            ["items"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["description"] = "按时间顺序排列的节次与课间",
                                ["items"] = new JsonObject
                                {
                                    ["type"] = "object",
                                    ["properties"] = new JsonObject
                                    {
                                        ["startTime"] = new JsonObject { ["type"] = "string", ["description"] = "开始时间 HH:mm:ss 或 HH:mm" },
                                        ["endTime"] = new JsonObject { ["type"] = "string", ["description"] = "结束时间 HH:mm:ss 或 HH:mm" },
                                        ["timeType"] = new JsonObject { ["type"] = "integer", ["description"] = "0: 上课, 1: 课间休息, 2: 分割线, 3: 行动" },
                                        ["breakName"] = new JsonObject { ["type"] = "string", ["description"] = "课间名称或广播操/大课间名称，可留空" }
                                    },
                                    ["required"] = new JsonArray { "startTime", "endTime", "timeType" }
                                }
                            }
                        },
                        ["required"] = new JsonArray { "layoutName", "items" }
                    }
                }
            },

            // 4. 配置每日课表
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "set_class_plan",
                    ["description"] = "创建或更新某天（如周一到周日）的课表课程安排",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["planName"] = new JsonObject { ["type"] = "string", ["description"] = "课表名称，如 星期一、星期二、周三" },
                            ["timeLayoutName"] = new JsonObject { ["type"] = "string", ["description"] = "绑定的时间表名称，留空则默认绑定第一套时间表" },
                            ["classes"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["description"] = "课程科目名称列表，严格按上课节次顺序排列",
                                ["items"] = new JsonObject { ["type"] = "string" }
                            }
                        },
                        ["required"] = new JsonArray { "planName", "classes" }
                    }
                }
            },

            // 5. 设置临时调课/临时课表
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "setup_temp_class_plan",
                    ["description"] = "设置或调整当天的临时课表（如某节课调课、自习课替换）",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["sourcePlanName"] = new JsonObject { ["type"] = "string", ["description"] = "基础课表名称（如 星期二）" },
                            ["modifiedClasses"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["description"] = "修改后的完整或局部节次课程列表",
                                ["items"] = new JsonObject { ["type"] = "string" }
                            }
                        },
                        ["required"] = new JsonArray { "sourcePlanName", "modifiedClasses" }
                    }
                }
            },

            // 6. 导航应用页面或修改设置
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "navigate_app_page",
                    ["description"] = "通过 classisland:// 内部协议打开指定设置或功能页面",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["pageUri"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "目标 URI，例如 classisland://app/settings/general, classisland://app/settings/classisland.plugins, classisland://app/settings/update"
                            }
                        },
                        ["required"] = new JsonArray { "pageUri" }
                    }
                }
            }
        };
    }
}
