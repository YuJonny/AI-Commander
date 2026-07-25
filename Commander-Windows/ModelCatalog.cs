using System.IO;

namespace Commander;

public enum ModelComponentKind
{
    QwenAsr,
    SileroVad,
    SenseVoiceAsr,
    Punctuation,
    LlamaRuntime,
    Llm,
}

public sealed record ModelRemoteFile(string RemotePath, string LocalPath);

public sealed record ModelFileSource(string BaseUrl, IReadOnlyList<ModelRemoteFile> Files);

public sealed record ModelItem(
    ModelComponentKind Kind,
    string DirectoryName,
    IReadOnlyList<string> RequiredFiles,
    double SizeMb,
    IReadOnlyList<ModelFileSource> FileSources,
    IReadOnlyList<string> ArchiveUrls,
    bool PreferArchives = false)
{
    public string DirectoryPath => Path.Combine(AppPaths.ModelsRoot, DirectoryName);
}

public static class ModelCatalog
{
    private static string[] GitHubMirrors(string url) =>
    [
        "https://ghfast.top/" + url,
        "https://gh-proxy.com/" + url,
        "https://ghproxy.net/" + url,
        url,
    ];

    public static ModelItem QwenAsr { get; } = new(
        ModelComponentKind.QwenAsr,
        "sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25",
        [
            "conv_frontend.onnx",
            "encoder.int8.onnx",
            "decoder.int8.onnx",
            "tokenizer/tokenizer_config.json",
            "tokenizer/merges.txt",
            "tokenizer/vocab.json",
        ],
        990,   // 散装 6 文件解包后的真实总量（838 是 tar.bz2 压缩后的大小）
        [
            new ModelFileSource(
                "https://modelscope.cn/models/zengshuishui/Qwen3-ASR-onnx/resolve/master/",
                [
                    new("model_0.6B/conv_frontend.onnx", "conv_frontend.onnx"),
                    new("model_0.6B/encoder.int8.onnx", "encoder.int8.onnx"),
                    new("model_0.6B/decoder.int8.onnx", "decoder.int8.onnx"),
                    new("tokenizer/merges.txt", "tokenizer/merges.txt"),
                    new("tokenizer/tokenizer_config.json", "tokenizer/tokenizer_config.json"),
                    new("tokenizer/vocab.json", "tokenizer/vocab.json"),
                ])
        ],
        // 归档兜底：先走 ModelScope 的整包（国内直连，实测 11MB/s），GitHub 镜像排在其后
        GitHubMirrors(
                "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/" +
                "sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25.tar.bz2")
            .Prepend(
                "https://modelscope.cn/models/ZhaoChaoqun/sherpa-onnx-asr-models/resolve/master/" +
                "Qwen3-ASR-0.6B.tar.bz2")
            .ToArray());

