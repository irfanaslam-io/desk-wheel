# Repository rulesets

GitHub doesn't apply these files automatically; import them once:
**Settings › Rules › Rulesets › New ruleset › Import a ruleset**, and choose each JSON file.

| File | Protects | What it blocks |
| --- | --- | --- |
| `protect-main.json` | the default branch (`main`) | deleting it and force-pushing over its history |
| `protect-release-tags.json` | release tags (`v0.18.0`, …) | deleting or moving a published version's tag |

Normal pushes and new tags still work. Repository admins (you) can bypass both in an emergency.
