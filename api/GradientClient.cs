using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CareerCoach.Agent;

public class GradientClient {
private readonly HttpClient _http = new();
    // ✅ OpenAI-compatible base for DigitalOcean Gradient
    private const string BaseUrl = "https://inference.do-ai.run/v1/chat/completions";
    private readonly string _key;


  public GradientClient(IConfiguration cfg) {
    _key = cfg["GRADIENT_API_KEY"] ?? "";
    if (!string.IsNullOrEmpty(_key))
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _key);
    }
    // Increase timeout to 5 minutes for long resume processing
    _http.Timeout = TimeSpan.FromMinutes(5);
  }

   public async Task<string> ChatAsync(string system, string user, string model = "llama3-8b-instruct", int? maxTokens = null)
    {
        if (string.IsNullOrEmpty(_key))
        {
            throw new InvalidOperationException("GRADIENT_API_KEY is not configured. Please set the GRADIENT_API_KEY environment variable.");
        }

        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user",   content = user }
            },
            temperature = 0.3,
            max_tokens = maxTokens
        };

        var json = JsonSerializer.Serialize(payload);
        var res = await _http.PostAsync(BaseUrl, new StringContent(json, Encoding.UTF8, "application/json"));

        // Better error visibility than EnsureSuccessStatusCode():
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Gradient error {((int)res.StatusCode)}: {res.ReasonPhrase}\n{body}");

        return body;
    }

    /// <summary>
    /// Advanced chat with function calling support
    /// </summary>
    public async Task<ChatCompletionResponse> ChatWithToolsAsync(
        List<ConversationMessage> messages,
        List<ToolDefinition>? tools = null,
        string model = "openai-gpt-4o-mini",
        double temperature = 0.3)
    {
        if (string.IsNullOrEmpty(_key))
        {
            throw new InvalidOperationException("GRADIENT_API_KEY is not configured. Please set the GRADIENT_API_KEY environment variable.");
        }

        var payload = new
        {
            model,
            messages,
            temperature,
            tools = tools ?? new List<ToolDefinition>(),
            tool_choice = tools?.Any() == true ? "auto" : (object?)"none"
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        // Debug logging
        Console.WriteLine($"[DEBUG] Sending to Gradient API:");
        Console.WriteLine($"[DEBUG] Payload: {json.Substring(0, Math.Min(500, json.Length))}...");

        var res = await _http.PostAsync(BaseUrl, new StringContent(json, Encoding.UTF8, "application/json"));
        var body = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
        {
            Console.WriteLine($"[ERROR] Gradient API Error: {body}");
            throw new HttpRequestException($"Gradient error {((int)res.StatusCode)}: {res.ReasonPhrase}\n{body}");
        }

        var response = JsonSerializer.Deserialize<ChatCompletionResponse>(body);
        return response ?? throw new Exception("Failed to deserialize response");
    }
}

/// <summary>
/// Response from the chat completion API
/// </summary>
public class ChatCompletionResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("object")]
    public string Object { get; set; } = "";

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("choices")]
    public List<ChatChoice> Choices { get; set; } = new();

    [JsonPropertyName("usage")]
    public Usage? Usage { get; set; }
}

public class ChatChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public ConversationMessage Message { get; set; } = new();

    [JsonPropertyName("finish_reason")]
    public string FinishReason { get; set; } = "";
}

public class Usage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}