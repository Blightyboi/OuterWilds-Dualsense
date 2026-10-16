using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// Hooks every OWAudioSource owned by the ship and jetpack thruster audio so its sound also drives the
	/// controller's haptic actuators. Left/right ship banks are mapped to the left/right actuator.
	/// </summary>
	[HarmonyPatch(typeof(ThrusterAudio), "Start")]
	internal static class ThrusterAudioPatch
	{
		private static void Postfix(ThrusterAudio __instance)
		{
			var mod = DualSenseMod.Instance;
			if (mod == null || !mod.Enabled || !mod.AudioHaptics) return;
			if (!DualSenseMod.EnsureHaptics()) return;

			try
			{
				int count = 0;
				for (Type type = __instance.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
				{
					foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
					{
						if (field.FieldType != typeof(OWAudioSource)) continue;
						var source = field.GetValue(__instance) as OWAudioSource;
						if (source == null) continue;

						string lower = field.Name.ToLowerInvariant();
						float pan = lower.Contains("left") ? -1f : lower.Contains("right") ? 1f : float.NaN;
						HapticTap.Attach(source, pan);
						count++;
					}
				}
				DualSenseMod.Debug("Tapped " + count + " audio sources on " + __instance.GetType().Name);
			}
			catch (Exception e)
			{
				DualSenseMod.Log("Audio tap failed: " + e);
			}
		}
	}
}
