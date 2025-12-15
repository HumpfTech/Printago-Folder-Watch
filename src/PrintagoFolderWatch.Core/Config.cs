using System;
using System.IO;
using Newtonsoft.Json;

namespace PrintagoFolderWatch.Core
{
    public class Config
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".printago-folder-watch"
        );
        private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

        public string WatchPath { get; set; } = "";
        public string ApiUrl { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string StoreId { get; set; } = "";

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(WatchPath) &&
                   !string.IsNullOrWhiteSpace(ApiUrl) &&
                   !string.IsNullOrWhiteSpace(ApiKey) &&
                   !string.IsNullOrWhiteSpace(StoreId);
        }

        public static Config Load()
        {
            try
            {
                if (!Directory.Exists(ConfigDir))
                {
                    Directory.CreateDirectory(ConfigDir);
                }

                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    return JsonConvert.DeserializeObject<Config>(json) ?? new Config();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading config: {ex.Message}");
            }

            return new Config();
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(ConfigDir))
                {
                    Directory.CreateDirectory(ConfigDir);
                }

                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(ConfigFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving config: {ex.Message}");
            }
        }
    }
}
