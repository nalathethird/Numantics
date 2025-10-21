using System;
using System.Reflection;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.Undo;
using HarmonyLib;
using ResoniteModLoader;
using System.Globalization;
using System.Data;

namespace Numantics {
	public class Numantics : ResoniteMod {
		internal const string VERSION_CONSTANT = "1.0.0";
		public override string Name => "Numantics";
		public override string Author => "NalaTheThird";
		public override string Version => VERSION_CONSTANT;
		public override string Link => "https://github.com/nalathethird/R-Numantics";
		
		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<bool> EnableValueFieldMath =
			new ModConfigurationKey<bool>("enable_math", "Enable math processing in input fields", () => true);

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<bool> IncludeStrings =
			new ModConfigurationKey<bool>("include_strings", "Allow math to be calculated in string fields - slightly dangerous, be careful!", () => false);

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<bool> VerboseLogging =
			new ModConfigurationKey<bool>("verbose_logging", "Enable verbose logging for debugging", () => false);

		private static ModConfiguration Config;

		public override void OnEngineInit() {
			Config = GetConfiguration();
			Config.Save(true);

			var harmony = new Harmony("com.nalathethird.Numantics");
			harmony.PatchAll();
			Msg("Harmony patches applied - happy mathing!");
		}

		[HarmonyPatch(typeof(TextEditor))]
		[HarmonyPatch("OnFinished")]
		class TextEditor_OnFinished_Patch {
			static void Prefix(TextEditor __instance) {
				try {
					bool verbose = Config?.GetValue(VerboseLogging) ?? false;

					if (verbose) Msg("OnFinished called");

					if (__instance?.Text?.Target == null) {
						if (verbose) Msg("TextEditor or Text.Target is null, skipping");
						return;
					}

					if (Config == null) {
						Warn("Config is null, cannot proceed");
						return;
					}

					if (!Config.GetValue(EnableValueFieldMath)) {
						if (verbose) Msg("Math evaluation is disabled in config");
						return;
					}

					string text = __instance.Text.Target.Text;
					if (verbose) Msg($"Input text: '{text}'");

					if (string.IsNullOrWhiteSpace(text)) {
						if (verbose) Msg("Text is null or whitespace, skipping");
						return;
					}

					string exprText = text
						.Replace("x", "*")
						.Replace("d", "/")
						.Replace("a", "+")
						.Replace("s", "-");

					if (verbose && exprText != text) {
						Msg($" Replaced operators: '{text}' => '{exprText}'");
					}

					if (TryEvaluateExpression(exprText, out string result, verbose)) {
						Msg($"SUCCESS - Evaluated '{text}' => '{result}'");
						
						__instance.Text.Target.Text = result;
						
						if (verbose) Msg($"Updated Text.Target.Text to '{result}'");
					} else {
						if (verbose) Msg($"Not a math expression or evaluation failed: '{exprText}'");
					}
				} catch (Exception e) {
					Error($"Exception in OnFinished patch: {e.Message}");
					Error($"Stack trace: {e.StackTrace}");
				}
			}

			private static bool TryEvaluateExpression(string input, out string result, bool verbose) {
				result = input;
				
				if (!(input.Contains("+") || input.Contains("-") || input.Contains("*") || input.Contains("/"))) {
					if (verbose) Msg("No math operators found in input");
					return false;
				}

				try {
					if (verbose) Msg($"Attempting to parse expression: '{input}'");
					var table = new DataTable();
					var evaluated = table.Compute(input, "");
					double resultValue = Convert.ToDouble(evaluated);
					
					if (verbose) Msg($"Evaluated to: {resultValue}");
					
					result = resultValue.ToString(CultureInfo.InvariantCulture);
					return true;
				} catch (Exception ex) {
					Warn($"Expression evaluation failed for '{input}': {ex.Message}");
					if (verbose) Warn($"Exception type: {ex.GetType().Name}");
					return false;
				}
			}
		}
	}
}
