import Foundation

/// 口水词清理 —— 纯规则、只删不改、零幻觉。对应框架第②步（Swift 版）。
enum Rules {
    typealias StepObserver = (_ step: String, _ text: String) -> Void

    // ⚠️ "额"不能进这里：金额/名额/额度会被腰斩——放 ambig 只在句首删
    static let fillers = ["嗯", "呃", "啊", "哦", "唉", "呐", "欸", "唔"]
    // 有歧义词：长词在前（正则择优匹配长的）
    // “这个/那个”可能是必要指代，文本规则无法可靠区分填充词，必须保留。
    static let ambig = ["然后呢", "然后", "就是说", "就是", "其实", "的话", "反正", "额"]

    private static func rx(_ s: String, _ pattern: String, _ tmpl: String) -> String {
        guard let re = try? NSRegularExpression(pattern: pattern) else { return s }
        let r = NSRange(s.startIndex..., in: s)
        return re.stringByReplacingMatches(in: s, range: r, withTemplate: tmpl)
    }

    static func clean(
        _ text: String,
        aggressive: Bool = true,
        onStep: StepObserver? = nil
    ) -> String {
        var t = text
        onStep?("input", t)
        t = rx(t, "[…·]+", "")
        onStep?("remove_pause_markers", t)
        for f in fillers { t = t.replacingOccurrences(of: f, with: "") }
        onStep?("remove_fillers", t)
        // 折叠"逗号重复"：查，查 -> 查
        t = rx(t, "([\\x{4E00}-\\x{9FFF}]{1,3})[，,]\\1(?![\\x{4E00}-\\x{9FFF}])", "$1")
        onStep?("collapse_repetition", t)

        if aggressive {
            let amb = ambig.map { NSRegularExpression.escapedPattern(for: $0) }.joined(separator: "|")
            // 歧义口水词：仅当领起小句（句首/标点后）时删
            t = rx(t, "(^|[，。！？、,.!?；;])\\s*(?:\(amb))", "$1")
            onStep?("remove_leading_ambiguous", t)
            // 连用时删前面的，保留最后一个
            for _ in 0..<3 { t = rx(t, "(?:\(amb))(?=(?:\(amb)))", "") }
            onStep?("remove_consecutive_ambiguous", t)
            // 删词后残留在句首的孤立尾词（如"然后"删完剩"呢"）
            t = rx(t, "(^|[，。！？、,.!?；;])\\s*[呢吧嘛]+", "$1")
            onStep?("remove_orphan_particles", t)
        } else {
            onStep?("ambiguous_cleanup_bypassed", t)
        }

        t = rx(t, "[，、]{2,}", "，")
        t = rx(t, "[，、,]+([。！？.!?])", "$1")
        // 句号后残留的逗号（删口水词"呃，"所致）："修复。，这个" → "修复。这个"
        t = rx(t, "([。！？.!?])\\s*[，、,]+", "$1")
        t = rx(t, "^[，、。,.\\s]+", "")
        onStep?("cleanup_punctuation", t)
        t = t.trimmingCharacters(in: .whitespacesAndNewlines)
        onStep?("output", t)
        return t
    }
}
