using HarmonyLib;

namespace OuterWildsDualSense
{
	/// <summary>
	/// The game rumbles through Unity's Input System, which writes its own HID reports to the
	/// DualSense (including the lightbar) and fights with ours. While our device is open we take
	/// over the motors and skip the Input System call.
	/// </summary>
	[HarmonyPatch(typeof(InputManager), nameof(InputManager.Rumble))]
	internal static class InputManagerRumblePatch
	{
		private static bool Prefix(ref float hiPower, ref float lowPower)
		{
			var mod = DualSenseMod.Instance;
			var device = DualSenseMod.Device;
			if (mod == null || device == null || !mod.Enabled || !device.IsOpen) return true;

			EffectPreview.OverrideRumble(ref hiPower, ref lowPower);
			var haptics = DualSenseMod.Haptics;
			if (haptics != null)
			{
				// Rumble as audio haptics: the motors stay off, so no rumble flags disturb the haptic audio.
				haptics.Rumble.TargetLow = lowPower;
				haptics.Rumble.TargetHigh = hiPower;
				device.SetMotors(0f, 0f);
			}
			else
			{
				device.SetMotors(hiPower, lowPower);
			}
			return false;
		}
	}
}
