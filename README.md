# Numantics

A [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) mod for [Resonite](https://resonite.com/) that adds math expression evaluation to input fields.

Type math expressions directly into any numeric field and have them automatically calculated when you finish editing!

## Features
- **Evaluate math expressions** in any numeric input field (e.g., `2+3` → `5`)
- **Shorthand operators**: Use `x` for multiply, `d` for divide, `a` for add, `s` for subtract
- **Standard operators**: Also supports `*`, `/`, `+`, `-`
- **Proper operator precedence**: Multiplication and division before addition and subtraction
- **Complex expressions**: Supports parentheses and more advanced calculations
- **Optional string field support**: Enable math evaluation in string fields via config

## Usage Examples
- Type `100+50` in a position field → becomes `150`
- Type `5x3` in a scale field → becomes `15`
- Type `10d2` in any numeric field → becomes `5`
- Type `(2+3)*4` → becomes `20`

## Installation
1. Install [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader).
1. Place [Numantics.dll](https://github.com/nalathethird/R-Numantics/releases/latest/download/Numantics.dll) into your `rml_mods` folder. This folder should be at `C:\Program Files (x86)\Steam\steamapps\common\Resonite\rml_mods` for a default install. You can create it if it's missing, or if you launch the game once with ResoniteModLoader installed it will create this folder for you.
1. Start the game. If you want to verify that the mod is working you can check your Resonite logs.

## Configuration
The mod creates a config file with the following options:
- `enable_math`: Enable/disable math processing (default: `true`)
- `include_strings`: Allow math in string fields (default: `false`)
- `verbose_logging`: Enable detailed logging for debugging (default: `false`)

## Links
- [GitHub Repository](https://github.com/nalathethird/R-Numantics)
- [Resonite Modding Group](https://github.com/resonite-modding-group)
