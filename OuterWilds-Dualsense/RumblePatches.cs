using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// The PC build of Outer Wilds still runs the console-era trigger effect logic inside
	/// RumbleManager but throws the result away. These patches read the effect the game picked
	/// for each trigger at the end of RumbleManager.Update and forward it to the DualSense.
	/// </summary>
	[HarmonyPatch(typeof(RumbleManager))]
	internal static class RumblePatches
	{
		// Mirrors RumbleManager.TriggerEffect.Mode.
		private enum GameMode { None, ResistanceConstant, ResistanceSlope, VibrationConstant, VibrationSlope }

		private static FieldInfo _stackField;
		private static bool _reflectionReady;
		private static FieldInfo _fActive, _fMode, _fResStart, _fResEnd, _fFreq, _fStrength, _fStrengths;
		private static MethodInfo _mScale, _mLeft, _mRight;

		// Own effects layered on top of what the game selected.
		private static bool _jetpackBoost;

		[HarmonyPostfix]
		[HarmonyPatch("Update")]
		private static void UpdatePostfix(RumbleManager __instance)
		{
			var mod = DualSenseMod.Instance;
			var device = DualSenseMod.Device;
			if (mod == null || device == null || !mod.Enabled) return;

			try
			{
				TriggerEffect left = TriggerEffect.Off;
				TriggerEffect right = TriggerEffect.Off;

				// Reading with the translator freezes time (PauseType.Reading) while the player stands still: the triggers stay on,
				// otherwise they switched off and back on with every nudge of the stick. Menus, loading and sleeping silence them.
				bool paused = OWTime.IsPaused(OWTime.PauseType.Menu) || OWTime.IsPaused(OWTime.PauseType.Loading)
					|| OWTime.IsPaused(OWTime.PauseType.Sleeping) || OWTime.IsPaused(OWTime.PauseType.Initializing)
					|| OWTime.IsPaused(OWTime.PauseType.Streaming) || OWTime.IsPaused(OWTime.PauseType.System);
				if (mod.AdaptiveTriggers && !paused && __instance.enabled)
				{
					TriggerLogic.Compute(mod.TriggerStrength, out left, out right);

					// Effects the game asks for itself (ship ignition, turbulence) win over ours.
					TriggerEffect gameLeft, gameRight;
					PickGameEffects(__instance, out gameLeft, out gameRight);
					if (gameLeft.Mode != TriggerEffect.Off.Mode) left = gameLeft;
					if (gameRight.Mode != TriggerEffect.Off.Mode) right = gameRight;

					if (_jetpackBoost)
					{
						var boost = Effects.Get("jp.boost");
						right = TriggerEffect.Vibration(boost.Int("zone"), boost.Int("freq"),
							Mathf.Clamp(Mathf.RoundToInt(boost.Int("amp") * mod.TriggerStrength), 1, 8));
					}

					TornadoHaptics.ApplyTriggers(ref left, ref right); // full resistance while a tornado holds the player; last word

					// The ship autopilot is flying: no resistance and no buzz, even the game's own effects and a tornado's.
					if (TriggerLogic.AutopilotHolds())
					{
						left = TriggerEffect.Off;
						right = TriggerEffect.Off;
					}
				}

				// A "Play" button in the F1 menu overrides the triggers for a few seconds, even while paused.
				if (EffectPreview.TriggersActive) EffectPreview.GetTriggers(out left, out right);

				device.SetTriggers(left, right);
				if (mod.Lightbar) PlanetLight.Update(device, Time.unscaledDeltaTime);
				device.Flush(Time.unscaledDeltaTime);
			}
			catch (Exception e)
			{
				DualSenseMod.Log("Update patch failed: " + e);
				mod.Enabled = false; // do not spam the log every frame
			}
		}

		[HarmonyPostfix]
		[HarmonyPatch("StartJetpackBoost")]
		private static void StartBoost() { _jetpackBoost = true; }

		[HarmonyPostfix]
		[HarmonyPatch("StopJetpackBoost")]
		private static void StopBoost() { _jetpackBoost = false; }

		private static TriggerEffect ScaleVibration(int freq, int amplitude, float scale)
		{
			return TriggerEffect.Vibration(2, freq, Mathf.RoundToInt(amplitude * scale));
		}

		/// <summary>Repeats the game's own selection loop: the topmost active effect per trigger wins.</summary>
		private static void PickGameEffects(RumbleManager manager, out TriggerEffect left, out TriggerEffect right)
		{
			left = TriggerEffect.Off;
			right = TriggerEffect.Off;
			if (!_reflectionReady && !InitReflection(manager)) return;

			var stack = _stackField.GetValue(manager) as IList;
			if (stack == null) return;

			bool leftDone = false, rightDone = false;
			for (int i = stack.Count - 1; i >= 0 && !(leftDone && rightDone); i--)
			{
				object fx = stack[i];
				if (fx == null || !(bool)_fActive.GetValue(fx)) continue;

				if (!leftDone && (bool)_mLeft.Invoke(fx, null))
				{
					left = Convert(fx);
					leftDone = true;
				}
				if (!rightDone && (bool)_mRight.Invoke(fx, null))
				{
					right = Convert(fx);
					rightDone = true;
				}
			}
		}

		private static TriggerEffect Convert(object fx)
		{
			float strengthScale = DualSenseMod.Instance.TriggerStrength * (float)_mScale.Invoke(fx, null);
			var mode = (GameMode)System.Convert.ToInt32(_fMode.GetValue(fx));
			switch (mode)
			{
				case GameMode.ResistanceConstant:
					return TriggerEffect.Resistance(0, Scaled((int)_fResStart.GetValue(fx), strengthScale));
				case GameMode.ResistanceSlope:
					return TriggerEffect.ResistanceSlope(
						Scaled((int)_fResStart.GetValue(fx), strengthScale),
						Scaled((int)_fResEnd.GetValue(fx), strengthScale));
				case GameMode.VibrationConstant:
					return TriggerEffect.Vibration(0, (int)_fFreq.GetValue(fx), Scaled((int)_fStrength.GetValue(fx), strengthScale));
				case GameMode.VibrationSlope:
				{
					// Per-zone strength curves are collapsed to their peak for now.
					var arr = _fStrengths.GetValue(fx) as byte[];
					int peak = 0;
					if (arr != null) foreach (byte b in arr) peak = Math.Max(peak, b);
					return TriggerEffect.Vibration(0, (int)_fFreq.GetValue(fx), Scaled(peak, strengthScale));
				}
				default:
					return TriggerEffect.Off; // "None" explicitly means: no effect on this trigger
			}
		}

		private static int Scaled(int strength, float scale)
		{
			return Mathf.Clamp(Mathf.RoundToInt(strength * scale), 0, 8);
		}

		private static bool InitReflection(RumbleManager manager)
		{
			_stackField = AccessTools.Field(typeof(RumbleManager), "m_triggerEffectStack");
			var elementType = _stackField == null ? null : _stackField.FieldType.GetElementType();
			if (elementType == null)
			{
				DualSenseMod.Log("RumbleManager.m_triggerEffectStack not found; this game version is not supported.");
				DualSenseMod.Instance.AdaptiveTriggers = false;
				return false;
			}

			_fActive = AccessTools.Field(elementType, "active");
			_fMode = AccessTools.Field(elementType, "mode");
			_fResStart = AccessTools.Field(elementType, "resistanceStrengthStart");
			_fResEnd = AccessTools.Field(elementType, "resistanceStrengthEnd");
			_fFreq = AccessTools.Field(elementType, "vibrationFrequency");
			_fStrength = AccessTools.Field(elementType, "vibrationStrength");
			_fStrengths = AccessTools.Field(elementType, "variableStrengthArray");
			_mScale = AccessTools.Method(elementType, "GetStrengthScale");
			_mLeft = AccessTools.Method(elementType, "AffectsLeftTrigger");
			_mRight = AccessTools.Method(elementType, "AffectsRightTrigger");
			_reflectionReady = true;
			DualSenseMod.Log("Trigger effect reflection ready.");
			return true;
		}
	}
}
