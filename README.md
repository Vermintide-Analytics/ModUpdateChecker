# ModUpdateChecker
 
This tool gives Vermintide modders a standardized way to tell users if they do not have the most recent version of their mod. It will automatically append the version-checking code when you upload your mod, then immediately remove it after the upload is complete, so your code does not get more cluttered.

## How to use

1. Download `EnabledModUpdateChecker.exe` and the archive of batch scripts from the latest release at https://github.com/Vermintide-Analytics/ModUpdateChecker/releases
2. Place the executable and unzip the batch scripts to the same folder as Vermintide Mod Builder (VMB).
3. Run one of those scripts the same way you would the original `_Upload Mod` scripts.

If you think the tool has incorrectly modified your files, you can find backups in the `MUC_Backups` folder next to the executable. You will find your original un-modified lua there, and you can also see what code was actually uploaded by looking at the `_MODIFIED` files.

## Details
`EnableModUpdateChecker.exe` is meant to be used along with VMB. This is accomplished with the included batch files, but you can also write your own scripts following the steps below. 

Execute this BEFORE uploading the mod with VMB:
`EnableModUpdateChecker.exe "Name of Mod"`

This will:
1. Create a backup of your mod's primary and localization lua files
2. Appends update-checking logic to the very end of your main lua file
3. Adds some localization definitions to the end of the localization table

Execute this AFTER uploading the mod with VMB:
`EnableModUpdateChecker.exe "Name of Mod" -cleanup`

This will remove the code that was just added to your mod's primary and localization lua files.

**Warning: Modifying the code that this tool inserts can lead to it removing content other than what it added. If you do this, you can use the backups kept in `MUC_Backups` to recover your data.**
