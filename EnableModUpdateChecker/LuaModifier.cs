using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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

        string ModScriptPath { get; set; }
        string LocalizationScriptPath { get; set; }
        string ModScriptName { get; set; }
        string LocalizationScriptName { get; set; }

        string TempFolder { get; set; }
        string BackupFolder { get; set; }
        string ModFolder { get; set; }

        public LuaModifier(string modName, string modFolder, string tempFolder, string backupFolder, string modScriptName, string localizationScriptName)
        {
            ModName = modName;
            ModFolder = modFolder;
            TempFolder = tempFolder;
            BackupFolder = backupFolder;

            ModScriptPath = modScriptName;
            LocalizationScriptPath = localizationScriptName;

            ModScriptName = Path.GetFileName($"{ModFolder}/{ModScriptPath}");
            LocalizationScriptName = Path.GetFileName($"{ModFolder}/{LocalizationScriptPath}");
        }

        #region Public
        public string? AddUpdateChecker(string modId, bool force, bool noChat, bool alwaysChat)
        {
            bool alreadyHaveMUCChangesModScript = false;
            bool alreadyHaveMUCChangesLocalizationScript = false;
            foreach(var line in File.ReadLines($"{ModFolder}/{ModScriptPath}"))
            {
                if(line.Contains(HEADER_BEGIN))
                {
                    alreadyHaveMUCChangesModScript = true;
                    break;
                }
            }
            foreach (var line in File.ReadLines($"{ModFolder}/{LocalizationScriptPath}"))
            {
                if (line.Contains(HEADER_BEGIN))
                {
                    alreadyHaveMUCChangesLocalizationScript = true;
                    break;
                }
            }

            if (!force && (alreadyHaveMUCChangesModScript || alreadyHaveMUCChangesLocalizationScript))
            {
                var filesText = alreadyHaveMUCChangesModScript ? $"\n{ModScriptName}" : "";
                filesText += alreadyHaveMUCChangesLocalizationScript ? $"\n{LocalizationScriptName}" : "";
                return $"The files listed below already contain edits from EnableModUpdateChecker. Please remove auto-generated code from the end of these files, or run this command again with -force.{filesText}";
            }

            BackupFile($"{ModFolder}/{ModScriptPath}", false);
            BackupFile($"{ModFolder}/{LocalizationScriptPath}", false);

            // -force will be true for either of these
            var forceCleanupFailureReason = "";
            if (alreadyHaveMUCChangesModScript)
            {
                forceCleanupFailureReason += "\n" + RemoveMUCChanges($"{ModFolder}/{ModScriptPath}", false) ?? "";
            }
            if(alreadyHaveMUCChangesLocalizationScript)
            {
                forceCleanupFailureReason += "\n" + RemoveMUCChanges($"{ModFolder}/{LocalizationScriptPath}", false) ?? "";
            }

            if(!string.IsNullOrWhiteSpace(forceCleanupFailureReason))
            {
                return $"Failed to clean up existing MUC modifications. Reason: {forceCleanupFailureReason}";
            }

            // Modify lua in-place
            var modScriptFailureReason = AddModScriptCode(modId, noChat, alwaysChat);
            if(modScriptFailureReason != null)
            {
                return $"Failed to add lua to {ModFolder}/{ModScriptPath}. Reason: {modScriptFailureReason}";
            }
            var localizationFailureReason = AddLocalizationScriptCode(noChat, alwaysChat);
            if(localizationFailureReason != null)
            {
                // If we get here, we're aborting but we already modified the other lua file, so restore it
                var cleanupFailureReason = RemoveMUCChanges($"{ModFolder}/{ModScriptPath}", false);

                if(cleanupFailureReason != null)
                {
                    localizationFailureReason += $"\nWARNING: {ModFolder}/{ModScriptPath} WAS MODIFIED AND THE CHANGES COULD NOT BE REVERTED. MANUAL ACTION REQUIRED.";
                }
                return $"Failed to add lua to {ModFolder}/{LocalizationScriptPath}. Reason: {localizationFailureReason}";
            }

            return Success;
        }

        public string? RestoreOriginalLua()
        {
            var cleanupFailureReason = "";
            cleanupFailureReason += "\n" + RemoveMUCChanges($"{ModFolder}/{ModScriptPath}", false) ?? "";
            cleanupFailureReason += "\n" + RemoveMUCChanges($"{ModFolder}/{LocalizationScriptPath}", true) ?? "";

            if (!string.IsNullOrWhiteSpace(cleanupFailureReason))
            {
                return $"Failed to clean up MUC modifications. Reason: {cleanupFailureReason}";
            }

            return Success;
        }
        #endregion

        #region Private
        private string? BackupFile(string filePath, bool modified) => CopyFile(filePath, GetBackupFilePath(filePath, modified), true);

        private string? GetBackupFilePath(string filePath, bool modified) => $"{BackupFolder}/{Path.GetFileName(filePath).Replace(".lua", "") + (modified ? "_MODIFIED" : "") + ".lua"}";

        private string? AddModScriptCode(string modId, bool noChat, bool alwaysChat)
        {
            var toAppend = $"{GenerateModScriptLua(modId, GetModVariableName(), noChat, alwaysChat)}";

            var lastLine = File.ReadLines($"{ModFolder}/{ModScriptPath}").LastOrDefault();
            if (lastLine is null)
            {
                return $"Could not determine if {ModScriptPath} alread has some empty space at the end of the file.";
            }
            if(!string.IsNullOrWhiteSpace(lastLine))
            {
                toAppend = "\n" + toAppend;
            }

            return AppendToFile($"{ModFolder}/{ModScriptPath}", toAppend);
        }

		private static string GenerateModScriptLua(string modId, string modVarName, bool noChat, bool alwaysChat)
		{
			string output = HEADER_BEGIN + "\n";

			var upload = DateTime.UtcNow.AddMinutes(2);

			var uploadDateTimeString = $"{upload.Year},{upload.Month},{upload.Day},{upload.Hour},{upload.Minute}";

			var chatOutput = @"
	    if not %MOD_VAR_NAME%.up_to_date then
		    %MOD_VAR_NAME%:echo(%MOD_VAR_NAME%:localize(""MUC_out_of_date"", %MOD_VAR_NAME%:get_readable_name()))
	    end";
			var onFail = "%MOD_VAR_NAME%:echo(%MOD_VAR_NAME%:localize(\"MUC_fail\", %MOD_VAR_NAME%:get_readable_name()))";

			if (noChat)
			{
				chatOutput = "";
				onFail = "";
			}
			else if (alwaysChat)
			{
				chatOutput = @"
	    if not %MOD_VAR_NAME%.up_to_date then
		    %MOD_VAR_NAME%:echo(%MOD_VAR_NAME%:localize(""MUC_out_of_date"", %MOD_VAR_NAME%:get_readable_name()))
	    else
            %MOD_VAR_NAME%:echo(%MOD_VAR_NAME%:localize(""MUC_up_to_date"", %MOD_VAR_NAME%:get_readable_name()))
        end";
			}


			var variables = new Dictionary<string, string>()
			{
				{ "%UPLOAD_DATE_TIME%", uploadDateTimeString },
				{ "%MOD_ID%", modId },
				{ "%MOD_VAR_NAME%", modVarName },
			};

			string code = @"%MOD_VAR_NAME%.up_to_date_callbacks = {}
%MOD_VAR_NAME%.register_MUC_callback = function(callback)
    table.insert(%MOD_VAR_NAME%.up_to_date_callbacks, callback)
end

local vmf = get_mod(""VMF"")
local MUC_verbose_logging = vmf and vmf:get(""developer_mode"")
local MUC_log = function(msg)
    if MUC_verbose_logging then
        %MOD_VAR_NAME%:echo(""[ModUpdateChecker] "" .. msg)
    else
        %MOD_VAR_NAME%:info(""[ModUpdateChecker] "" .. msg)
    end
end
local MUC_format_dt = function(t)
    if not t or not t[1] then return ""(invalid)""
    end
    return string.format(""%04d-%02d-%02d %02d:%02d UTC"", t[1], t[2] or 0, t[3] or 0, t[4] or 0, t[5] or 0)
end
local MUC_compare_message = function(ours, latest, is_up_to_date)
    local steam_dt = MUC_format_dt(latest)
    local ours_dt = MUC_format_dt(ours)
    if not is_up_to_date then
        return string.format(""The most recent update from the steam page is %s which is newer than this version's build time %s (mod is out of date)."", steam_dt, ours_dt)
    end
    local same = true
    for i = 1, 5 do
        if ours[i] ~= latest[i] then same = false break end
    end
    if same then
        return string.format(""The most recent update from the steam page is %s which is the same as this version's build time %s (mod is up to date)."", steam_dt, ours_dt)
    end
    return string.format(""The most recent update from the steam page is %s which is older than this version's build time %s (mod is up to date)."", steam_dt, ours_dt)
end

local mod_update_check_callback = function(success, code, headers, data, userdata)
    MUC_log(""Update check callback received (success="" .. tostring(success) .. "", HTTP code="" .. tostring(code) .. "")"")
    %MOD_VAR_NAME%:pcall(function()
	    if not data then
            MUC_log(""FAILURE at step 1: curl returned no response body."")
            " + onFail + @"
            return
        end
	    MUC_log(""Step 1 OK: response body length is "" .. #data .. "" bytes."")
	    local first_update_index = data:find(""Update: "")
	    if not first_update_index then
            MUC_log('FAILURE at step 2: response body does not contain the changelog marker ""Update: "".')
            %MOD_VAR_NAME%:echo(%MOD_VAR_NAME%:localize(""MUC_fail"", %MOD_VAR_NAME%:get_readable_name()))
            return
        end
	    MUC_log(""Step 2 OK: found changelog marker at byte index "" .. first_update_index .. ""."")
	    local ours = { %UPLOAD_DATE_TIME% }
	    MUC_log(""This version's hardcoded build time (UTC): "" .. MUC_format_dt(ours))
	    local year_p, no_year_p = ""(%d+)%. (%a+)%.? (%d+) um (%d+):(%d+)"", ""(%d+)%. (%a+)%.? um (%d+):(%d+)""
	    local month_lut = {Jan=1,[""Jän""]=1,Feb=2,[""März""]=3,Mrz=3,Apr=4,Mai=5,Jun=6,Juni=6,Jul=7,Juli=7,Aug=8,Sep=9,Sept=9,Okt=10,Nov=11,Dez=12}
	    local substr = data:sub(first_update_index, first_update_index+30)
	    MUC_log(""Raw substr from HTML (30 chars from marker): "" .. substr)
	    local day, month, year, hour, minute = substr:match(year_p)
	    if not day then
            year, day, month, hour, minute = os.date(""%Y""), substr:match(no_year_p)
            MUC_log(""Parsed with no-year pattern; assumed year from os.date: "" .. tostring(year))
        else
            MUC_log(""Parsed with year-in-substr pattern."")
        end
	    MUC_log(""Parsed values — day="" .. tostring(day) .. "", month="" .. tostring(month) .. "", year="" .. tostring(year) .. "", hour="" .. tostring(hour) .. "", minute="" .. tostring(minute))
	    local month_num = month_lut[month]
	    if not day or not month_num or not hour or not minute then
            MUC_log(""FAILURE at step 3: could not parse a complete date/time from substr (month_lut lookup: "" .. tostring(month_num) .. "")."")
            %MOD_VAR_NAME%:echo(%MOD_VAR_NAME%:localize(""MUC_fail"", %MOD_VAR_NAME%:get_readable_name()))
            return
        end
	    local latest = { tonumber(year), month_num, tonumber(day), tonumber(hour), tonumber(minute) }
	    if not latest[1] or not latest[3] or not latest[4] or not latest[5] then
            MUC_log(""FAILURE at step 3: tonumber failed on parsed date/time components."")
            %MOD_VAR_NAME%:echo(%MOD_VAR_NAME%:localize(""MUC_fail"", %MOD_VAR_NAME%:get_readable_name()))
            return
        end
	    MUC_log(""Step 3 OK: latest Steam update time (UTC): "" .. MUC_format_dt(latest))
	    local MUC_get_up_to_date = function(table_ours, table_latest)
		    for i = 1, 5 do if table_ours[i] > table_latest[i] then return true elseif table_ours[i] < table_latest[i] then return false end end
		    return true
	    end
	    %MOD_VAR_NAME%.up_to_date = MUC_get_up_to_date(ours, latest)
	    MUC_log(MUC_compare_message(ours, latest, %MOD_VAR_NAME%.up_to_date))
	    MUC_log(""Comparison result: up_to_date="" .. tostring(%MOD_VAR_NAME%.up_to_date))" + chatOutput + @"
        for _, cb in ipairs(%MOD_VAR_NAME%.up_to_date_callbacks) do
            cb(%MOD_VAR_NAME%.up_to_date)
        end
	end)
end
%MOD_VAR_NAME%.MUC_check_for_update = function()
    MUC_log(""Starting update check for workshop item %MOD_ID%."")
    Managers.curl:get(""https://steamcommunity.com/sharedfiles/filedetails/changelog/%MOD_ID%"", {""Accept-Language: de;q=0.5"", ""Cookie: timezoneOffset=0,0""}, mod_update_check_callback)
end
%MOD_VAR_NAME%.MUC_check_for_update()
";

			foreach (var kvp in variables)
			{
				code = code.Replace(kvp.Key, kvp.Value);
			}
			output += code;

			output += "\n" + HEADER_END;
			return output;
		}

		private string? GetModVariableName()
        {
            var pattern = new Regex($"\\s*local\\s+(\\w+)\\s*=\\s*get_mod\\(\"{ModName}\"\\)");
            foreach(var line in File.ReadLines($"{ModFolder}/{ModScriptPath}"))
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


		private static readonly Dictionary<string, string> UpToDateLocalizations = new Dictionary<string, string>()
		{
			{ "en", "You have the latest version of %s." },
			{ "es", "Tienes la última versión del %s" },
			{ "fr", "Vous avez la dernière version du %s" },
			{ "de", "Sie haben die neueste Version von %s" },
			{ "zh", "你有最新版本的 %s" },
		};

		private string? AddLocalizationScriptCode(bool noChat, bool alwaysChat)
        {
            string localizationText = File.ReadAllText($"{ModFolder}/{LocalizationScriptPath}");
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

            // Walk forwards in the text to find the closing } of the localization table defintion. We will insert before that
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
            var nonWhitespacePattern = new Regex("[^\\s]");
            var foundNonWhitespace = false;
            var needNewLine = true;
            for (int backwardWalkIndex = forwardWalkIndex - 2; backwardWalkIndex >= startingIndex && !doneBackwardsWalk; backwardWalkIndex--)
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
                    case '\n' when !foundNonWhitespace:
                        needNewLine = false;
                        break;
                    case var character when nonWhitespacePattern.IsMatch($"{character}"):
                        foundNonWhitespace = true;
                        break;
                }
            }

            return WriteToFile($"{ModFolder}/{LocalizationScriptPath}", localizationText.Insert(forwardWalkIndex - 1, GenerateLocalizationScriptLua(prependWithComma, needNewLine, noChat, alwaysChat)));
        }
        private string GenerateLocalizationScriptLua(bool prependWithComma, bool prependWithNewline, bool noChat, bool alwaysChat)
        {
            var newLine = prependWithNewline ? "\n" : "";
            var comma = prependWithComma ? "," : "";

            string output = $"{newLine}\t" + HEADER_BEGIN + "\n";
            output += $"\t{comma}MUC_fail = {{\n";
            foreach(var kvp in FailLocalizations)
            {
                output += $"\t\t{kvp.Key} = \"{kvp.Value}\",\n";
            }
            if(!noChat)
			{
				output += "\t},\n\tMUC_out_of_date = {\n";
				foreach (var kvp in OutOfDateLocalizations)
				{
					output += $"\t\t{kvp.Key} = \"{kvp.Value}\",\n";
				}
				if (alwaysChat)
				{
					output += "\t},\n\tMUC_up_to_date = {\n";
					foreach (var kvp in UpToDateLocalizations)
					{
						output += $"\t\t{kvp.Key} = \"{kvp.Value}\",\n";
					}
				}
			}
			output += "\t},\n\t" + HEADER_END + "\n";
            return output;
        }

        // This function assumes that the MUC code has its own lines, which is a risk of data loss if the user modifies it.
        private string? RemoveMUCChanges(string filePath, bool preserveEmptyLines)
        {
            BackupFile(filePath, true);

            var fileName = Path.GetFileName(filePath);
            var tempFilePath = $"{TempFolder}/{fileName}";
            
            {
                using var reader = new StreamReader(filePath);
                using var writer = new StreamWriter(tempFilePath);
                bool isMUCCode = false;
                bool detectedAnyMUCCode = false;
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (line.Contains(HEADER_BEGIN))
                    {
                        isMUCCode = true;
                        detectedAnyMUCCode = true;
                        // Special case, if for some reason there is content before the header comment, assume it's the user's and don't remove it
                        var beforeHeader = line.Substring(0, line.IndexOf(HEADER_BEGIN));
                        if(!string.IsNullOrWhiteSpace(beforeHeader))
                        {
                            writer.WriteLine(beforeHeader);
                        }
                    }
                    if (!isMUCCode && (!string.IsNullOrWhiteSpace(line) || !detectedAnyMUCCode || preserveEmptyLines))
                    {
                        writer.WriteLine(line);
                    }
                    if (line.Contains(HEADER_END))
                    {
                        if (!isMUCCode)
                        {
                            return $"Detected END of MUC code without a BEGINNING in {filePath}";
                        }
                        isMUCCode = false;
                    }
                }

                // If we get to the file without seeing a final HEADER_END, consider that an error and don't go forward
                if (isMUCCode)
                {
                    return $"Detected BEGINNING of MUC code without an END in {filePath}";
                }

                if(!detectedAnyMUCCode)
                {
                    // Nothing to do, consider it a success
                    return Success;
                }

                writer.Flush();
            }

            return MoveFile(tempFilePath, filePath, true);
        }

        private static string? CopyFile(string source, string destination, bool overwrite)
        {
            try
            {
                File.Copy(source, destination, overwrite);
            }
            catch (Exception ex)
            {
                return ex switch
                {
                    PathTooLongException => "File path too long",
                    DirectoryNotFoundException => "Part of path not found",
                    FileNotFoundException => "File not found",
                    IOException => "Failed to write to file",
                    UnauthorizedAccessException => "Do not have permission to write to file",
                    _ => ex.Message,
                };
            }
            return Success;
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
