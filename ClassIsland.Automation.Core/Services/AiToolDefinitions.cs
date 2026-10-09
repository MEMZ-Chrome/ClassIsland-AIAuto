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
                    ["description"] = "获取当前 ClassIsland 档案中的科目列表、时间表和课表概况（仅名称概要）",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject(),
                        ["required"] = new JsonArray()
                    }
                }
            },

            // 2. 获取指定课表或时间表的详细信息
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "get_schedule_details",
                    ["description"] = "获取课表与作息时间表的具体详细内容（包含各节次的序号、时间段、科目全称、任课老师及临时层预定状态，当用户询问具体某天课程安排或调课前需核实课表时使用）",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["planName"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "课表名称或对应星期（例如：周三、周六、星期五），留空则返回所有课表或当天生效课表的详细信息"
                            },
                            ["targetDate"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "可选，指定查询日期（例如：today、tomorrow、今天、明天、2026-10-10等）"
                            }
                        },
                        ["required"] = new JsonArray()
                    }
                }
            },

            // 3. 添加或更新科目
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

            // 4. 创建或更新时间表
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

            // 5. 配置每日课表
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "set_class_plan",
                    ["description"] = "创建或更新某天（如周一到周日）的固定课表课程安排",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["planName"] = new JsonObject { ["type"] = "string", ["description"] = "课表名称，如 星期一、星期二、周三、周六" },
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

            // 6. 设置临时调课/临时课表
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "setup_temp_class_plan",
                    ["description"] = "设置或调整指定日期的临时调课/临时课表（支持指定节次单科调课或整天课程替换，调课后立即激活为当天生效的临时课表）",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["sourcePlanName"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "基础课表名称或星期，例如：周三、周六、星期五"
                            },
                            ["dayOfWeek"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "星期几（与 sourcePlanName 等效，例如：周三、周六）"
                            },
                            ["targetDate"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "调课生效的目标日期（例如：today、tomorrow、今天、明天、周六、2026-10-10等，留空默认今天）"
                            },
                            ["classIndex"] = new JsonObject
                            {
                                ["type"] = "integer",
                                ["description"] = "要调整的节次序号（1 表示第 1 节课，2 表示第 2 节课，依此类推；传 -1 可直接表示最后一节课）"
                            },
                            ["classIndices"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["description"] = "需要同时调整为该科目的多个节次序号（例如：[1, -1] 表示第一节和最后一节课，[1, 2] 等）",
                                ["items"] = new JsonObject { ["type"] = "integer" }
                            },
                            ["subject"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "调整后的新科目名称（如：化学、数学、自习）"
                            },
                            ["modifiedClasses"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["description"] = "修改后的完整节次科目列表（若已指定 classIndex 和 subject 则优先使用单科替换）",
                                ["items"] = new JsonObject { ["type"] = "string" }
                            }
                        },
                        ["required"] = new JsonArray()
                    }
                }
            },

            // 7. 读取应用全局设置（直接调接口）
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "get_app_settings",
                    ["description"] = "直接通过服务接口获取 ClassIsland 的应用设置（如通知开关、深色/浅色主题模式、窗口透明度、界面缩放、全屏/上课隐藏状态等）",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject(),
                        ["required"] = new JsonArray()
                    }
                }
            },

            // 8. 修改应用全局设置（直接调接口即时生效，无需用户点击）
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "update_app_settings",
                    ["description"] = "直接通过底层接口修改 ClassIsland 的应用设置并即时保存生效，无需用户手动在设置页面点击（例如开启/关闭通知、切换深色/浅色主题、调整透明度与缩放、上课/全屏隐藏等）",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["timeOffsetSeconds"] = new JsonObject
                            {
                                ["type"] = "number",
                                ["description"] = "时间偏移量（单位：秒，支持正数或负数，例如 10、-5、0）。设定课程时间与实际时间的偏移值。增大偏移以抵消铃声提前，减小偏移以抵消铃声滞后。"
                            },
                            ["isNotificationEnabled"] = new JsonObject
                            {
                                ["type"] = "boolean",
                                ["description"] = "通知总开关（true 为开启提醒，false 为静音/关闭提醒）"
                            },
                            ["theme"] = new JsonObject
                            {
                                ["type"] = "integer",
                                ["description"] = "主题外观模式：0 为跟随系统，1 为浅色模式，2 为深色模式"
                            },
                            ["opacity"] = new JsonObject
                            {
                                ["type"] = "number",
                                ["description"] = "窗口不透明度（0.1 到 1.0）"
                            },
                            ["scale"] = new JsonObject
                            {
                                ["type"] = "number",
                                ["description"] = "界面缩放比例（0.5 到 2.0）"
                            },
                            ["hideOnClass"] = new JsonObject
                            {
                                ["type"] = "boolean",
                                ["description"] = "上课时是否自动隐藏悬浮窗"
                            },
                            ["hideOnFullscreen"] = new JsonObject
                            {
                                ["type"] = "boolean",
                                ["description"] = "全屏应用运行时是否自动隐藏悬浮窗"
                            },
                            ["isClassPrepareNotificationEnabled"] = new JsonObject
                            {
                                ["type"] = "boolean",
                                ["description"] = "是否开启上课预备提醒"
                            },
                            ["isClassOffNotificationEnabled"] = new JsonObject
                            {
                                ["type"] = "boolean",
                                ["description"] = "是否开启下课提醒"
                            },
                            ["customSettings"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["description"] = "其他支持的任意设置项键值对"
                            }
                        },
                        ["required"] = new JsonArray()
                    }
                }
            },

            // 9. 导航应用页面或打开功能页
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "navigate_app_page",
                    ["description"] = "当用户明确要求打开设置窗口界面或插件页面时，通过 classisland:// 内部协议跳转打开页面",
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