    public static ModelItem SileroVad { get; } = new(
        ModelComponentKind.SileroVad,
        "silero-vad",
        ["silero_vad.onnx"],
        0.63,
        [
            new ModelFileSource(
                "https://hf-mirror.com/csukuangfj/vad/resolve/main/",
                [new("silero_vad.onnx", "silero_vad.onnx")])
        ],
        GitHubMirrors(
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx"));

    public static ModelItem SenseVoiceAsr { get; } = new(
        ModelComponentKind.SenseVoiceAsr,
        "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17",
        ["model.int8.onnx", "tokens.txt"],
        229,
        [
            new ModelFileSource(
                "https://modelscope.cn/models/fengge2024/" +
                "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17/resolve/master/",
                [
                    new("model.int8.onnx", "model.int8.onnx"),
                    new("tokens.txt", "tokens.txt"),
                ]),
            new ModelFileSource(
                "https://hf-mirror.com/csukuangfj/" +
                "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17/resolve/main/",
                [
                    new("model.int8.onnx", "model.int8.onnx"),
                    new("tokens.txt", "tokens.txt"),
                ])
        ],
        GitHubMirrors(
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/" +
            "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17.tar.bz2"));

    public static ModelItem Punctuation { get; } = new(
        ModelComponentKind.Punctuation,
        "sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8",
        ["model.int8.onnx"],
        62,
        [
            // This fp32 mirror is larger, but is a dependable final fallback when GitHub is blocked.
            new ModelFileSource(
                "https://modelscope.cn/models/csukuangfj/" +
                "sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12/resolve/master/",
                [new("model.onnx", "model.int8.onnx")]),
            new ModelFileSource(
                "https://hf-mirror.com/csukuangfj/" +
                "sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12/resolve/main/",
                [new("model.onnx", "model.int8.onnx")])
        ],
        GitHubMirrors(
                "https://github.com/k2-fsa/sherpa-onnx/releases/download/punctuation-models/" +
                "sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8.tar.bz2")
            .Prepend(
                "https://modelscope.cn/models/ZhaoChaoqun/sherpa-onnx-asr-models/resolve/master/" +
                "sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8.tar.bz2")
            .ToArray(),
        PreferArchives: true);

    public static ModelItem LlamaRuntime { get; } = new(
        ModelComponentKind.LlamaRuntime,
        "llama-b9873-win-cpu-x64",
        [
            "llama-server.exe",
            "llama-server-impl.dll",
            "llama-common.dll",
            "llama.dll",
            "ggml.dll",
            "ggml-base.dll",
            "ggml-cpu-x64.dll",
            "libomp140.x86_64.dll",
        ],
        45,
        [],
        GitHubMirrors(
            "https://github.com/ggml-org/llama.cpp/releases/download/b9873/" +
            "llama-b9873-bin-win-cpu-x64.zip"),
        PreferArchives: true);

    public static ModelItem Llm { get; } = new(
        ModelComponentKind.Llm,
        "llm",
        ["Qwen3-8B-Q4_K_M.gguf"],
        4790,
        [
            new ModelFileSource(
                "https://modelscope.cn/models/Qwen/Qwen3-8B-GGUF/resolve/master/",
                [new("Qwen3-8B-Q4_K_M.gguf", "Qwen3-8B-Q4_K_M.gguf")]),
            new ModelFileSource(
                "https://hf-mirror.com/Qwen/Qwen3-8B-GGUF/resolve/main/",
                [new("Qwen3-8B-Q4_K_M.gguf", "Qwen3-8B-Q4_K_M.gguf")]),
            new ModelFileSource(
                "https://huggingface.co/Qwen/Qwen3-8B-GGUF/resolve/main/",
                [new("Qwen3-8B-Q4_K_M.gguf", "Qwen3-8B-Q4_K_M.gguf")]),
        ],
        []);

    public static IReadOnlyList<ModelItem> QwenGroup { get; } = [QwenAsr, SileroVad];
    public static IReadOnlyList<ModelItem> SenseVoiceGroup { get; } = [SenseVoiceAsr, Punctuation];
    public static IReadOnlyList<ModelItem> LlmGroup { get; } = [LlamaRuntime, Llm];

    public static IReadOnlyList<ModelItem> ForAsr(AsrModelKind model) =>
        model == AsrModelKind.Qwen ? QwenGroup : SenseVoiceGroup;

    public static bool IsReadyFile(ModelItem item, string relativePath)
    {
        var path = Path.Combine(item.DirectoryPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return false;
        try
        {
            return new FileInfo(path).Length >= MinimumReadyBytes(item.Kind, relativePath);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsReady(ModelItem item) =>
        item.RequiredFiles.All(relativePath => IsReadyFile(item, relativePath));

    /// 就绪判定失败时，说清是哪个文件、实际多大、期望多大。
    /// 否则界面只会莫名其妙地"换线路"，排查时完全瞎。
    public static string DescribeMissing(ModelItem item)
    {
        var problems = new List<string>();
        foreach (var relativePath in item.RequiredFiles)
        {
            if (IsReadyFile(item, relativePath)) continue;
            var path = Path.Combine(item.DirectoryPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var expected = MinimumReadyBytes(item.Kind, relativePath);
            if (!File.Exists(path))
            {
                problems.Add($"{relativePath}: missing");
                continue;
            }
            try
            {
                problems.Add($"{relativePath}: {new FileInfo(path).Length:N0} bytes, expected >= {expected:N0}");
            }
            catch (Exception exception)
            {
                problems.Add($"{relativePath}: unreadable ({exception.GetType().Name})");
            }
        }
        return problems.Count == 0 ? "ready" : string.Join("; ", problems);
    }

    /// 就绪门槛 = 实测真实大小的 ~95%，用来挡住截断/HTML 假文件，而不是卡合法文件。
    /// ⚠️ 数值必须来自实测，不能拍脑袋：此前 SileroVad 写成 1_800_000（大概是照 v5 的 2.2MB 猜的），
    /// 而官方文件只有 643,854 字节 → IsReady 永远为 false → 下载成功也被判失败，一路换源到 GitHub。
    /// 实测值（2026-07-25，对照本机可正常工作的安装与 ModelScope 源）：
    ///   conv_frontend 44,148,281 · encoder 182,491,662 · decoder 755,914,231
    ///   tokenizer_config 12,487 · merges 1,671,853 · vocab 2,776,833
    ///   silero_vad 643,854 · sense model 239,233,841 · sense tokens 315,894 · punct 75,519,198
    private static long MinimumReadyBytes(ModelComponentKind kind, string relativePath) =>
        (kind, relativePath.Replace('\\', '/')) switch
        {
            (ModelComponentKind.QwenAsr, "conv_frontend.onnx") => 41_900_000,
            (ModelComponentKind.QwenAsr, "encoder.int8.onnx") => 173_300_000,
            (ModelComponentKind.QwenAsr, "decoder.int8.onnx") => 718_100_000,
            (ModelComponentKind.QwenAsr, "tokenizer/tokenizer_config.json") => 11_800,
            (ModelComponentKind.QwenAsr, "tokenizer/merges.txt") => 1_588_000,
            (ModelComponentKind.QwenAsr, "tokenizer/vocab.json") => 2_637_000,
            (ModelComponentKind.SileroVad, _) => 611_000,
            (ModelComponentKind.SenseVoiceAsr, "model.int8.onnx") => 227_200_000,
            (ModelComponentKind.SenseVoiceAsr, "tokens.txt") => 300_000,
            (ModelComponentKind.Punctuation, _) => 60_000_000,
            (ModelComponentKind.Llm, _) => 5_000_000_000,
            _ => 1024,
        };

    public static bool IsAsrReady(AsrModelKind model) => ForAsr(model).All(IsReady);
    public static bool IsLlmReady => LlmGroup.All(IsReady);

    public static string? ResolveAsrDirectory(AsrModelKind model) =>
        IsAsrReady(model)
            ? (model == AsrModelKind.Qwen ? QwenAsr.DirectoryPath : SenseVoiceAsr.DirectoryPath)
            : null;
}
