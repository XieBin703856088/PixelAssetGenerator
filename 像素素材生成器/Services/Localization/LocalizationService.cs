using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace PixelAssetGenerator.Services.Localization
{
    /// <summary>
    /// Singleton localization service with INotifyPropertyChanged.
    /// XAML binds to Instance via {Binding Source={x:Static loc:LocalizationService.Instance}, Path=[Key]}.
    /// Language switch triggers PropertyChanged which refreshes all bindings.
    ///
    /// Lookup chain (first non-empty wins):
    ///   1. External JSON file for current culture (%APPDATA%/PixelAssetGenerator/Languages/{culture}.json)
    ///   2. Built-in embedded JSON for current culture
    ///   3. Built-in embedded zh-Hans.json (fallback)
    ///   4. Returns the key itself
    /// </summary>
    public sealed class LocalizationService : ILocalizationService
    {
        private const string FallbackCulture = "zh-Hans";
        private const string LanguagesResourcePrefix = "PixelAssetGenerator.Resources.Languages.";
        private string _currentCulture = FallbackCulture;

        public string CurrentCulture
        {
            get => _currentCulture;
            private set => _currentCulture = value;
        }

        public string CurrentCultureDisplayName { get; private set; } = "中文（简体）";

        public event PropertyChangedEventHandler? PropertyChanged;
        /// <summary>Fired after language switch for code-side consumers.</summary>
        public event Action? CultureChanged;

        /// <summary>
        /// Indexer for XAML binding: {Binding Source={x:Static loc:LocalizationService.Instance}, Path=[Key]}
        /// </summary>
        public string this[string key] => GetString(key);

        /// <summary>All known keys across all built-in languages.</summary>
        private string[] _allKeys = Array.Empty<string>();
        public IReadOnlyList<string> AllKeys => _allKeys;

        /// <summary>
        /// External language dictionaries keyed by culture code (e.g., "ja" -> { key -> value }).
        /// Loaded from %APPDATA%/PixelAssetGenerator/Languages/*.json.
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, string>> _externalLanguages = new();

        /// <summary>
        /// Built-in embedded JSON dictionaries, keyed by culture code.
        /// Loaded once from assembly embedded resources at static init.
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, string>> BuiltInLanguages;
        private static readonly JsonSerializerOptions JsonOptions;

        /// <summary>Fast string cache for the current culture.</summary>
        private readonly Dictionary<string, string> _cache = new();

        static LocalizationService()
        {
            BuiltInLanguages = LoadBuiltInLanguages();
            JsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }

        private LocalizationService()
        {
            // Union of all keys across all built-in languages for template generation
            _allKeys = BuiltInLanguages
                .SelectMany(kv => kv.Value.Keys)
                .Distinct()
                .OrderBy(k => k)
                .ToArray();

            LoadExternalLanguages();
            BuildCache();
        }

        // ------------------------------------------------ //
        //  String lookup
        // ------------------------------------------------ //

        /// <summary>
        /// Returns the localized string. Lookup order:
        /// external JSON -> built-in JSON -> fallback zh-Hans -> key.
        /// </summary>
        public string GetString(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            // 1. External language file (user-installed)
            if (_externalLanguages.TryGetValue(_currentCulture, out var langDict)
                && langDict.TryGetValue(key, out var extVal)
                && !string.IsNullOrEmpty(extVal))
                return extVal;

            // 2. Built-in embedded JSON for current culture
            if (BuiltInLanguages.TryGetValue(_currentCulture, out var builtIn)
                && builtIn.TryGetValue(key, out var builtInVal)
                && !string.IsNullOrEmpty(builtInVal))
                return builtInVal;

            // 3. Fallback to zh-Hans
            if (_currentCulture != FallbackCulture
                && BuiltInLanguages.TryGetValue(FallbackCulture, out var fallback)
                && fallback.TryGetValue(key, out var fallbackVal)
                && !string.IsNullOrEmpty(fallbackVal))
                return fallbackVal;

            return key;
        }

        /// <summary>
        /// Gets localized string using cached dictionaries (faster for bulk access).
        /// </summary>
        public string GetStringFast(string key)
        {
            if (_cache.TryGetValue(key, out var val)) return val;
            val = GetString(key);
            _cache[key] = val;
            return val;
        }

        private void BuildCache()
        {
            _cache.Clear();
            foreach (var key in _allKeys)
                _cache[key] = GetString(key);
        }

        // ------------------------------------------------ //
        //  Culture switching
        // ------------------------------------------------ //

        /// <summary>
        /// Change language and notify all bindings.
        /// </summary>
        public void SetCulture(string cultureCode)
        {
            System.Diagnostics.Debug.WriteLine($"[SetCulture] requested={cultureCode} current={_currentCulture}");
            if (cultureCode == _currentCulture) return;

            _currentCulture = cultureCode;

            try
            {
                var ci = CultureInfo.GetCultureInfo(cultureCode);
                CurrentCultureDisplayName = ci.NativeName;
                System.Threading.Thread.CurrentThread.CurrentUICulture = ci;
                System.Threading.Thread.CurrentThread.CurrentCulture = ci;
            }
            catch (CultureNotFoundException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LocalizationService] Invalid culture code '{cultureCode}': {ex.Message}");
                _currentCulture = FallbackCulture;
                CurrentCultureDisplayName = "中文（简体）";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LocalizationService] Failed to set culture '{cultureCode}': {ex.Message}");
            }

            BuildCache();

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            CultureChanged?.Invoke();
        }

        // ------------------------------------------------ //
        //  Available cultures
        // ------------------------------------------------ //

        /// <summary>
        /// Returns available cultures as (code, displayName) pairs.
        /// Built-in cultures plus any external language files.
        /// </summary>
        public IReadOnlyList<(string Code, string DisplayName)> GetAvailableCultures()
        {
            var results = new List<(string, string)>();

            // Built-in languages first, in canonical order
            if (BuiltInLanguages.TryGetValue("zh-Hans", out var zhHans))
                results.Add(("zh-Hans", zhHans.GetValueOrDefault("_LanguageName", "中文（简体）")));
            if (BuiltInLanguages.TryGetValue("en", out var en))
                results.Add(("en", en.GetValueOrDefault("_LanguageName", "English")));

            // Additional built-in (e.g., if more are embedded later)
            foreach (var kv in BuiltInLanguages)
            {
                if (kv.Key != "zh-Hans" && kv.Key != "en")
                {
                    var displayName = kv.Value.GetValueOrDefault("_LanguageName", kv.Key);
                    results.Add((kv.Key, displayName));
                }
            }

            // External languages (user-installed JSON files)
            foreach (var kv in _externalLanguages)
            {
                if (results.Any(r => r.Item1 == kv.Key)) continue;
                var displayName = kv.Value.TryGetValue("_LanguageName", out var name) && !string.IsNullOrEmpty(name)
                    ? name
                    : kv.Key;
                results.Add((kv.Key, displayName));
            }

            return results;
        }

        // ------------------------------------------------ //
        //  Loading
        // ------------------------------------------------ //

        /// <summary>
        /// Loads built-in language dictionaries from embedded JSON resources.
        /// </summary>
        private static Dictionary<string, Dictionary<string, string>> LoadBuiltInLanguages()
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var resNames = asm.GetManifestResourceNames();

                foreach (var resName in resNames)
                {
                    if (!resName.StartsWith(LanguagesResourcePrefix, StringComparison.Ordinal))
                        continue;

                    var code = resName.Substring(LanguagesResourcePrefix.Length);
                    // Strip .json suffix if present (it's the logical name)
                    if (code.EndsWith(".json", StringComparison.Ordinal))
                        code = code.Substring(0, code.Length - 5);
                    if (string.IsNullOrEmpty(code)) continue;

                    using var stream = asm.GetManifestResourceStream(resName);
                    if (stream == null) continue;

                    using var reader = new StreamReader(stream);
                    var json = reader.ReadToEnd();
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null && dict.Count > 0)
                    {
                        result[code] = dict;
                        System.Diagnostics.Debug.WriteLine($"[Localization] Loaded built-in language: {code} ({dict.Count} keys)");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Localization] Failed to load built-in languages: {ex.Message}");
            }

            // Ensure at least zh-Hans is available
            if (!result.ContainsKey(FallbackCulture))
            {
                result[FallbackCulture] = new Dictionary<string, string> { ["_LanguageName"] = "中文（简体）" };
            }

            return result;
        }

        /// <summary>
        /// Loads external language files from the Languages folder.
        /// Also generates zh-Hans.json and en.json template files on first launch.
        /// </summary>
        private void LoadExternalLanguages()
        {
            try
            {
                var dir = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Languages");

                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // Generate built-in language template files (only if they don't exist)
                GenerateBuiltInLanguageFiles(dir, overwrite: false);

                if (!Directory.Exists(dir)) return;
                foreach (var file in Directory.GetFiles(dir, "*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(file, System.Text.Encoding.UTF8);
                        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (dict == null || dict.Count == 0) continue;

                        var code = Path.GetFileNameWithoutExtension(file);
                        if (string.IsNullOrEmpty(code)) continue;

                        _externalLanguages[code] = dict;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LocalizationService] Failed to load language file '{file}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LocalizationService] Failed to load external languages: {ex.Message}");
            }
        }

        /// <summary>
        /// Generates zh-Hans.json and en.json from built-in embedded resources.
        /// When overwrite=false, skips existing files to preserve user modifications.
        /// </summary>
        private static void GenerateBuiltInLanguageFiles(string dir, bool overwrite = false)
        {
            try
            {
                foreach (var kv in BuiltInLanguages)
                {
                    var filePath = Path.Combine(dir, $"{kv.Key}.json");
                    if (!overwrite && File.Exists(filePath)) continue;

                    // Always include _LanguageName first
                    var dict = new Dictionary<string, string>();
                    if (kv.Value.TryGetValue("_LanguageName", out var langName))
                        dict["_LanguageName"] = langName;

                    foreach (var entry in kv.Value)
                    {
                        if (entry.Key != "_LanguageName")
                            dict[entry.Key] = entry.Value;
                    }

                    var json = JsonSerializer.Serialize(dict, JsonOptions);
                    File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
                    System.Diagnostics.Debug.WriteLine($"[Localization] Generated language file: {filePath} ({dict.Count} keys)");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LocalizationService] Failed to generate built-in language files: {ex.Message}");
            }
        }

        // ------------------------------------------------ //
        //  Singleton
        // ------------------------------------------------ //

        private static readonly object _lock = new();
        private static LocalizationService? _instance;
        public static LocalizationService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            var temp = new LocalizationService();
                            _instance = temp;
                        }
                    }
                }
                return _instance;
            }
        }
    }
}
