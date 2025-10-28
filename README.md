# Numantics

A [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) mod for [Resonite](https://resonite.com/) that adds math expression evaluation to input fields.

Type math expressions directly into any numeric field and have them automatically calculated when you finish editing!

## Features
- Evaluate expressions in numeric fields (and optionally string fields).
- Supports functions, percentages, powers, and shorthand operator letters.
- Optional rounding mode (rounds final numeric results to nearest integer).
- Evaluate math expressions in input fields:
  - Basic operators: `+`, `-`, `*`, `/`
  - Shorthand operators: `x` → `*`, `d` → `/`, `a` → `+`, `s` → `-`
  - PEMDAS Support: Standard Order of Operations
  - Power operator: `^` (e.g. `6^(2+3)`)
  - Percent support: `50%` is interpreted as `*0.5` (so `3200-50%` → `1600`)
  - Parentheses and precedence supported
- Math functions (input in degrees for trig):  
  `sqrt`, `sin`, `cos`, `tan`, `log`, `log10`, `ln`, `abs`, `floor`, `ceil`
- `pi` constant is supported (case-insensitive)
- Rounding (config-controlled):
  - `round_results` option — when enabled rounds all calculated numeric results to the nearest integer

## Usage Examples
- Type `100+50` in a position field → becomes `150`
- Type `5x3` in a scale field → becomes `15`
- Type `10d2` in any numeric field → becomes `5`
- Type `(2+3)*4` → becomes `20`

## Installation
1. Install [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader).
1. Place [Numantics.dll](https://github.com/nalathethird/R-Numantics/releases/latest/download/Numantics.dll) into your `rml_mods` folder. This folder should be at `C:\Program Files (x86)\Steam\steamapps\common\Resonite\rml_mods` for a default install. You can create it if it's missing, or if you launch the game once with ResoniteModLoader installed it will create this folder for you.
1. Start the game. If you want to verify that the mod is working you can check your Resonite logs.
---

## Configuration
The mod creates/uses a configuration file with the following options:
- `enable_math` (bool, default: `true`)  
  Enable/disable math processing in input fields.
- `include_strings` (bool, default: `false`)  
  Allow math evaluation inside fields whose type is `string`.
- `round_results` (bool, default: `false`)  
  When true, all numeric results are rounded to the nearest integer before being written back to fields.
- `verbose_logging` (bool, default: `false`)  
  Enables detailed logging — recommended while testing field detection and parsing.

**Star this repo if it helped you!** ⭐ It keeps me motivated to maintain and improve my mods.

Or, if you want to go further, Support me on [Ko-fi!](https://ko-fi.com/nalathethird) ☕
It helps me pay bills, and other things someone whos unemployed cant pay!
****

## Links
- [Resonite Modding Group](https://github.com/resonite-modding-group)
