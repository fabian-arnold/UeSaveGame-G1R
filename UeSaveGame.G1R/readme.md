# Fixing the Fire Mage "Trial of Fire" softlock by editing your save (Gothic Remake)

**TL;DR:** The Fire Mage questline can softlock if the two Trial of Fire statues get
ignited at the wrong time. You can repair it by opening your `.sav` file and setting the
two statue objective quests to `Succeeded`. Here's the exact tool and steps I used.

---

## ⚠️ Read this first

- **Back up your save before touching anything.** Copy the `.sav` somewhere safe. If the
  edit goes wrong you restore the backup and you've lost nothing.
- Save editing is at your own risk. It worked for me; your mileage may vary.
- This edits a *single-player* save. Don't do this to anything you care about without a backup.

## What the softlock is

The Trial of Fire involves lighting **two statues**. If they get ignited before the quest
expects it, the Fire Mage path stays locked. The two statue objectives live in your save as
separate quests, and the game checks their state.

## The tool

I used a small open-source command-line tool that can read and rewrite Gothic Remake saves
(they're a custom `GSAV` container with Oodle-compressed data inside, so a normal save editor
won't open them).

**Get it (pick one):**

- **Download** the prebuilt `UeSaveGame.G1R-…-win-x64.zip` from the
  [Releases page](../../releases) and unzip it. No .NET install needed.
- **Or build from source** with the .NET 8 SDK:
  `dotnet build UeSaveGame.G1R/UeSaveGame.G1R.csproj -c Release`.

**One required file:** the tool needs `oo2core_9_win64.dll` ([Oodle](https://github.com/new-world-tools/go-oodle/releases)) next to
`UeSaveGame.G1R.exe`. 

## Step by step (exactly what I did)

**1. Back up the save files.**

Save files are in `%LocalAppData%\G1R\Saved\SaveGames

(Your file will have a different number, e.g. `G1R-017.sav`.)

**2. Copy latest save and patch the quests**

```
> UeSaveGame.G1R.exe set-quest .\G1R-023.sav -o G1R-023-fixed.sav TRIALOFFIRE_OBJ_SEA=Succeeded TRIALOFFIRE_OBJ_WATERFALL=Succeeded                 
  = /Script/Angelscript.Quest_OldCamp_OCCHAPTER3_TRIALOFFIRE_TRIALOFFIRE_OBJ_SEA already EQuestState::Succeeded
  /Script/Angelscript.Quest_OldCamp_OCCHAPTER3_TRIALOFFIRE_TRIALOFFIRE_OBJ_WATERFALL
      EQuestState::Running -> EQuestState::Succeeded

Changed 1 quest(s). Wrote G1R-023-fixed.sav
Verified: reloaded save reflects the changes.
```


**2. Copy fixed file back with original name**