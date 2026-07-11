import Foundation

/// 内置术语热词规范（无 UI）：转写后把常见生活应用、数码与科技/AI 专名统一为正确写法。
/// 纯规则、零模型，只碰明确列出的大小写和分词变体，不猜测中文同音词。
enum Hotwords {

    /// 规范写法（按其本身做大小写不敏感匹配 → 统一成这个写法）。
    /// 只放"有特定大小写/拼写"的词；天然小写的普通词（token/prompt/async…）不放，免得误大写。
    static let canonical: [String] = [
        // —— AI / 大模型 ——
        "ChatGPT", "GPT-4", "GPT-4o", "GPT-5", "GPT", "OpenAI", "Claude Code", "Claude",
        "Codex", "Anthropic", "Gemini", "DeepSeek", "Grok", "Copilot", "Cursor", "Midjourney",
        "LangChain", "PyTorch", "TensorFlow", "Llama", "Ollama", "Whisper", "SenseVoice", "Qwen",
        "Qwen3-ASR", "Stable Diffusion", "DALL-E", "Perplexity", "Kimi", "Sora",
        "LLM", "AGI", "RAG", "NLP", "MCP", "Hugging Face",
        // —— 开发 ——
        "GitHub", "GitLab", "VS Code", "Xcode", "Docker", "Kubernetes", "kubectl",
        "Python", "JavaScript", "TypeScript", "Node.js", "Next.js", "React", "Vue", "Angular",
        "Swift", "SwiftUI", "Kotlin", "Rust", "Golang", "WebAssembly",
        "API", "SDK", "CLI", "GUI", "IDE", "JSON", "YAML", "XML", "HTML", "CSS", "SQL",
        "Redis", "MongoDB", "PostgreSQL", "GraphQL", "Vite", "Webpack", "Tailwind", "Figma",
        // —— 系统 / 云 ——
        "Linux", "Ubuntu", "macOS", "iOS", "iPadOS", "Android", "AWS", "Azure",
        "Cloudflare", "Vercel", "Supabase", "Firebase", "Notion", "Zoom", "Microsoft Teams",
        // —— 硬件 / 网络 ——
        "iPhone", "iPad", "MacBook", "AirPods", "Apple Watch", "Apple Silicon", "HarmonyOS",
        "PlayStation", "Xbox", "Nintendo Switch", "Wi-Fi", "Bluetooth", "USB-C", "HDMI", "NAS", "WPS",
        // —— 日常应用 / 服务 ——
        "WeChat", "Alipay", "TikTok", "YouTube", "Netflix", "Spotify", "B站",
        "URL", "HTTP", "HTTPS", "TCP", "DNS", "VPN", "CDN", "SaaS", "UI", "UX", "SEO",
    ]

    /// 明确的"错/变体 → 正确"映射（分词、连写、常见听错）。
    static let aliases: [String: String] = [
        "git hub": "GitHub", "open ai": "OpenAI", "chat gpt": "ChatGPT", "chatgtp": "ChatGPT",
        "type script": "TypeScript", "java script": "JavaScript", "type-script": "TypeScript",
        "vs code": "VS Code", "v s code": "VS Code", "vscode": "VS Code", "visual studio code": "VS Code",
        "node js": "Node.js", "nodejs": "Node.js", "next js": "Next.js", "nextjs": "Next.js",
        "hugging face": "Hugging Face", "huggingface": "Hugging Face",
        "postgres": "PostgreSQL", "mongo db": "MongoDB", "tail wind": "Tailwind",
        "cloud code": "Claude Code", "claude code": "Claude Code",
        "deep seek": "DeepSeek", "co pilot": "Copilot", "lang chain": "LangChain",
        "sense voice": "SenseVoice", "qwen 3 asr": "Qwen3-ASR",
        "apple silicon": "Apple Silicon", "apple watch": "Apple Watch",
        "air pods": "AirPods", "mac book": "MacBook", "harmony os": "HarmonyOS",
        "play station": "PlayStation", "nintendo switch": "Nintendo Switch",
        "microsoft teams": "Microsoft Teams", "b 站": "B站",
        "g p t": "GPT", "m c p": "MCP", "l l m": "LLM", "a p i": "API",
        "wifi": "Wi-Fi", "wi fi": "Wi-Fi", "usb c": "USB-C", "web assembly": "WebAssembly",
    ]

    private static let rules: [(re: NSRegularExpression, repl: String)] = build()

    private static func build() -> [(NSRegularExpression, String)] {
        // 别名 + 规范词；长的在前，避免 "Claude" 抢在 "Claude Code" 前面。
        var pairs: [(String, String)] = aliases.map { ($0.key, $0.value) }
        pairs += canonical.map { ($0, $0) }
        pairs.sort { $0.0.count > $1.0.count }
        return pairs.compactMap { term, repl in
            let p = "(?<![A-Za-z0-9])" + NSRegularExpression.escapedPattern(for: term) + "(?![A-Za-z0-9])"
            guard let re = try? NSRegularExpression(pattern: p, options: [.caseInsensitive]) else { return nil }
            return (re, repl)
        }
    }

    static func correct(_ text: String) -> String {
        guard !text.isEmpty else { return text }
        var t = text
        for (re, repl) in rules {
            let range = NSRange(t.startIndex..., in: t)
            t = re.stringByReplacingMatches(in: t, range: range,
                                            withTemplate: NSRegularExpression.escapedTemplate(for: repl))
        }
        return t
    }
}
