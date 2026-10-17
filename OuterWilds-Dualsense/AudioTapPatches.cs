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
		/// <summary>
		/// The real thrust (0-1) behind a thruster sound, or null for sounds that are not continuous thrust.
		/// Names are the serialized field names of ThrusterAudio and its subclasses, in lower case.
		/// </summary>
		private static Func<float> ThrustFor(ThrusterModel model, string field)
		{
			var jetpack = model as JetpackThrusterModel;
			if (jetpack != null)
			{
				if (field.Contains("boost")) return () => jetpack.IsBoosterFiring() ? 1f : 0f;
				if (field.Contains("translational") || field.Contains("underwater") || field.Contains("oxygen"))
					return () => jetpack.IsBoosterFiring() ? 0f : jetpack.GetThrustFraction();
				return null;
			}

			var ship = model as ShipThrusterModel;
			if (ship != null && (field.Contains("left") || field.Contains("right")))
			{
				bool leftBank = field.Contains("left");
				return () => BankThrust(ship, leftBank);
			}
			return null;
		}

		/// <summary>Thrust of the left or right engine bank, the same split the ship's own audio uses.</summary>
		private static float BankThrust(ShipThrusterModel ship, bool leftBank)
		{
			if (!ship.IsThrusterBankEnabled(leftBank ? ThrusterBank.Left : ThrusterBank.Right)) return 0f;
			Vector3 accel = ship.GetLocalAcceleration();
			accel.x = leftBank ? Mathf.Max(0f, accel.x) : Mathf.Min(0f, accel.x);
			return Mathf.Clamp01(accel.magnitude / ship.GetMaxTranslationalThrust());
		}

		private static void Postfix(ThrusterAudio __instance)
		{
			var mod = DualSenseMod.Instance;
			if (mod == null || !mod.Enabled || !mod.AudioHaptics) return;
			if (!DualSenseMod.EnsureHaptics()) return;

			try
			{
				int count = 0;
				var model = AccessTools.Field(typeof(ThrusterAudio), "_thrusterModel").GetValue(__instance) as ThrusterModel;
				for (Type type = __instance.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
				{
					foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
					{
						if (field.FieldType != typeof(OWAudioSource)) continue;
						var source = field.GetValue(__instance) as OWAudioSource;
						if (source == null) continue;

						string lower = field.Name.ToLowerInvariant();
						float pan = lower.Contains("left") ? -1f : lower.Contains("right") ? 1f : float.NaN;
						HapticTap.Attach(source, pan, ThrustFor(model, lower));
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

	/// <summary>
	/// One-shot sounds (ship ignition, rotational thrusters) played on a tapped source use the haptic
	/// file extracted from the game instead of the live copy of the sound.
	/// </summary>
	internal static class OneShotHook
	{
		public static void Play(OWAudioSource source, AudioClip clip, float volume)
		{
			var mod = DualSenseMod.Instance;
			if (mod == null || !mod.Enabled || !mod.AudioHaptics || !mod.UseExtractedClips || DualSenseMod.Haptics == null || clip == null) return;

			var tap = source.GetComponent<HapticTap>();
			if (tap == null) return;

			float[] wave = HapticClipBank.Get(clip);
			if (wave != null) tap.PlayOneShot(wave, clip.frequency, volume);
		}
	}

	[HarmonyPatch(typeof(OWAudioSource), "PlayOneShot", typeof(AudioType), typeof(float))]
	internal static class PlayOneShotPatch
	{
		private static void Postfix(OWAudioSource __instance, AudioClip __result, float volume)
		{
			OneShotHook.Play(__instance, __result, volume);
		}
	}

	[HarmonyPatch(typeof(OWAudioSource), "PlayOneShot", typeof(AudioType), typeof(int), typeof(float))]
	internal static class PlayOneShotIndexPatch
	{
		private static void Postfix(OWAudioSource __instance, AudioClip __result, float volume)
		{
			OneShotHook.Play(__instance, __result, volume);
		}
	}
}
