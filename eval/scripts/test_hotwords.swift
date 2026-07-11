import Foundation

@main
struct HotwordsTests {
    static func main() {
        let cases = [
            ("open ai 和 chat gpt", "OpenAI 和 ChatGPT"),
            ("air pods 连接 mac book", "AirPods 连接 MacBook"),
            ("用 v s code 打开 node js 项目", "用 VS Code 打开 Node.js 项目"),
            ("打开 b 站看视频", "打开 B站看视频"),
            ("m c p 接口和 g p t 模型", "MCP 接口和 GPT 模型"),
            ("APIClient 不应被拆开", "APIClient 不应被拆开"),
            ("我今天坐地铁回家", "我今天坐地铁回家"),
        ]

        for (input, expected) in cases {
            let actual = Hotwords.correct(input)
            guard actual == expected else {
                fputs("FAIL\ninput: \(input)\nexpected: \(expected)\nactual: \(actual)\n", stderr)
                exit(1)
            }
        }
        print("Hotwords: \(cases.count) cases passed")
    }
}
