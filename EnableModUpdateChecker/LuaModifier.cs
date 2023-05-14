using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EnableModUpdateChecker
{
    internal class LuaModifier
    {
        const string HEADER_BEGIN   = "-- DO NOT MODIFY ::: BEGIN Auto-Generated Mod Update-Checker";
        const string HEADER_END = "-- DO NOT MODIFY ::: END Auto-Generated Mod Update-Checker";

        string ModName { get; set; }

        string ModScriptName { get; set; }
        string LocalizationScriptName { get; set; }

        string TempFolder { get; set; }
        string ModFolder { get; set; }

        public LuaModifier(string modName, string modFolder, string tempFolder, string modScriptName, string localizationScriptName)
        {
            ModName = modName;
            ModFolder = modFolder;
            TempFolder = tempFolder;
            ModScriptName = modScriptName;
            LocalizationScriptName = localizationScriptName;
        }

        #region Public
        public void AddUpdateChecker(string modId, bool force)
        {
            var modScriptTempFileName = new FileInfo($"{ModFolder}/{ModScriptName}").Name;
            var localizationScriptTempFileName = new FileInfo($"{ModFolder}/{LocalizationScriptName}").Name;

            if(!force && (File.Exists($"{TempFolder}/{modScriptTempFileName}") || File.Exists($"{TempFolder}/{localizationScriptTempFileName}")))
            {
                Console.Error.WriteLine($"EnableModUpdateChecker did not properly clean up last time it was run. Please remove auto-generated code from the end of these files, then run this command again with -force\n{modScriptTempFileName}\n{localizationScriptTempFileName}");
                return;
            }

            // Backup scripts to our temp folder
            File.Copy($"{ModFolder}/{ModScriptName}", $"{TempFolder}/{modScriptTempFileName}", true);
            File.Copy($"{ModFolder}/{LocalizationScriptName}", $"{TempFolder}/{localizationScriptTempFileName}", true);

            // Modify lua in-place
            AddModScriptCode(modId);
            AddLocalizationScriptCode();
        }

        public void RestoreOriginalLua()
        {
            var modScriptTempFileName = new FileInfo($"{ModFolder}/{ModScriptName}").Name;
            var localizationScriptTempFileName = new FileInfo($"{ModFolder}/{LocalizationScriptName}").Name;

            // Restore scripts from our temp folder
            File.Move($"{TempFolder}/{modScriptTempFileName}", $"{ModFolder}/{ModScriptName}", true);
            File.Move($"{TempFolder}/{localizationScriptTempFileName}", $"{ModFolder}/{LocalizationScriptName}", true);
        }
        #endregion

        #region Private
        private void AddModScriptCode(string modId)
        {
            string modText = File.ReadAllText($"{ModFolder}/{ModScriptName}") + "\n\n";

            var replacementPattern = new Regex("$");
            var insertion = GenerateModScriptLua(modId, GetModVariableName());

            var replaced = replacementPattern.Replace(modText, insertion, 1);

            File.WriteAllText($"{ModFolder}/{ModScriptName}", replaced);
        }
        private string GenerateModScriptLua(string modId, string modVarName)
        {
            string output = HEADER_BEGIN + "\n";

            var upload = DateTime.UtcNow.AddMinutes(2);

            var uploadDateTimeString = $"{upload.Year},{upload.Month},{upload.Day},{upload.Hour},{upload.Minute}";

            var variables = new Dictionary<string, string>()
            {
                { "%UPLOAD_DATE_TIME%", uploadDateTimeString },
                { "%MOD_ID%", modId },
                { "%MOD_VAR_NAME%", modVarName },
            };

            string code = @"local mod_update_check_callback = function(success, code, headers, data, userdata)
	if not data then %MOD_VAR_NAME%:echo(%MOD_VAR_NAME%:localize(""MUC_fail"", %MOD_VAR_NAME%:get_readable_name())) return end
	local first_update_index = data:find(""Update: "")
	if not first_update_index then %MOD_VAR_NAME%:echo(%MOD_VAR_NAME%:localize(""MUC_fail"", %MOD_VAR_NAME%:get_readable_name())) return end
	local ours = { %UPLOAD_DATE_TIME% }
	local year_p, no_year_p = ""(%d+)%. (%a+)%.? (%d+) um (%d+):(%d+)"", ""(%d+)%. (%a+)%.? um (%d+):(%d+)""
	local month_lut = {Jan=1,Feb=2,[""März""]=3,Apr=4,Mai=5,Jun=6,Jul=7,Aug=8,Sep=9,Okt=10,Nov=11,Dez=12}
	local substr = data:sub(first_update_index, first_update_index+30)
	local day, month, year, hour, minute = substr:match(year_p)
	if not day then year, day, month, hour, minute = os.date(""%Y""), substr:match(no_year_p) end
	local latest = { tonumber(year),month_lut[month],tonumber(day),tonumber(hour),tonumber(minute) }
	local MUC_get_up_to_date = function(table_ours, table_latest)
		for i = 1, 5 do if table_ours[i] > table_latest[i] then return true elseif table_ours[i] < table_latest[i] then return false end end
		return true
	end
	%MOD_VAR_NAME%.up_to_date = MUC_get_up_to_date(ours, latest)
	if not %MOD_VAR_NAME%.up_to_date then
		%MOD_VAR_NAME%:echo(%MOD_VAR_NAME%:localize(""MUC_out_of_date"", %MOD_VAR_NAME%:get_readable_name()))
	end
end
Managers.curl:get(""https://steamcommunity.com/sharedfiles/filedetails/changelog/%MOD_ID%"", {""Accept-Language: de;q=0.5""}, mod_update_check_callback)";

            foreach(var kvp in variables)
            {
                code = code.Replace(kvp.Key, kvp.Value);
            }
            output += code;

            output += "\n" + HEADER_END + "\n";
            return output;
        }

        private string? GetModVariableName()
        {
            var pattern = new Regex($"\\s*local\\s+(\\w+)\\s*=\\s*get_mod\\(\"{ModName}\"\\)");
            foreach(var line in File.ReadLines($"{ModFolder}/{ModScriptName}"))
            {
                var match = pattern.Match(line);
                if(match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
            return null;
        }

        private static readonly Dictionary<string, string> FailLocalizations = new Dictionary<string, string>()
        {
            { "en", "Could not verify that you have the latest version of %s. Is it public on Steam?" }
        };

        private static readonly Dictionary<string, string> OutOfDateLocalizations = new Dictionary<string, string>()
        {
            { "en", "You do not have the latest version of %s." }
        };

        private void AddLocalizationScriptCode()
        {
            string localizationText = File.ReadAllText($"{ModFolder}/{LocalizationScriptName}");

            var detectTrailingCommaPattern = new Regex(",\\s*}\\s*$");
            var prependWithComma = !detectTrailingCommaPattern.Match(localizationText).Success;

            var replacementPattern = new Regex("(}\\s*)$");
            var insertion = (prependWithComma ? "," : "") + GenerateLocalizationScriptLua() + "}";

            File.WriteAllText($"{ModFolder}/{LocalizationScriptName}", replacementPattern.Replace(localizationText, insertion));
        }
        private string GenerateLocalizationScriptLua()
        {
            string output = "\t" + HEADER_BEGIN + "\n";
            output += "\tMUC_fail = {\n";
            foreach(var kvp in FailLocalizations)
            {
                output += $"\t\t{kvp.Key} = \"{kvp.Value}\",\n";
            }
            output += "\t},\n\tMUC_out_of_date = {\n";
            foreach(var kvp in OutOfDateLocalizations)
            {
                output += $"\t\t{kvp.Key} = \"{kvp.Value}\",\n";
            }
            output += "\t},\n\t" + HEADER_END + "\n";
            return output;
        }
        #endregion
    }
}
