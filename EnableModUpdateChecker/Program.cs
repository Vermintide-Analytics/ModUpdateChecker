using System.Text.RegularExpressions;

namespace EnableModUpdateChecker
{
    internal class Program
    {
        static string ModFolder { get; set; }
        static string TempFolder { get; set; }
        static string BackupFolder { get; set; }

        static async Task<int> Main(string[] args)
        {
            var modName = args[0];
            var cleanup = args.Contains("-cleanup");
            var force = args.Contains("-force");

			var noChat = args.Contains("-nochat");
			var alwaysChat = args.Contains("-alwayschat");

            if(noChat && alwaysChat)
            {
                Console.Error.WriteLine("You cannot provide both -nochat and -alwayschat flags. MUC is not making any changes!");
                return 1;
            }

			var vmbFolder = GetVmbFolderPath();
            if(vmbFolder is null)
            {
                Console.Error.WriteLine("Could not find VMB folder.");
                return 1;
            }

            ModFolder = $"{Environment.CurrentDirectory}/mods/{modName}";
            TempFolder = Directory.CreateDirectory($"{vmbFolder}/.muctemp").FullName;
            BackupFolder = Directory.CreateDirectory($"{vmbFolder}/MUC_Backup").FullName;

            var modFile = $"{ModFolder}/{modName}.mod";
            if (!File.Exists(modFile))
            {
                Console.Error.WriteLine($"Could not locate {modFile}");
                return 1;
            }

            var modFileText = File.ReadAllText(modFile);
            var modScriptPathPattern = new Regex("mod_script\\s*=\\s*\"([\\w\\s/]+)\"\\s*,");
            var localizationScriptPathPattern = new Regex("mod_localization\\s*=\\s*\"([\\w\\s/]+)\"\\s*,");

            var modScriptPathMatch = modScriptPathPattern.Match(modFileText);
            var localizationScriptPathMatch = localizationScriptPathPattern.Match(modFileText);

            if(!modScriptPathMatch.Success || !localizationScriptPathMatch.Success)
            {
                Console.Error.WriteLine($"Could not read script file paths from {modFile}");
                return 1;
            }

            var modScriptFileName = modScriptPathMatch.Groups[1] + ".lua";
            var localizationScriptFileName = localizationScriptPathMatch.Groups[1] + ".lua";

            var luaMod = new LuaModifier(modName, ModFolder, TempFolder, BackupFolder, modScriptFileName, localizationScriptFileName);

            if(cleanup)
            {
                var failureReason = luaMod.RestoreOriginalLua();
                if(failureReason != null)
                {
                    Console.Error.WriteLine($"FAILED TO REMOVE MOD UPDATE CHECKER CODE, PLEASE TAKE MANUAL ACTION:\n{failureReason}");
                }
                else
                {
                    Console.WriteLine("ModUpdateChecker code removed from " + modName);
                }
            }
            else
            {
                var modId = GetModId();
                if (modId is null)
                {
                    Console.Error.WriteLine("Could not read mod ID from itemV2.cfg");
                    return 1;
                }
                var failureReason = luaMod.AddUpdateChecker(modId, force, noChat, alwaysChat);
                if(failureReason != null)
                {
                    Console.Error.WriteLine($"FAILED TO ADD MOD UPDATE CHECKER CODE:\n{failureReason}");
                }
                else
                {
                    Console.WriteLine("ModUpdateChecker code added to " + modName);
                }
            }

            return 0;
        }

        // Search the current directory and upwards for the folder containing .vmbrc
        static string? GetVmbFolderPath()
        {
            DirectoryInfo? dir = new DirectoryInfo(Environment.CurrentDirectory);
            while(dir != null && !File.Exists($"{dir.FullName}/.vmbrc"))
            {
                dir = dir.Parent;
            }
            return dir?.FullName;
        }

        static string? GetModId()
        {
            var itemV2cfg = $"{ModFolder}/itemV2.cfg";
            if (!File.Exists(itemV2cfg))
            {
                return null;
            }
            foreach(var line in File.ReadLines(itemV2cfg))
            {
                if(line.StartsWith("published_id"))
                {
                    var split = line.Split("=");
                    if (split.Length < 2)
                    {
                        return null;
                    }
                    return split[1].Replace("L", "").Replace(";", "").Trim();
                }
            }

            return null;
        }
    }
}