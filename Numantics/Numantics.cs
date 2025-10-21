using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

using FrooxEngine;

using HarmonyLib;

using ResoniteModLoader;

namespace Numantics {
	public class Numantics : ResoniteMod {
		internal const string VERSION_CONSTANT = "1.0.1";
		public override string Name => "Numantics";
		public override string Author => "NalaTheThird";
		public override string Version => VERSION_CONSTANT;
		public override string Link => "https://github.com/nalathethird/Numantics";
		
		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<bool> EnableValueFieldMath =
			new ModConfigurationKey<bool>("enable_math", "Enable math processing in input fields - Disables/Enables Entire Mod", () => true);

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<bool> IncludeStrings =
			new ModConfigurationKey<bool>("include_strings", "Allow math to be calculated in 'string' type fields", () => false);

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<bool> RoundResults =
			new ModConfigurationKey<bool>("round_results", "Round all calculated results to nearest integer (e.g., 5*0.5=3 instead of 2.5)", () => false);

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<bool> EnableEasterEggs =
			new ModConfigurationKey<bool>("enable_easter_eggs", "Enable 'Easter eggs' - (Who knows what you might get with this on...)", () => false);

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<bool> VerboseLogging =
			new ModConfigurationKey<bool>("verbose_logging", "Enables Verbose Logging (Not Likely needed for regular folks)", () => false);

		private static ModConfiguration Config;

		public override void OnEngineInit() {
			Config = GetConfiguration();
			Config.Save(true);

			var harmony = new Harmony("com.nalathethird.Numantics");
			harmony.PatchAll();
			Msg("NalaTheThird was here :p - Thanks for using my mod, please consider staring the Repo (the link value in the mod settings), or sending me a Donation on Ko-Fi: https://www.ko-fi.com/zeianala | It really motivates me to make more things and keep my mods maintained!");
			Msg("Harmony patch applied! Ready to Crunch the Numbers with Numantics!");
		}

