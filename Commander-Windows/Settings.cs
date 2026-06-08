using System;
using System.IO;
using System.Text.Json;

namespace Commander
{
    public class Settings
    {
        private static Settings? _instance;
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Commander", "settings.json");

        public bool IsEnabled { get; set; } = false;
        public int TargetKeyCode { get; set; } = 0xA5; // VK_RMENU (Right Alt)
        public string TargetKeyName { get; set; } = "Right Alt";
        public bool LaunchAtLogin { get; set; } = false;
        public string Language { get; set; } = "auto";

        public static Settings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Load();
                }
                return _instance;
            }
        }

        private static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
                }
            }
            catch { }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }
    }
}
