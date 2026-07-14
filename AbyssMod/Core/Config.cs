using BepInEx.Configuration;
using Utility.Toast;

namespace AbyssMod
{
    /// <summary>
    /// 全局配置管理器。
    /// 负责初始化所有配置项并绑定事件监听。
    /// </summary>
    public static class Config
    {
#if DEBUG
        #region Debug
        public static ConfigEntry<bool> Offline;
        public static ConfigEntry<string> OfflineAPI;
        public static bool OfflineStartup;
        #endregion
#endif

        #region General
        public static ConfigEntry<bool> DynamicMosaic;
        public static ConfigEntry<bool> SoundCaution;
        public static ConfigEntry<bool> VoiceInterruption;
        public static ConfigEntry<bool> TitleMovie;
        #endregion

        #region Translation
        public static ConfigEntry<bool> Translation;
        public static ConfigEntry<string> TranslationCDN;
        public static ConfigEntry<string> TranslationLanguage;
        public static ConfigEntry<string> TranslationCryptoTag;
        public static ConfigEntry<string> TranslationCryptoKey;
        #endregion

        #region Font
        public static ConfigEntry<string> FontBundlePath;
        #endregion

        #region Collector
        public static ConfigEntry<string> MTApiKey;
        public static ConfigEntry<bool> CollectText;
        public static ConfigEntry<bool> ClassifyText;
        #endregion

        #region MachineTranslation
        public static ConfigEntry<bool> MTEnabled;
        public static ConfigEntry<string> MTEngine;
        public static ConfigEntry<string> MTEndpoint;
        public static ConfigEntry<string> MTModel;
        public static ConfigEntry<int> MTTimeout;
        #endregion

        /// <summary>
        /// 初始化配置系统。
        /// </summary>
        public static void Initialize()
        {
            BindAllEntries();
            BindSettingChangedLog();
        }

        private static void BindAllEntries()
        {
#if DEBUG
            #region Debug
            Offline = Plugin.ConfigFile.Bind(
                "Debug.Offline",
                "Enabled",
                false,
                "API localization for debug"
            );
            OfflineAPI = Plugin.ConfigFile.Bind(
                "Debug.Offline",
                "CDN",
                "http://localhost:33333/abyss/",
                "CDN for debug"
            );
            #endregion
#endif

            #region General
            DynamicMosaic = Plugin.ConfigFile.Bind(
                "General",
                "DynamicMosaic",
                false,
                "Enable the game's dynamic mosaic censoring."
            );
            SoundCaution = Plugin.ConfigFile.Bind(
                "General",
                "SoundCaution",
                false,
                "Show the volume warning popup on game start."
            );
            VoiceInterruption = Plugin.ConfigFile.Bind(
                "General",
                "VoiceInterruption",
                false,
                "Interrupt the current voice line when the next silent text line plays during a story scene."
            );
            TitleMovie = Plugin.ConfigFile.Bind(
                "General",
                "TitleMovie",
                true,
                "Show the title animation on game startup."
            );
            #endregion

            #region Translation
            Translation = Plugin.ConfigFile.Bind(
                "Translation",
                "Enabled",
                true,
                "Enable in-game translation."
            );
            TranslationCDN = Plugin.ConfigFile.Bind(
                "Translation",
                "CDN",
                "https://raw.githubusercontent.com/Brombeis/dotabyss-translation/main/translations",
                "URL of the translation CDN."
            );
            TranslationLanguage = Plugin.ConfigFile.Bind(
                "Translation",
                "Language",
                "en",
                "Translation language code."
            );
            TranslationCryptoTag = Plugin.ConfigFile.Bind(
                "Translation.Crypto",
                "Tag",
                "ENC:",
                "Prefix tag that marks an encrypted translation value."
            );
            TranslationCryptoKey = Plugin.ConfigFile.Bind(
                "Translation.Crypto",
                "Key",
                "woshitonghuadawang",
                "Decryption key for encrypted translation values."
            );
            #endregion

            #region Font
            FontBundlePath = Plugin.ConfigFile.Bind(
                "Translation.Font",
                "AssetBundlePath",
                "",
                "Path to a TMP font AssetBundle. Leave empty to use the game's native font (recommended for English). Can be an absolute path or relative to the BepInEx/plugins folder."
            );
            #endregion

            #region Collector
            MTApiKey = Plugin.ConfigFile.Bind(
                "MachineTranslation",
                "ApiKey",
                "",
                "API key for cloud translation services. Engine=claude: Anthropic API key. Engine=openai: OpenAI API key. Leave empty for local services such as Ollama."
            );
            CollectText = Plugin.ConfigFile.Bind(
                "Collector",
                "CollectText",
                true,
                "Collect untranslated Japanese strings encountered at runtime and write them to the dump/ folder. Useful for contributing new translations — the disk overhead is negligible."
            );
            ClassifyText = Plugin.ConfigFile.Bind(
                "Collector",
                "ClassifyText",
                true,
                "Auto-classify collected strings into subcategories (equipment_effect, facility, bar, mission, materials, abyss_code, dialogue, system, ui_misc) rather than lumping everything into ui_misc."
            );
            #endregion

            #region MachineTranslation
            MTEnabled = Plugin.ConfigFile.Bind(
                "MachineTranslation",
                "Enabled",
                false,
                "Enable machine translation pre-processing. Untranslated strings are batched and sent to the configured engine in the background, then cached locally. Requires a running translation service (e.g. Ollama) unless using a cloud API."
            );
            MTEngine = Plugin.ConfigFile.Bind(
                "MachineTranslation",
                "Engine",
                "openai",
                "Translation engine. Options: openai (also compatible with LM Studio / Ollama), sugoi, libre, claude."
            );
            MTEndpoint = Plugin.ConfigFile.Bind(
                "MachineTranslation",
                "Endpoint",
                "http://127.0.0.1:11434/v1/chat/completions",
                "Full API endpoint URL for the translation service. openai/ollama default: http://127.0.0.1:11434/v1/chat/completions — sugoi: http://127.0.0.1:14366/ — libre: http://127.0.0.1:5000/translate"
            );
            MTModel = Plugin.ConfigFile.Bind(
                "MachineTranslation",
                "Model",
                "qwen2.5:3b",
                "Model name used by the openai/ollama engine. Examples: qwen2.5:3b (fast), qwen2.5:7b (better quality). Ignored for sugoi and libre."
            );
            MTTimeout = Plugin.ConfigFile.Bind(
                "MachineTranslation",
                "TimeoutSeconds",
                30,
                "Timeout in seconds for a single translation request."
            );
            #endregion
        }

        /// <summary>
        /// 绑定配置变更日志输出。
        /// </summary>
        private static void BindSettingChangedLog()
        {
            Plugin.ConfigFile.SettingChanged += (_, e) =>
            {
                var c = e.ChangedSetting;
                Plugin.Log.LogInfo(
                    $"[{c.Definition.Section}] {c.Definition.Key} => {c.BoxedValue}"
                );
                Toast.Info($"[{c.Definition.Section}]", $"{c.Definition.Key} => {c.BoxedValue}");
            };
        }
    }
}
