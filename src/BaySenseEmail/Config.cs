using System.Collections.Generic;
using System.IO;

namespace BaySenseSystems.BaySenseEmail
{
    /// <summary>
    /// Simple INI config reader. No external dependencies.
    /// </summary>
    public class Config
    {
        private readonly Dictionary<string, Dictionary<string, string>> _sections =
            new Dictionary<string, Dictionary<string, string>>();

        public string ShopName => Get("shop", "name");
        public string ShopPhone => Get("shop", "phone");
        public string SubjectTemplate => Get("email", "subject_template", "{document_type} #{ro_number}");
        public string BodyTemplate => Get("email", "body_template", "Please find attached your {document_type_lower}.");
        public string ThunderbirdPath => Get("thunderbird", "path");
        public string ApiBaseUrl => Get("api", "base_url");
        public bool ApiEnabled => Get("api", "enabled", "false").ToLower() == "true";
        public string LogFile => Get("logging", "file", "baysense_email.log");
        public string LogLevel => Get("logging", "level", "Info");

        public static Config Load(string path)
        {
            var config = new Config();
            if (!File.Exists(path)) return config;

            string currentSection = "";
            string currentKey = null;
            var currentValue = new System.Text.StringBuilder();

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.TrimEnd();

                // Section header
                if (line.StartsWith("[") && line.Contains("]"))
                {
                    FlushMultiline(config, currentSection, ref currentKey, currentValue);
                    currentSection = line.Trim('[', ']', ' ').ToLower();
                    if (!config._sections.ContainsKey(currentSection))
                        config._sections[currentSection] = new Dictionary<string, string>();
                    continue;
                }

                // Comment or empty line
                if (line.TrimStart().StartsWith("#") || line.TrimStart().StartsWith(";"))
                    continue;

                // Key = value (new key starts at column 0, not indented)
                if (!line.StartsWith(" ") && !line.StartsWith("\t") && line.Contains("="))
                {
                    FlushMultiline(config, currentSection, ref currentKey, currentValue);

                    var eqIndex = line.IndexOf('=');
                    currentKey = line.Substring(0, eqIndex).Trim().ToLower();
                    var value = line.Substring(eqIndex + 1).Trim();
                    currentValue.Clear();
                    currentValue.Append(value);
                    continue;
                }

                // Continuation line (indented) — append to current value
                if (currentKey != null && (line.StartsWith(" ") || line.StartsWith("\t")))
                {
                    currentValue.AppendLine();
                    currentValue.Append(line.TrimStart());
                    continue;
                }

                // Blank line inside multiline value
                if (currentKey != null && string.IsNullOrWhiteSpace(line))
                {
                    currentValue.AppendLine();
                }
            }

            FlushMultiline(config, currentSection, ref currentKey, currentValue);
            return config;
        }

        private static void FlushMultiline(Config config, string section, ref string key,
            System.Text.StringBuilder value)
        {
            if (key == null || string.IsNullOrEmpty(section)) return;
            if (!config._sections.ContainsKey(section))
                config._sections[section] = new Dictionary<string, string>();
            config._sections[section][key] = value.ToString().Trim();
            key = null;
            value.Clear();
        }

        public string Get(string section, string key, string fallback = "")
        {
            section = section.ToLower();
            key = key.ToLower();
            if (_sections.ContainsKey(section) && _sections[section].ContainsKey(key))
                return _sections[section][key];
            return fallback;
        }
    }
}