		[HarmonyPatch(typeof(TextEditor))]
		[HarmonyPatch("OnFinished")]
		class TextEditor_OnFinished_Patch {
			static void Prefix(TextEditor __instance) {
				try {
					bool verbose = Config?.GetValue(VerboseLogging) ?? false;


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

					Type fieldType = GetFieldType(__instance, verbose);
					bool isStringField = fieldType == typeof(string);
					
					if (verbose) Msg($"Detected field type: {fieldType?.Name ?? "Unknown"}");
					
					if (isStringField) {
						if (!Config.GetValue(IncludeStrings)) {
							if (verbose) Msg("Editing string field but include_strings is disabled, skipping");
							return;
						}
						if (verbose) Msg("String field detected and include_strings is enabled");
					}

					// Storage for EE.
					string originalText = text.Replace(" ", "");

					text = text.Replace("pi", Math.PI.ToString(CultureInfo.InvariantCulture));
					if (verbose && text != originalText) {
						Msg("Replaced 'pi' constant");
					}

					string exprText = ProcessMathFunctions(text, verbose);

					// Shorthand ops - with regex to avoid breaking function names
					exprText = Regex.Replace(exprText, @"(?<![a-zA-Z])x(?![a-zA-Z])", "*");
					exprText = Regex.Replace(exprText, @"(?<![a-zA-Z])d(?![a-zA-Z])", "/");
					exprText = Regex.Replace(exprText, @"(?<![a-zA-Z])a(?![a-zA-Z])", "+");
					exprText = Regex.Replace(exprText, @"(?<![a-zA-Z])s(?![a-zA-Z])", "-");
					exprText = exprText.Replace("^", "^");

					if (verbose && exprText != text) {
						Msg($"Replaced operators: '{text}' => '{exprText}'");
					}

					// EE Values and Checks
					bool easterEggs = Config.GetValue(EnableEasterEggs);
					if (easterEggs) {

						if (originalText.ToLower().Contains("sqrt")) {
							Msg("EASTER EGG - Squirt!");
						}

						if (originalText == "0/0") {
							Msg("EASTER EGG - NaN ERROR! 0! VOID, ERROR! ERROR! You Destroy.e.d t..h..e w...o...r..l..d.... (Leaving world...)");
							__instance.Text.Target.Text = "NaN";
							__instance.World.RunSynchronously(() => {
								if (__instance.World.IsAuthority) {
									Userspace.EndSession(__instance.World);
								} else {
									Userspace.LeaveSession(__instance.World);
								}
							});
							return;
						}
						if (originalText == "2+2") {
							__instance.Text.Target.Text = "5";
							Msg("EASTER EGG - Look, we get it, your '2s' are 'very large'. Just take the 5 and go.");
							return;
						}
						if (originalText == "6/2*(1+2)") {
							__instance.Text.Target.Text = "9";
							Msg("EASTER EGG - It's 9. Don't you DARE start this again. We know who you are.");
							return;
						}
						if (originalText == "69+420") {
							__instance.Text.Target.Text = "0";
							Msg("EASTER EGG - No mating or smoking :< (at least the math checks out) Still going to make this 0 though for your rampant behavior.");
							return;
						}
						if (originalText == "9+10") {
							__instance.Text.Target.Text = "21";
							Msg("EASTER EGG - U Stoopid, ITS 19!)");
							return;
						}
					}

					// Expression Evaluator - If you cant express it, you cant impress it! | Main Logic for Math Evaluation
					if (TryEvaluateExpression(exprText, out string result, verbose)) {
						if (Config.GetValue(RoundResults)) {
							if (double.TryParse(result, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) {
								double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
								if (verbose) Msg($"Rounded {value} to {rounded}");
								result = rounded.ToString(CultureInfo.InvariantCulture);
							}
						}

						Msg($"SUCCESS - Evaluated '{originalText}' => '{result}'");
						__instance.Text.Target.Text = result;
						
						if (verbose) Msg($"Updated Text.Target.Text to '{result}'");
					} else {
						if (double.TryParse(exprText, NumberStyles.Float, CultureInfo.InvariantCulture, out double numValue)) {
							string numResult = numValue.ToString(CultureInfo.InvariantCulture);

							if (Config.GetValue(RoundResults)) {
								double rounded = Math.Round(numValue, MidpointRounding.AwayFromZero);
								if (verbose) Msg($"Rounded {numValue} to {rounded}");
								numResult = rounded.ToString(CultureInfo.InvariantCulture);
							}
							
							Msg($"SUCCESS - Evaluated '{originalText}' => '{numResult}'");
							__instance.Text.Target.Text = numResult;
							if (verbose) Msg($"Updated Text.Target.Text to '{numResult}'");
						} else {
							if (verbose) Msg($"Not a math expression or evaluation failed: '{exprText}'");
						}
					}
				} catch (Exception e) {
					Error($"Exception in OnFinished patch: {e.Message}");
					Error($"Stack trace: {e.StackTrace}");
				}
			}

			private static bool IsIntegerType(Type type) {
				return type == typeof(int) || type == typeof(long) ||
					   type == typeof(short) || type == typeof(byte) ||
					   type == typeof(uint) || type == typeof(ulong) ||
					   type == typeof(ushort) || type == typeof(sbyte);
			}

			/// Question: Is this next part RefHacking?
			/// No! Technically this is NOT RefHacking!
			/// RefHacking would imply that we are manipulating RefIDs to access references you shouldn't have access to normally in the Engine.
			/// What we are doing HERE, is simply using Reflection to access properties that are not publicly exposed.
			/// Reflection is a standard .NET feature that allows inspection of types at runtime.
			/// Here, we are using an "Inspect Component State" approach to navigate the component hierarchy to find the field type associated with the TextEditor.
			/// This ENTIRE next section is to JUST get the field type of the TextEditor being edited so we know if its a String, Float, Int etc. which makes it easier for the mod to determine how to handle the input.
			private static Type GetFieldType(TextEditor editor, bool verbose) {
				try {
					var components = new List<Component>();
					editor.Slot.GetComponentsInParents<Component>(components);

					foreach (var component in components) {
						var accessorProp = component.GetType().GetProperty("Accessor");
						if (accessorProp != null) {
							var accessor = accessorProp.GetValue(component);
							if (accessor != null) {
								var targetTypeProp = accessor.GetType().GetProperty("TargetType");
								if (targetTypeProp != null) {
									var targetType = targetTypeProp.GetValue(accessor) as Type;
									if (targetType != null) {
										if (verbose) Msg($"Found field type from MemberEditor.Accessor: {targetType.Name}");
										return targetType;
									}
								}
							}
						}
					}

					if (editor.Text.Target is IField directField) {
						if (verbose) Msg($"Text.Target is IField: {directField.ValueType.Name}");
						return directField.ValueType;
					}

					var textTarget = editor.Text.Target;
					if (textTarget != null) {
						var component = textTarget as Component;
						if (component != null && component.GetType().IsGenericType) {
							var genericTypeDef = component.GetType().GetGenericTypeDefinition();
							if (genericTypeDef == typeof(ValueField<>)) {
								var genericArgs = component.GetType().GetGenericArguments();
								if (genericArgs.Length > 0) {
									if (verbose) Msg($"Found ValueField<{genericArgs[0].Name}>");
									return genericArgs[0];
								}
							}
						}

						if (textTarget is IWorldElement worldElement) {
							var slot = worldElement.Parent as Slot;
							if (slot != null) {
								var parentField = slot.GetComponentInParents<IField>();
								if (parentField != null) {
									if (verbose) Msg($"Found IField in parents: {parentField.ValueType.Name}");
									return parentField.ValueType;
								}
							}
						}
					}

					// Fallback: check editors slot hierarchy - JUSTTT in-case all the above fails. (which, I doubt it wont, but hey, safety first!)
					var editorField = editor.Slot.GetComponentInParents<IField>();
					if (editorField != null) {
						if (verbose) Msg($"Found IField from editor slot: {editorField.ValueType.Name}");
						return editorField.ValueType;
					}

					if (verbose) Msg("Could not determine field type");
					return null;
				} catch (Exception ex) {
					if (verbose) Warn($"Error detecting field type: {ex.Message}");
					return null;
				}
			}

			private static string ProcessMathFunctions(string input, bool verbose) {
				string processed = input;
				
				processed = ProcessFunction(processed, "sqrt", Math.Sqrt, verbose);
				processed = ProcessFunction(processed, "sin", x => Math.Sin(x * Math.PI / 180.0), verbose);
				processed = ProcessFunction(processed, "cos", x => Math.Cos(x * Math.PI / 180.0), verbose);
				processed = ProcessFunction(processed, "tan", x => Math.Tan(x * Math.PI / 180.0), verbose);
				processed = ProcessFunction(processed, "log10", Math.Log10, verbose);
				processed = ProcessFunction(processed, "log", Math.Log, verbose);
				processed = ProcessFunction(processed, "ln", Math.Log, verbose);
				processed = ProcessFunction(processed, "abs", Math.Abs, verbose);
				processed = ProcessFunction(processed, "floor", Math.Floor, verbose);
				processed = ProcessFunction(processed, "ceil", Math.Ceiling, verbose);
				
				return processed;
			}

			private static string ProcessFunction(string input, string funcName, Func<double, double> func, bool verbose) {
				var regex = new Regex($@"{funcName}\(([^)]+)\)", RegexOptions.IgnoreCase);
				while (regex.IsMatch(input)) {
					var match = regex.Match(input);
					string innerExpr = match.Groups[1].Value;
					
					double innerValue;
					if (double.TryParse(innerExpr, NumberStyles.Float, CultureInfo.InvariantCulture, out innerValue)) {
					} else {
						var innerTable = new DataTable();
						innerValue = Convert.ToDouble(innerTable.Compute(innerExpr, ""));
					}
					
					double funcResult = func(innerValue);
					
					// Floating Point Rounding - We all float down here...
					funcResult = Math.Round(funcResult, 10, MidpointRounding.AwayFromZero);
					
					input = input.Replace(match.Value, funcResult.ToString(CultureInfo.InvariantCulture));
					
					if (verbose) Msg($"Evaluated {funcName}({innerExpr}) = {funcResult}");
				}
				return input;
			}

			private static bool TryEvaluateExpression(string input, out string result, bool verbose) {
				result = input;

				// Container Checks - Operator, do we have a dial tone? - (basically checks for math operators then allows calculations)
				if (!(input.Contains("+") || input.Contains("-") || input.Contains("*") || 
				      input.Contains("/") || input.Contains("^") || input.Contains("%"))) {
					if (verbose) Msg("No math operators found in input");
					return false;
				}

				try {
					if (verbose) Msg($"Attempting to parse expression: '{input}'");
					
					string processedInput = input;

					// Percentage Handler - you just HAD to use these instead of decimals didn't you? For SHAME!
					var percentRegex = new Regex(@"(\d+(?:\.\d+)?)%");
					processedInput = percentRegex.Replace(processedInput, match => {
						double percentValue = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
						string converted = $"*{(percentValue / 100.0).ToString(CultureInfo.InvariantCulture)}";
						if (verbose) Msg($"Converted {match.Value} to {converted}");
						return converted;
					});

					// Power Handler - SO MUCH POWER! ITS OVER 9000!
					while (processedInput.Contains("^")) {
						int powIndex = processedInput.IndexOf('^');

						int leftStart = FindNumberStart(processedInput, powIndex - 1);
						string leftStr = processedInput.Substring(leftStart, powIndex - leftStart);

						int rightEnd = FindNumberEnd(processedInput, powIndex + 1);
						string rightStr = processedInput.Substring(powIndex + 1, rightEnd - powIndex - 1);

						double left;
						if (!double.TryParse(leftStr.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out left)) {
							break;
						}

						double right;
						if (!double.TryParse(rightStr.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out right)) {
							try {
								var innerTable = new DataTable();
								right = Convert.ToDouble(innerTable.Compute(rightStr.Trim(), ""));
							} catch {
								break;
							}
						}

						double powResult = Math.Pow(left, right);

						processedInput = processedInput.Substring(0, leftStart) +
						powResult.ToString(CultureInfo.InvariantCulture) +
						processedInput.Substring(rightEnd);

						if (verbose) Msg($"Evaluated power: {left}^{right} = {powResult}");
					}
					
					var table = new DataTable();
					var evaluated = table.Compute(processedInput, "");
					double resultValue = Convert.ToDouble(evaluated);

					// Floating Point Precision Handling
					resultValue = Math.Round(resultValue, 7, MidpointRounding.AwayFromZero);
					
					if (verbose) Msg($"Evaluated to: {resultValue}");
					
					result = resultValue.ToString(CultureInfo.InvariantCulture);
					return true;
				} catch (Exception ex) {
					Warn($"Expression evaluation failed for '{input}': {ex.Message}");
					if (verbose) Warn($"Exception type: {ex.GetType().Name}");
					return false;
				}
			}

			private static int FindNumberStart(string expr, int from) {
				while (from > 0 && (char.IsDigit(expr[from]) || expr[from] == '.' || 
				    (expr[from] == '-' && from > 0 && !char.IsDigit(expr[from - 1])))) {
					from--;
				}
				if (from == 0 && (char.IsDigit(expr[0]) || expr[0] == '-' || expr[0] == '.')) {
					return 0;
				}
				return from + 1;
			}
			
			private static int FindNumberEnd(string expr, int from) {
				while (from < expr.Length && (char.IsDigit(expr[from]) || expr[from] == '.' || 
				    (expr[from] == '-' && from == 0))) {
					from++;
				}
				return from;
			}
		}
	}
}
