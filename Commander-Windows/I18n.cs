using System;
using System.Collections.Generic;
using System.Globalization;

namespace Commander
{
    public static class I18n
    {
        private static string _currentLang = "auto";

        private static readonly Dictionary<string, string> ZhStrings = new()
        {
            // Tabs
            ["tab.function"] = "功能",
            ["tab.about"] = "关于",
            ["tab.language"] = "Language",

            // Function
            ["title.app"] = "AI接线员",
            ["title.subtitle"] = "Commander",
            ["toggle.title"] = "总开关",
            ["toggle.description"] = "开启后耳机播放键将映射为目标按键",
            ["mapping.title"] = "映射目标",
            ["mapping.source"] = "播放/暂停",
            ["mapping.recording"] = "请按下目标按键...",
            ["mapping.cancel"] = "取消",
            ["mapping.change"] = "更改按键",
            ["mapping.reset"] = "恢复默认 (Right Alt)",
            ["launch.title"] = "开机自启动",
            ["launch.description"] = "登录时自动启动AI接线员",
            ["accessibility.not_needed"] = "Windows 无需额外权限",

            // About
            ["about.description"] = "将耳机媒体键映射为键盘按键",
            ["about.recommended"] = "推荐搭配 App",
            ["about.app.shandianshuo"] = "闪电说",
            ["about.app.shandianshuo.desc"] = "（本地模型）",
            ["about.app.typeless"] = "Typeless",
            ["about.app.typeless.desc"] = "（语音输入）",
            ["about.app.wechat.input"] = "微信输入法",
            ["about.app.doubao.input"] = "豆包输入法",
            ["about.recommendedHardware"] = "推荐搭配硬件",
            ["about.hw.nativeunion"] = "Native Union 复古电话",
            ["about.hw.nativeunion.desc"] = "更具时代反差感 | Apple Store入驻配件",

            // Language
            ["lang.title"] = "选择语言",
            ["lang.description"] = "切换应用界面语言",

            // Tray
            ["menu.title"] = "AI接线员 Commander",
            ["menu.status.running"] = "状态：运行中",
            ["menu.status.stopped"] = "状态：已停止",
            ["menu.disable"] = "关闭映射",
            ["menu.enable"] = "开启映射",
            ["menu.open"] = "打开窗口",
            ["menu.quit"] = "退出",

            // Status
            ["status.started"] = "事件监听已启动",
            ["status.stopped"] = "已停止",
            ["status.key.down"] = "播放键按下 ↓",
            ["status.key.up"] = "播放键松开 ↑",

            // Other
            ["app.already_running"] = "AI接线员已在运行中",
        };

        private static readonly Dictionary<string, string> EnStrings = new()
        {
            // Tabs
            ["tab.function"] = "Function",
            ["tab.about"] = "About",
            ["tab.language"] = "语言",

            // Function
            ["title.app"] = "Commander",
            ["title.subtitle"] = "AI接线员",
            ["toggle.title"] = "Master Switch",
            ["toggle.description"] = "Map headset play button to target key when enabled",
            ["mapping.title"] = "Mapping Target",
            ["mapping.source"] = "Play/Pause",
            ["mapping.recording"] = "Press target key...",
            ["mapping.cancel"] = "Cancel",
            ["mapping.change"] = "Change Key",
            ["mapping.reset"] = "Reset Default (Right Alt)",
            ["launch.title"] = "Launch at Login",
            ["launch.description"] = "Auto-start Commander at login",
            ["accessibility.not_needed"] = "No extra permissions needed on Windows",

            // About
            ["about.description"] = "Map headset media keys to keyboard keys",
            ["about.recommended"] = "Recommended Apps",
            ["about.app.shandianshuo"] = "ShanDianShuo",
            ["about.app.shandianshuo.desc"] = "(Local Model)",
            ["about.app.typeless"] = "Typeless",
            ["about.app.typeless.desc"] = "(Voice Input)",
            ["about.app.wechat.input"] = "WeChat Input",
            ["about.app.doubao.input"] = "Doubao Input",
            ["about.recommendedHardware"] = "Recommended Hardware",
            ["about.hw.nativeunion"] = "Native Union Retro Phone",
            ["about.hw.nativeunion.desc"] = "Extra retro contrast — available at the Apple Store",

            // Language
            ["lang.title"] = "Select Language",
            ["lang.description"] = "Switch the app interface language",

            // Tray
            ["menu.title"] = "Commander AI接线员",
            ["menu.status.running"] = "Status: Running",
            ["menu.status.stopped"] = "Status: Stopped",
            ["menu.disable"] = "Disable Mapping",
            ["menu.enable"] = "Enable Mapping",
            ["menu.open"] = "Open Window",
            ["menu.quit"] = "Quit",

            // Status
            ["status.started"] = "Event listener started",
            ["status.stopped"] = "Stopped",
            ["status.key.down"] = "Play key pressed ↓",
            ["status.key.up"] = "Play key released ↑",

            // Other
            ["app.already_running"] = "Commander is already running",
        };

        public static string CurrentLanguage
        {
            get => _currentLang;
            set
            {
                _currentLang = value;
                Settings.Instance.Language = value;
                Settings.Instance.Save();
                LanguageChanged?.Invoke();
            }
        }

        public static event Action? LanguageChanged;

        public static bool IsChinese
        {
            get
            {
                if (_currentLang == "auto")
                    return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
                return _currentLang == "zh-Hans";
            }
        }

        public static string Get(string key)
        {
            var dict = IsChinese ? ZhStrings : EnStrings;
            return dict.TryGetValue(key, out var val) ? val : key;
        }

        public static void Init()
        {
            _currentLang = Settings.Instance.Language;
        }
    }
}
