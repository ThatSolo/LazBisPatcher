[Download LazBisPatcher.exe for Windows](https://github.com/ThatSolo/LazBisPatcher/releases/latest/download/LazBisPatcher.exe)

Installs, updates, or repairs [LazBis](https://github.com/ThatSolo/lazbis) in your MacroQuest folder.

## Download for Windows

1. Save `LazBisPatcher.exe` in a writable folder of your choice.
2. Run it. Click **Browse** and select the MacroQuest folder that contains `lua` and `config`.
3. Follow the install or update prompt and review the changes before confirming.

No ZIP extraction or separate helper download is needed. The EXE is not digitally signed.

## Install or update LazBis

The patcher checks for updates when it opens. You can also click **Check for Updates** at any time.

- **Install LazBis** sets up a new copy.
- **Update LazBis** installs a newer LazBis version.
- **Review existing LazBis** checks an unrecorded copy. Recognized official 1.0 files can upgrade directly. Other managed-file differences block installation and stay in place.
- **Verify / repair LazBis** checks your installed copy and can restore missing files.

MacroQuest can stay open. Before applying an update or moving an old script folder:

- Run `/lua stop lazbis` on every character running it.
- If that copy runs the background helper, also run `/lua stop lazbis/bg` on those characters.

Review the summary, then confirm. Expand **File details** to see what will change.

Once LazBis is installed, start it in MacroQuest with `/lua run lazbis`.

## Switch from another LazBis copy

If **Existing LazBis copy found** blocks installation, the patcher leaves that copy and its extra files in place.

1. Stop the scripts using the commands above.
2. Back up the entire old script folder and its data.
3. Move `lua\lazbis` outside `lua`.
4. Click **Check for Updates**, then install ThatSolo's LazBis.

The patcher does not move folders, merge fork files or migrate data. Moving the script folder alone does not isolate shared files such as `resources\lazbis.db` or `config\lazbis_lockouts.lua`. If database or settings compatibility is unknown, use a separate MacroQuest test copy with its own data.

## Update the patcher

When a new LazBisPatcher version is available, **Update Patcher** appears near the top. Follow the prompt and let the patcher close and reopen.
