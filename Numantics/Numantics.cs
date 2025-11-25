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
		internal const string VERSION_CONSTANT = "1.0.3";
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

		private static ModConfiguration Config;

		public override void OnEngineInit() {
			Config = GetConfiguration();
			Config.Save(true);

			var harmony = new Harmony("com.nalathethird.Numantics");
			harmony.PatchAll();
		}

		[HarmonyPatch(typeof(TextEditor))]
		[HarmonyPatch("OnFinished")]
		class TextEditor_OnFinished_Patch {
			static void Prefix(TextEditor __instance) {
				try {
					if (__instance?.Text?.Target == null) {
						return;
					}

					if (Config == null) {
						Warn("Config is null, cannot proceed");
						return;
					}

					if (!Config.GetValue(EnableValueFieldMath)) {
						return;
					}

					string text = __instance.Text.Target.Text;

					if (string.IsNullOrWhiteSpace(text)) {
						return;
					}

					Type fieldType = GetFieldType(__instance);
					bool isStringField = fieldType == typeof(string);
					
					if (isStringField) {
						if (!Config.GetValue(IncludeStrings)) {
							return;
						}
					}

					// Storage for EE.
					string originalText = text.Replace(" ", "");

					text = text.Replace("pi", Math.PI.ToString(CultureInfo.InvariantCulture));

					string exprText = ProcessMathFunctions(text);

					// Shorthand ops - with regex to avoid breaking function names
					exprText = Regex.Replace(exprText, @"(?<![a-zA-Z])x(?![a-zA-Z])", "*");
					exprText = Regex.Replace(exprText, @"(?<![a-zA-Z])d(?![a-zA-Z])", "/");
					exprText = Regex.Replace(exprText, @"(?<![a-zA-Z])a(?![a-zA-Z])", "+");
					exprText = Regex.Replace(exprText, @"(?<![a-zA-Z])s(?![a-zA-Z])", "-");
					exprText = exprText.Replace("^", "^");

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
					if (TryEvaluateExpression(exprText, out string result)) {
						if (Config.GetValue(RoundResults)) {
							if (double.TryParse(result, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) {
								double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
								result = rounded.ToString(CultureInfo.InvariantCulture);
							}
						}

						Debug($"SUCCESS - Evaluated '{originalText}' => '{result}'");
						__instance.Text.Target.Text = result;
					} else {
						if (double.TryParse(exprText, NumberStyles.Float, CultureInfo.InvariantCulture, out double numValue)) {
							string numResult = numValue.ToString(CultureInfo.InvariantCulture);

							if (Config.GetValue(RoundResults)) {
								double rounded = Math.Round(numValue, MidpointRounding.AwayFromZero);
								numResult = rounded.ToString(CultureInfo.InvariantCulture);
							}
							
							Msg($"SUCCESS - Evaluated '{originalText}' => '{numResult}'");
							__instance.Text.Target.Text = numResult;
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
			private static Type GetFieldType(TextEditor editor) {
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
										return targetType;
									}
								}
							}
						}
					}

					if (editor.Text.Target is IField directField) {
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
									return genericArgs[0];
								}
							}
						}

						if (textTarget is IWorldElement worldElement) {
							var slot = worldElement.Parent as Slot;
							if (slot != null) {
								var parentField = slot.GetComponentInParents<IField>();
								if (parentField != null) {
									return parentField.ValueType;
								}
							}
						}
					}

					var editorField = editor.Slot.GetComponentInParents<IField>();
					if (editorField != null) {
						return editorField.ValueType;
					}

					return null;
				} catch {
					return null;
				}
			}

			private static string ProcessMathFunctions(string input) {
				string processed = input;

				processed = ProcessFunction(processed, "sqrt", Math.Sqrt);
				processed = ProcessFunction(processed, "sin", x => Math.Sin(x * Math.PI / 180.0));
				processed = ProcessFunction(processed, "cos", x => Math.Cos(x * Math.PI / 180.0));
				processed = ProcessFunction(processed, "tan", x => Math.Tan(x * Math.PI / 180.0));
				processed = ProcessFunction(processed, "asin", x => Math.Asin(x) * 180.0 / Math.PI);
				processed = ProcessFunction(processed, "acos", x => Math.Acos(x) * 180.0 / Math.PI);
				processed = ProcessFunction(processed, "atan", x => Math.Atan(x) * 180.0 / Math.PI);
				processed = ProcessFunction(processed, "log10", Math.Log10);
				processed = ProcessFunction(processed, "log", Math.Log);
				processed = ProcessFunction(processed, "ln", Math.Log);
				processed = ProcessFunction(processed, "abs", Math.Abs);
				processed = ProcessFunction(processed, "floor", Math.Floor);
				processed = ProcessFunction(processed, "ceil", Math.Ceiling);

				return processed;
			}

			private static string ProcessFunction(string input, string funcName, Func<double, double> func) {
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
					funcResult = Math.Round(funcResult, 10, MidpointRounding.AwayFromZero);
					input = input.Replace(match.Value, funcResult.ToString(CultureInfo.InvariantCulture));
				}
				return input;
			}

			private static bool TryEvaluateExpression(string input, out string result) {
				result = input;

				// Container Checks - Operator, do we have a dial tone? - (basically checks for math operators then allows calculations)
				if (!(input.Contains("+") || input.Contains("-") || input.Contains("*") || 
				      input.Contains("/") || input.Contains("^") || input.Contains("%"))) {
					return false;
				}

				try {
					string processedInput = input;

					// Percentage Handler - you just HAD to use these instead of decimals didn't you? For SHAME!
					var percentRegex = new Regex(@"(\d+(?:\.\d+)?)%");
					processedInput = percentRegex.Replace(processedInput, match => {
						double percentValue = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
						string converted = $"*{(percentValue / 100.0).ToString(CultureInfo.InvariantCulture)}";
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
					}
					
					var table = new DataTable();
					var evaluated = table.Compute(processedInput, "");
					double resultValue = Convert.ToDouble(evaluated);
					resultValue = Math.Round(resultValue, 7, MidpointRounding.AwayFromZero);
					result = resultValue.ToString(CultureInfo.InvariantCulture);
					return true;
				} catch (Exception ex) {
					Warn($"Expression evaluation failed for '{input}': {ex.Message}");
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
