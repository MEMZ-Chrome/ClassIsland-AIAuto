using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClassIsland.Automation.Core.Models;

namespace ClassIsland.Automation.Core.Services;

public class AiClientService
{
    private readonly HttpClient _httpClient;

    public AiClientService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(120);
    }

    /// <summary>
    /// 获取提供商支持的模型列表
    /// </summary>
    public async Task<List<string>> FetchModelsAsync(string baseUrl, string apiKey, CancellationToken cancellationToken = default)
    {
        var models = new List<string>();
        if (string.IsNullOrWhiteSpace(baseUrl))
            return models;

        var url = baseUrl.TrimEnd('/');
        if (!url.EndsWith("/models"))
        {
            url += "/models";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in dataElement.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idElement))
                {
                    var id = idElement.GetString();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        models.Add(id);
                    }
                }
            }
        }

        models.Sort();
        return models;
    }

    /// <summary>
    /// 执行图片分析或对话，提取课表及指令
    /// </summary>
    public async Task<AiExtractedSchedule> ProcessScheduleOrCommandAsync(
        PluginSettings settings,
        string userPrompt,
        byte[]? imageBytes = null,
        string imageMimeType = "image/png",
        CancellationToken cancellationToken = default)
    {
        var baseUrl = settings.CurrentBaseUrl.TrimEnd('/');
        var chatUrl = baseUrl.EndsWith("/chat/completions") ? baseUrl : $"{baseUrl}/chat/completions";

        var systemPrompt = BuildSystemPrompt();

        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = systemPrompt
            }
        };

        var userContent = new JsonArray();
        var textContent = string.IsNullOrWhiteSpace(userPrompt)
            ? "请根据上传的课表图片，提取出科目、作息时间表、每日课表安排以及设置项。"
            : userPrompt;

        userContent.Add(new JsonObject
        {
            ["type"] = "text",
            ["text"] = textContent
        });

        if (imageBytes != null && imageBytes.Length > 0)
        {
            var base64 = Convert.ToBase64String(imageBytes);
            userContent.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject
                {
                    ["url"] = $"data:{imageMimeType};base64,{base64}"
                }
            });
        }

        messages.Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] = userContent
        });

        var requestBody = new JsonObject
        {
            ["model"] = settings.CurrentModel,
            ["messages"] = messages,
            ["temperature"] = 0.2
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, chatUrl)
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(settings.CurrentApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.CurrentApiKey.Trim());
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"AI 接口请求失败 ({response.StatusCode}): {rawResponse}");
        }

        using var doc = JsonDocument.Parse(rawResponse);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        return ParseAiResponse(content);
    }

    private static string BuildSystemPrompt()
    {
        return """
你是一个专门为 ClassIsland 课表信息显示软件提取课表配置与处理软件设置指令的自动化助手。
当用户上传课表图片或输入指令时，你必须且只能以合法的 JSON 格式进行回复（不要包含任何 markdown 语法外的闲聊内容），结构如下：

{
  "message": "简要操作说明或给用户的提示信息",
  "subjects": [
    {
      "name": "语文",
      "initial": "文",
      "teacherName": "张老师",
      "isOutDoor": false
    }
  ],
  "timeLayout": {
    "name": "作息时间表",
    "items": [
      {
        "startTime": "08:00:00",
        "endTime": "08:45:00",
        "timeType": 0,
        "breakName": "",
        "defaultSubject": ""
      },
      {
        "startTime": "08:45:00",
        "endTime": "08:55:00",
        "timeType": 1,
        "breakName": "课间休息",
        "defaultSubject": ""
      }
    ]
  },
  "classPlans": [
    {
      "name": "星期一",
      "classes": ["语文", "数学", "英语"]
    }
  ],
  "appSettings": {
    "IsNotificationSoundEnabled": true
  }
}

说明规范：
1. timeType 含义：0 为上课，1 为课间，2 为分割线，3 为行动。
2. 每一个 classPlans 中的 classes 数组长度应当严格对应 timeLayout 中 timeType==0 的节点数目。
3. 如果用户包含修改设置项的要求（如关闭声音、修改窗口位置、更新频率等），请在 appSettings 键值对中提供对应属性和值。
4. 如果只提到设置修改，subjects/timeLayout/classPlans 可为空列表或留空。
""";
    }

    private static AiExtractedSchedule ParseAiResponse(string content)
    {
        var cleaned = content.Trim();
        if (cleaned.StartsWith("```json"))
        {
            cleaned = cleaned[7..];
        }
        else if (cleaned.StartsWith("```"))
        {
            cleaned = cleaned[3..];
        }

        if (cleaned.EndsWith("```"))
        {
            cleaned = cleaned[..^3];
        }

        cleaned = cleaned.Trim();

        try
        {
            var result = JsonSerializer.Deserialize<AiExtractedSchedule>(cleaned, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (result != null)
            {
                return result;
            }
        }
        catch
        {
            // 如果解析失败，封装为普通文字消息返回
        }

        return new AiExtractedSchedule
        {
            Message = content
        };
    }
}
