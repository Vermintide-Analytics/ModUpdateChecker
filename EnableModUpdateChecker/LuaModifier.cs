using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
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
        public string? AddUpdateChecker(string modId, bool force)
        {
            var modScriptTempFileName = new FileInfo($"{ModFolder}/{ModScriptName}").Name;
            var localizationScriptTempFileName = new FileInfo($"{ModFolder}/{LocalizationScriptName}").Name;

            if(!force && (File.Exists($"{TempFolder}/{modScriptTempFileName}") || File.Exists($"{TempFolder}/{localizationScriptTempFileName}")))
            {
                return $"EnableModUpdateChecker did not properly clean up last time it was run. Please remove auto-generated code from the end of these files, then run this command again with -force\n{modScriptTempFileName}\n{localizationScriptTempFileName}";
            }

            // Backup scripts to our temp folder
            File.Copy($"{ModFolder}/{ModScriptName}", $"{TempFolder}/{modScriptTempFileName}", true);
            File.Copy($"{ModFolder}/{LocalizationScriptName}", $"{TempFolder}/{localizationScriptTempFileName}", true);

            // Modify lua in-place
            var modScriptFailureReason = AddModScriptCode(modId);
            if(modScriptFailureReason != null)
            {
                File.Delete($"{TempFolder}/{modScriptTempFileName}");
                File.Delete($"{TempFolder}/{localizationScriptTempFileName}");

                return $"Failed to add lua to {ModFolder}/{ModScriptName}. Reason: {modScriptFailureReason}";
            }
            var localizationFailureReason = AddLocalizationScriptCode();
            if(localizationFailureReason != null)
            {
                // If we get here, we're aborting but we already modified the other lua file, so restore it
                string restoreFailureReason = MoveFile($"{TempFolder}/{modScriptTempFileName}", $"{ModFolder}/{ModScriptName}", true);
                if(restoreFailureReason != null)
                {
                    localizationFailureReason += $"\nWARNING: {ModFolder}/{ModScriptName} WAS MODIFIED AND THE CHANGES COULD NOT BE REVERTED. MANUAL ACTION REQUIRED.";
                }
                File.Delete($"{TempFolder}/{localizationScriptTempFileName}");
                return $"Failed to add lua to {ModFolder}/{LocalizationScriptName}. Reason: {localizationFailureReason}";
            }

            return Success;
        }

        public string? RestoreOriginalLua()
        {
            var modScriptTempFileName = new FileInfo($"{ModFolder}/{ModScriptName}").Name;
            var localizationScriptTempFileName = new FileInfo($"{ModFolder}/{LocalizationScriptName}").Name;

            // Restore scripts from our temp folder
            var modScriptFailureReason = MoveFile($"{TempFolder}/{modScriptTempFileName}", $"{ModFolder}/{ModScriptName}", true);
            var localizationScriptFailureReason = MoveFile($"{TempFolder}/{localizationScriptTempFileName}", $"{ModFolder}/{LocalizationScriptName}", true);

            var failureReason = "";
            if(modScriptFailureReason != null)
            {
                failureReason += modScriptFailureReason;
            }
            if(localizationScriptFailureReason != null)
            {
                failureReason += (failureReason.Length > 0 ? "\n" : "") + localizationScriptFailureReason;
            }
            if(failureReason.Length == 0)
            {
                failureReason = null;
            }
            return failureReason;
        }
        #endregion

        #region Private
        private string? AddModScriptCode(string modId) =>
            AppendToFile($"{ModFolder}/{ModScriptName}", $"\n\n{GenerateModScriptLua(modId, GetModVariableName())}");

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
            { "en", "Could not verify that you have the latest version of %s. Is it public on Steam?" },
            { "es", "No se pudo verificar que tienes la última versión de %s. ¿Es público en Steam?" },
            { "fr", "Impossible de vérifier que vous disposez de la dernière version de %s. C'est public sur Steam ?" },
            { "de", "Es konnte nicht überprüft werden, ob Sie über die neueste Version von %s verfügen. Ist es auf Steam öffentlich?" },
            { "zh", "无法验证您是否拥有最新版本的 %s。 steam上是公开的吗？" },
        };

        private static readonly Dictionary<string, string> OutOfDateLocalizations = new Dictionary<string, string>()
        {
            { "en", "NOTICE: You are not using the latest version of %s." },
            { "es", "AVISO: No estás usando la última versión de %s" },
            { "fr", "AVIS : Vous n'utilisez pas la dernière version de %s" },
            { "de", "HINWEIS: Sie verwenden nicht die neueste Version von %s" },
            { "zh", "注意：您没有使用最新版本的 %s" },
        };

        private string? AddLocalizationScriptCode()
        {
            string localizationText = File.ReadAllText($"{ModFolder}/{LocalizationScriptName}");
            var namedReturnPattern = new Regex("return\\s+(\\w+)\\s*$", RegexOptions.RightToLeft);
            var namedReturnMatch = namedReturnPattern.Match(localizationText);
            int startingIndex;
            if (namedReturnMatch.Success)
            {
                var returnVarName = namedReturnMatch.Groups[1].Value;
                var namedTablePattern = new Regex($"{returnVarName}\\s*=\\s*{{", RegexOptions.RightToLeft);
                var namedTableMatch = namedTablePattern.Match(localizationText);
                if (namedTableMatch.Success)
                {
                    startingIndex = localizationText.LastIndexOf(namedTableMatch.Value) + namedTableMatch.Value.Length;
                }
                else
                {
                    return $"Found \"return {returnVarName}\" but could not find corresponding Localization table definition";
                }
            }
            else
            {
                var tableReturnPattern = new Regex("return\\s*{", RegexOptions.RightToLeft);
                var tableReturnMatch = tableReturnPattern.Match(localizationText);
                if (!tableReturnMatch.Success)
                {
                    return "Could not figure out how Localization table is returned";
                }

                startingIndex = localizationText.LastIndexOf(tableReturnMatch.Value) + tableReturnMatch.Value.Length;
            }

            // Walk forwards in the text to find the final } of the localization table defintion. We will insert before that
            var forwardWalkIndex = startingIndex;
            var depth = 1;
            var isInString = false;
            for(;forwardWalkIndex < localizationText.Length && depth > 0; forwardWalkIndex++)
            {
                switch(localizationText[forwardWalkIndex])
                {
                    case '"':
                        isInString = !isInString;
                        break;
                    case '{':
                        if (!isInString) depth++;
                        break;
                    case '}':
                        if (!isInString) depth--;
                        break;
                }
            }
            if (forwardWalkIndex >= localizationText.Length && depth > 0)
            {
                return "Could not find end of Localization table";
            }

            // Do a quick walk backwards to figure out if we need to insert a comma before our new entries
            var prependWithComma = true;
            var doneBackwardsWalk = false;
            for(int backwardWalkIndex = forwardWalkIndex - 2; backwardWalkIndex >= startingIndex && !doneBackwardsWalk; backwardWalkIndex--)
            {
                switch(localizationText[backwardWalkIndex])
                {
                    case ',': // Comma found
                    case '{': // No comma, but no previous entries for which we would need a comma either
                        prependWithComma = false;
                        doneBackwardsWalk = true;
                        break;
                    case '}': // No comma found
                        doneBackwardsWalk = true;
                        break;
                }
            }

            return WriteToFile($"{ModFolder}/{LocalizationScriptName}", localizationText.Insert(forwardWalkIndex - 1, GenerateLocalizationScriptLua(prependWithComma)));
        }
        private string GenerateLocalizationScriptLua(bool prependWithComma)
        {
            var comma = prependWithComma ? "," : "";

            string output = "\t" + HEADER_BEGIN + "\n";
            output += $"\t{comma}MUC_fail = {{\n";
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

        private static string? WriteToFile(string path, string content)
        {
            try
            {
                File.WriteAllText(path, content);
            }
            catch(Exception ex)
            {
                return ex switch
                {
                    PathTooLongException => "File path too long",
                    DirectoryNotFoundException => "Part of path not found",
                    IOException => "Failed to write to file",
                    UnauthorizedAccessException => "Do not have permission to write to file",
                    _ => ex.Message,
                };
            }
            return Success;
        }
        private static string? AppendToFile(string path, string content)
        {
            try
            {
                File.AppendAllText(path, content);
            }
            catch (Exception ex)
            {
                return ex switch
                {
                    PathTooLongException => "File path too long",
                    DirectoryNotFoundException => "Part of path not found",
                    IOException => "Failed to write to file",
                    UnauthorizedAccessException => "Do not have permission to write to file",
                    _ => ex.Message,
                };
            }
            return Success;
        }

        private static string? MoveFile(string source, string dest, bool overwrite)
        {
            try
            {
                File.Move(source, dest, overwrite);
            }
            catch (Exception ex)
            {
                return ex switch
                {
                    FileNotFoundException => "File not found",
                    PathTooLongException => "File path too long",
                    DirectoryNotFoundException => "Part of path not found",
                    IOException => "Failed to write to file",
                    UnauthorizedAccessException => "Do not have permission to write to file",
                    _ => ex.Message,
                };
            }
            return Success;
        }

        private static string? Success => null;
        #endregion
    }
}
