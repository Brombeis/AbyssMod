# AbyssMod

| Repo | Contains |
|---|---|
| https://github.com/Brombeis/AbyssMod | Plugin C# code |
| https://github.com/Brombeis/dotabyss-translation | Translation dictionaries |

## Configuration (`BepInEx\config\AbyssMod.cfg`)

- `[Translation] Enabled/CDN/Language` — on/off, dictionary source, target language (`en`).
- `[Collector] CollectText` (default on) — logs every untranslated Japanese string to `dump/{category}_raw.json`
- `[Collector] ClassifyText` — auto-sorts UI text into sub-categories; off = everything goes to `ui_misc`.
- `[MachineTranslation]` — optional LLM auto-translate for anything not yet in a dictionary (off by default).

## Hotkeys

`F8` toggle translation · `F9` toggle voice interruption · `F10` reload config.

## Updating translations

**Missing UI text**: play with `CollectText = true`, check `dump/*_raw.json`, fill in English values, add them to the right dictionary in `dotabyss-translation/translations/add-on/`, **recompute the dictionary hash in `manifest/en.json`** (otherwise the plugin won't notice the change), commit + push.

**Skill/ability descriptions**: see the [Technical details](#technical-details) section below for how these work — they use a template format, not hand-written regex.


### Skill/ability translations

The approach differs from XUnity.AutoTranslator's regex approach.

Old:
```
r:"^次回の通常攻撃が【1HIT / 合計(?<n1>...)】ダメージに変化$"=Next normal attack changes to [1 Hit / Total ${n1}] damage
```
New:
```json
{ "次回の通常攻撃が【1HIT / 合計{[n1]}】ダメージに変化": "Next normal attack changes to [1 Hit / Total {[n1]}] damage" }
```

### Build

```powershell
dotnet build -c Release
```

Optionally, `set $env:ABYSS_GAME_DIR = "C:\Path\To\Game\Folder"` to auto-deploy it to the game's own folder.
