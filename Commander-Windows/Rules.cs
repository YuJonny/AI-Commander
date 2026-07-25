using System.Text.RegularExpressions;

namespace Commander;

public static class Rules
{
    private static readonly string[] Fillers = ["嗯", "呃", "啊", "哦", "唉", "呐", "欸", "唔"];
    private static readonly string[] Ambiguous = ["然后呢", "然后", "就是说", "就是", "其实", "的话", "反正", "额"];

    public static string Clean(string text, bool aggressive = true, Action<string, string>? onStep = null)
    {
        var value = text;
        Step("input");
        value = Regex.Replace(value, "[…·]+", "");
        Step("remove_pause_markers");
        foreach (var filler in Fillers) value = value.Replace(filler, "", StringComparison.Ordinal);
        Step("remove_fillers");

        value = Regex.Replace(value, "([\\u4E00-\\u9FFF]{1,3})[，,]\\1(?![\\u4E00-\\u9FFF])", "$1");
        Step("collapse_repetition");

        if (aggressive)
        {
            var terms = string.Join("|", Ambiguous.Select(Regex.Escape));
            value = Regex.Replace(value, $"(^|[，。！？、,.!?；;])\\s*(?:{terms})", "$1");
            Step("remove_leading_ambiguous");
            for (var index = 0; index < 3; index++)
                value = Regex.Replace(value, $"(?:{terms})(?=(?:{terms}))", "");
            Step("remove_consecutive_ambiguous");
            value = Regex.Replace(value, "(^|[，。！？、,.!?；;])\\s*[呢吧嘛]+", "$1");
            Step("remove_orphan_particles");
        }
        else
        {
            Step("ambiguous_cleanup_bypassed");
        }

        value = Regex.Replace(value, "[，、]{2,}", "，");
        value = Regex.Replace(value, "[，、,]+([。！？.!?])", "$1");
        value = Regex.Replace(value, "([。！？.!?])\\s*[，、,]+", "$1");
        value = Regex.Replace(value, "^[，、。,.\\s]+", "");
        Step("cleanup_punctuation");
        value = value.Trim();
        Step("output");
        return value;

        void Step(string name) => onStep?.Invoke(name, value);
    }
}
