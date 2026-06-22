using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AbyssMod
{
    /// <summary>
    /// 翻译清单数据结构，对应远程 manifest.json 格式。
    /// </summary>
    public class Manifest
    {
        [JsonPropertyName("hash")]
        public string Hash { get; set; }

        [JsonPropertyName("names")]
        public string Names { get; set; }

        [JsonPropertyName("titles")]
        public string Titles { get; set; }

        [JsonPropertyName("descriptions")]
        public string Descriptions { get; set; }

        [JsonPropertyName("another_name")]
        public string AnotherName { get; set; }

        [JsonPropertyName("ability_descriptions")]
        public string AbilityDescriptions { get; set; }

        [JsonPropertyName("novels")]
        public Dictionary<string, string> Novels { get; set; }

        /// <summary>other/{category}/ 各子類別檔案雜湊（可選，社群 CDN 擴展）。</summary>
        [JsonPropertyName("other")]
        public Dictionary<string, string> Other { get; set; }

        /// <summary>add-on/{category}/ 各子類別檔案雜湊（可選，社群精翻 CDN）。</summary>
        [JsonPropertyName("add_on")]
        public Dictionary<string, string> AddOn { get; set; }
    }
}
