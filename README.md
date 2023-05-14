# ModUpdateChecker
 
This tool gives Vermintide modders a standardized way to tell users if they do not have the most recent version of their mod. It will automatically append the version-checking code when you upload your mod, then immediately remove it after the upload is complete, so your code does not get more cluttered.

## How to use

1. Download `EnabledModUpdateChecker.exe` and whichever batch scripts you want to use and place them both in the same folder as Vermintide Mod Builder (VMB).
2. Then, run one of those scripts the same way you would the original `_Upload Mod` scripts.

## Details
`EnableModUpdateChecker.exe` is meant to be used along with VMB. This is accomplished with the included batch files, but you can also write your own scripts following the steps below. 

Execute this BEFORE uploading the mod with VMB:
`EnableModUpdateChecker.exe "Name of Mod"`

This will:
1. Create a backup of your mod's primary and localization lua files
2. Appends update-checking logic to the very end of your main lua file
3. Adds some localization definitions to the end of the localization lua file

Execute this AFTER uploading the mod with VMB:
`EnableModUpdateChecker.exe "Name of Mod" -cleanup`

This will restore the backups of your mod's primary and localization lua files

**Warning: running this part on its own can lead to data loss if you make changes to your mod after this tool has backed it up**
