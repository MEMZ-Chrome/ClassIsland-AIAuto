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
    /// 基于 Tools Call 的通用 Agent 对话交互循环
    /// </summary>
    public async Task<AgentChatResponse> ExecuteAgentTurnAsync(
        PluginSettings settings,
        JsonArray conversationMessages,
        ToolExecutor? executor,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = settings.CurrentBaseUrl.TrimEnd('/');
        var chatUrl = baseUrl.EndsWith("/chat/completions") ? baseUrl : $"{baseUrl}/chat/completions";

        var agentResponse = new AgentChatResponse();
        var tools = AiToolDefinitions.GetAvailableTools();

        // 构造请求
        var requestPayload = new JsonObject
        {
            ["model"] = settings.CurrentModel,
            ["messages"] = conversationMessages.DeepClone(),
            ["tools"] = tools,
            ["tool_choice"] = "auto",
            ["temperature"] = 0.2
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, chatUrl)
        {
            Content = new StringContent(requestPayload.ToJsonString(), Encoding.UTF8, "application/json")
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
        var choice = doc.RootElement.GetProperty("choices")[0];
        var message = choice.GetProperty("message");

        var textContent = message.TryGetProperty("content", out var cElem) && cElem.ValueKind == JsonValueKind.String
            ? cElem.GetString() ?? ""
            : "";

        agentResponse.ReplyText = textContent;

        // 检查是否有 tool_calls
        if (message.TryGetProperty("tool_calls", out var toolCallsElem) && toolCallsElem.ValueKind == JsonValueKind.Array)
        {
            var assistantMsg = new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = textContent,
                ["tool_calls"] = JsonNode.Parse(toolCallsElem.GetRawText())
            };
            conversationMessages.Add(assistantMsg);

            foreach (var tc in toolCallsElem.EnumerateArray())
            {
                var id = tc.GetProperty("id").GetString() ?? Guid.NewGuid().ToString();
                var fn = tc.GetProperty("function");
                var name = fn.GetProperty("name").GetString() ?? "";
                var argsStr = fn.GetProperty("arguments").GetString() ?? "{}";

                JsonObject argsObj;
                try
                {
                    argsObj = JsonNode.Parse(argsStr) as JsonObject ?? new JsonObject();
                }
                catch
                {
                    argsObj = new JsonObject();
                }

                var callInfo = new ToolCallInfo
                {
                    Id = id,
                    Name = name,
                    Arguments = argsObj
                };
                agentResponse.ToolCalls.Add(callInfo);

                // 执行工具
                ToolExecutionResult execResult;
                if (executor != null)
                {
                    execResult = executor.Execute(callInfo);
                }
                else
                {
                    execResult = new ToolExecutionResult
                    {
                        ToolCallId = id,
                        Name = name,
                        Success = false,
                        Result = "本地工具执行器未注入"
                    };
                }
                agentResponse.ExecutedResults.Add(execResult);

                // 将工具调用结果送回历史记录
                conversationMessages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = id,
                    ["content"] = execResult.Result
                });
            }

            // 二轮请求：把工具执行结果交给模型进行最终整合回答
            var secondPayload = new JsonObject
            {
                ["model"] = settings.CurrentModel,
                ["messages"] = conversationMessages.DeepClone(),
                ["temperature"] = 0.3
            };

            using var secondReq = new HttpRequestMessage(HttpMethod.Post, chatUrl)
            {
                Content = new StringContent(secondPayload.ToJsonString(), Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(settings.CurrentApiKey))
            {
                secondReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.CurrentApiKey.Trim());
            }

            using var secondResp = await _httpClient.SendAsync(secondReq, cancellationToken);
            if (secondResp.IsSuccessStatusCode)
            {
                var secondRaw = await secondResp.Content.ReadAsStringAsync(cancellationToken);
                using var secondDoc = JsonDocument.Parse(secondRaw);
                var secondChoice = secondDoc.RootElement.GetProperty("choices")[0];
                var secondMsg = secondChoice.GetProperty("message");
                if (secondMsg.TryGetProperty("content", out var finalContent) && finalContent.ValueKind == JsonValueKind.String)
                {
                    agentResponse.ReplyText = finalContent.GetString() ?? agentResponse.ReplyText;
                }
            }
        }
        else
        {
            // 无 tool call，直接记录回复
            conversationMessages.Add(new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = textContent
            });
        }

        return agentResponse;
    }
}
