using System.Text.RegularExpressions;

namespace Commander;

public static class Hotwords
{
    private static readonly string[] Canonical =
    [
        "ChatGPT", "GPT-4", "GPT-4o", "GPT-5", "GPT", "OpenAI", "Claude Code", "Claude",
        "Codex", "Anthropic", "Gemini", "DeepSeek", "Grok", "Copilot", "Cursor", "Midjourney",
        "LangChain", "PyTorch", "TensorFlow", "Llama", "Ollama", "Whisper", "SenseVoice", "Qwen",
        "Qwen3-ASR", "Stable Diffusion", "DALL-E", "Perplexity", "Kimi", "Sora",
        "LLM", "AGI", "RAG", "NLP", "MCP", "Hugging Face",
        "GitHub", "GitLab", "VS Code", "Xcode", "Docker", "Kubernetes", "kubectl",
        "Python", "JavaScript", "TypeScript", "Node.js", "Next.js", "React", "Vue", "Angular",
        "Swift", "SwiftUI", "Kotlin", "Rust", "Golang", "WebAssembly",
        "API", "SDK", "CLI", "GUI", "IDE", "JSON", "YAML", "XML", "HTML", "CSS", "SQL",
        "Redis", "MongoDB", "PostgreSQL", "GraphQL", "Vite", "Webpack", "Tailwind", "Figma",
        "Linux", "Ubuntu", "macOS", "iOS", "iPadOS", "Android", "AWS", "Azure",
        "Cloudflare", "Vercel", "Supabase", "Firebase", "Notion", "Zoom", "Microsoft Teams",
        "iPhone", "iPad", "MacBook", "AirPods", "Apple Watch", "Apple Silicon", "HarmonyOS",
        "PlayStation", "Xbox", "Nintendo Switch", "Wi-Fi", "Bluetooth", "USB-C", "HDMI", "NAS", "WPS",
        "WeChat", "Alipay", "TikTok", "YouTube", "Netflix", "Spotify", "B站",
        "URL", "HTTP", "HTTPS", "TCP", "DNS", "VPN", "CDN", "SaaS", "UI", "UX", "SEO",
    ];

    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>
    {
        ["git hub"] = "GitHub", ["open ai"] = "OpenAI", ["chat gpt"] = "ChatGPT",
        ["chatgtp"] = "ChatGPT", ["type script"] = "TypeScript", ["java script"] = "JavaScript",
        ["type-script"] = "TypeScript", ["vs code"] = "VS Code", ["v s code"] = "VS Code",
        ["vscode"] = "VS Code", ["visual studio code"] = "VS Code", ["node js"] = "Node.js",
        ["nodejs"] = "Node.js", ["next js"] = "Next.js", ["nextjs"] = "Next.js",
        ["hugging face"] = "Hugging Face", ["huggingface"] = "Hugging Face",
        ["postgres"] = "PostgreSQL", ["mongo db"] = "MongoDB", ["tail wind"] = "Tailwind",
        ["cloud code"] = "Claude Code", ["claude code"] = "Claude Code",
        ["deep seek"] = "DeepSeek", ["co pilot"] = "Copilot", ["lang chain"] = "LangChain",
        ["sense voice"] = "SenseVoice", ["qwen 3 asr"] = "Qwen3-ASR",
        ["apple silicon"] = "Apple Silicon", ["apple watch"] = "Apple Watch",
        ["air pods"] = "AirPods", ["mac book"] = "MacBook", ["harmony os"] = "HarmonyOS",
        ["play station"] = "PlayStation", ["nintendo switch"] = "Nintendo Switch",
        ["microsoft teams"] = "Microsoft Teams", ["b 站"] = "B站",
        ["g p t"] = "GPT", ["m c p"] = "MCP", ["l l m"] = "LLM", ["a p i"] = "API",
        ["wifi"] = "Wi-Fi", ["wi fi"] = "Wi-Fi", ["usb c"] = "USB-C",
        ["web assembly"] = "WebAssembly",
    };

    private static readonly IReadOnlyList<(Regex Pattern, string Replacement)> Rules = BuildRules();

    public static string Correct(string text)
    {
        var value = text;
        foreach (var (pattern, replacement) in Rules) value = pattern.Replace(value, replacement);
        return value;
    }

    private static IReadOnlyList<(Regex, string)> BuildRules()
    {
        var pairs = Aliases.Select(item => (Term: item.Key, Replacement: item.Value))
            .Concat(Canonical.Select(term => (Term: term, Replacement: term)))
            .OrderByDescending(item => item.Term.Length);
        return pairs.Select(item =>
            (new Regex(
                $"(?<![A-Za-z0-9]){Regex.Escape(item.Term)}(?![A-Za-z0-9])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
             item.Replacement)).ToArray();
    }
}
