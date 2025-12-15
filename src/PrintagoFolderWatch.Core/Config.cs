using System;
using System.Collections.Generic;
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
                    var settings = new JsonSerializerSettings
                    {
                        // Handle both camelCase (old config) and PascalCase (new config)
                        ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver()
                    };

                    // Try loading - Newtonsoft.Json handles case-insensitive by default with JsonProperty
                    var config = JsonConvert.DeserializeObject<Config>(json, settings);
                    if (config != null)
                    {
                        // If still empty, try manual mapping for legacy camelCase format
                        if (string.IsNullOrEmpty(config.WatchPath))
                        {
                            var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                            if (dict != null)
                            {
                                config.WatchPath = dict.GetValueOrDefault("watchPath", dict.GetValueOrDefault("WatchPath", "")) ?? "";
                                config.ApiUrl = dict.GetValueOrDefault("apiUrl", dict.GetValueOrDefault("ApiUrl", "")) ?? "";
                                config.ApiKey = dict.GetValueOrDefault("apiKey", dict.GetValueOrDefault("ApiKey", "")) ?? "";
                                config.StoreId = dict.GetValueOrDefault("storeId", dict.GetValueOrDefault("StoreId", "")) ?? "";
                            }
                        }
                        return config;
                    }
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
