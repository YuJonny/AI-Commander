using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Commander;

/// <summary>
/// 本机润色性能探测。
///
/// 为什么需要：Windows 版内嵌的是 llama.cpp **CPU** 运行时，而 macOS 走 Metal GPU。
/// 同一个 Qwen3-8B Q4 在 Metal 上约 25-40 tok/s，在普通笔记本 CPU 上可能只有 3-8 tok/s——
/// 相差一个数量级。如果照搬 macOS 的固定超时预算，慢机器上每次润色都会走到超时再回退规则：
/// 用户下载了 4.7GB，看着"润色中"转十几秒，最后什么也没得到。
///
/// 所以在用户第一次打开润色开关时实测一次：用与真实请求同规模的负载测出本机吞吐，
/// 据此判定「可用 / 慢但可用 / 不可用」，并把运行时超时预算按实测值放宽，而不是拍脑袋。
/// 结果持久化，只测一次。
/// </summary>
public static class LlmPerformanceProbe
{
    /// 真实润色请求的典型规模（生产 prompt 约 700 token + 口述正文，输出约 70 token）
    private const double TypicalPromptTokens = 740;
    private const double TypicalOutputTokens = 70;

    /// 判定门槛（预计单次润色耗时）
    private const double GoodSeconds = 12;
    private const double UsableSeconds = 30;

    /// 探测本身允许的冷加载时间：4.7GB 权重首次从机械硬盘载入可能很慢，
    /// 这是用户主动触发、有界面反馈的一次性动作，给足时间比误判"不可用"好。
    private static readonly TimeSpan ProbeReadyTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ProbeRequestTimeout = TimeSpan.FromSeconds(180);

    private static readonly HttpClient HttpClient = new();

    public enum Tier
    {
        /// 未测过
        Unknown = 0,
        /// 流畅：按正常预算跑
        Good = 1,
        /// 慢但可用：允许用户自行选择，超时预算按实测放宽
        Slow = 2,
        /// 太慢：不建议开启，开了也基本只会超时回退
        TooSlow = 3,
        /// 探测失败（服务起不来等），不拦截用户，按未知处理
        Failed = 4,
    }

    public readonly record struct Result(
        Tier Tier,
        double PromptTokensPerSecond,
        double GenerationTokensPerSecond,
        double EstimatedSeconds);

    /// <summary>实测一次本机吞吐；结果写入 Settings。</summary>
    public static async Task<Result> RunAsync(CancellationToken token = default)
    {
        try
        {
            if (!await LlamaRunner.Shared.EnsureReadyAsync(ProbeReadyTimeout, token))
                return Store(new Result(Tier.Failed, 0, 0, 0));

            // 用与生产同构的负载：长 prompt 触发真实 prefill 成本，再生成数十 token 测生成速度。
            // 若只发一句短句，prefill 几乎免费，会把慢机器误判成快机器。
            var padding = string.Concat(Enumerable.Repeat("这是一段用于测量本机推理速度的示例文本。", 40));
            var payload = new
            {
                messages = new[] { new { role = "user", content = padding + "\n请把上面这段话原样复述一遍。" } },
                temperature = 0,
                max_tokens = 64,
                chat_template_kwargs = new { enable_thinking = false },
            };
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(LlamaRunner.BaseUri, "v1/chat/completions"))
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };

            var timer = Stopwatch.StartNew();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(ProbeRequestTimeout);
            using var response = await HttpClient.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            timer.Stop();
            if (!response.IsSuccessStatusCode) return Store(new Result(Tier.Failed, 0, 0, 0));

            using var document = JsonDocument.Parse(body);
            var (prefill, generation) = ReadThroughput(document.RootElement, timer.Elapsed.TotalSeconds);
            if (prefill <= 0 || generation <= 0) return Store(new Result(Tier.Failed, 0, 0, 0));

            var estimate = TypicalPromptTokens / prefill + TypicalOutputTokens / generation;
            var tier = estimate <= GoodSeconds ? Tier.Good
                     : estimate <= UsableSeconds ? Tier.Slow
                     : Tier.TooSlow;
            AppLog.Info(nameof(LlmPerformanceProbe),
                $"prefill={prefill:F1} tok/s generation={generation:F1} tok/s estimate={estimate:F1}s tier={tier}");
            return Store(new Result(tier, prefill, generation, estimate));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AppLog.Error(nameof(LlmPerformanceProbe), exception);
            return Store(new Result(Tier.Failed, 0, 0, 0));
        }
    }

    /// <summary>按实测吞吐给出本次请求的超时预算；没测过则用保守默认值。</summary>
    public static double RequestTimeoutSeconds(int inputCharacters)
    {
        var settings = Settings.Instance;
        var prefill = settings.LlmPromptTokensPerSecond;
        var generation = settings.LlmGenerationTokensPerSecond;
        if (prefill <= 0 || generation <= 0)
            return Math.Clamp(10d + inputCharacters / 20d, 12d, 25d);   // 未探测：沿用原有保守预算

        // 输入字符 → token 粗估（中文约 1:1），输出与输入同量级
        var promptTokens = TypicalPromptTokens + inputCharacters;
        var outputTokens = Math.Clamp(inputCharacters, 32, 1024);
        var estimate = promptTokens / prefill + outputTokens / generation;
        // 1.6 倍余量吸收波动；下限保底 12 秒，上限 90 秒（慢机器也不至于永远挂着）
        return Math.Clamp(estimate * 1.6, 12d, 90d);
    }

    public static Tier CurrentTier => (Tier)Settings.Instance.LlmPerformanceTier;

    private static (double Prefill, double Generation) ReadThroughput(JsonElement root, double wallSeconds)
    {
        double prefill = 0, generation = 0;
        if (root.TryGetProperty("timings", out var timings))
        {
            if (timings.TryGetProperty("prompt_per_second", out var p) && p.TryGetDouble(out var pv)) prefill = pv;
            if (timings.TryGetProperty("predicted_per_second", out var g) && g.TryGetDouble(out var gv)) generation = gv;
        }
        // llama-server 未返回 timings 时按墙钟兜底估算
        if ((prefill <= 0 || generation <= 0) && wallSeconds > 0 &&
            root.TryGetProperty("usage", out var usage))
        {
            var promptTokens = usage.TryGetProperty("prompt_tokens", out var pt) && pt.TryGetDouble(out var ptv) ? ptv : 0;
            var outputTokens = usage.TryGetProperty("completion_tokens", out var ct) && ct.TryGetDouble(out var ctv) ? ctv : 0;
            if (promptTokens > 0 && outputTokens > 0)
            {
                // 无法区分 prefill/生成时，按总吞吐平摊——只用于避免整个探测失效
                var total = promptTokens + outputTokens;
                var rate = total / wallSeconds;
                prefill = prefill > 0 ? prefill : rate;
                generation = generation > 0 ? generation : outputTokens / wallSeconds;
            }
        }
        return (prefill, generation);
    }

    private static Result Store(Result result)
    {
        var settings = Settings.Instance;
        settings.LlmPerformanceTier = (int)result.Tier;
        settings.LlmPromptTokensPerSecond = result.PromptTokensPerSecond;
        settings.LlmGenerationTokensPerSecond = result.GenerationTokensPerSecond;
        settings.LlmEstimatedPolishSeconds = result.EstimatedSeconds;
        settings.Save();
        return result;
    }
}
