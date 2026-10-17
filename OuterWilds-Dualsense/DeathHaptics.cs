using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// Death haptics. The game plays one of its death sounds through PlayerDeathAudio when the player dies (which
	/// sound depends on the way of dying). Those sources carry a dynamic tap, so what is felt is the death sound
	/// itself with its own build-up, blow and fade, and each kind of death has its own strength setting.
	/// </summary>
	internal static class DeathHaptics
	{
		private static PlayerDeathAudio _audio;
		private static FieldInfo _deathSource;
		private static FieldInfo _helmetSource;
		private static FieldInfo _groanSource;

		/// <summary>Puts dynamic taps on the death sound sources. Safe to call repeatedly.</summary>
		public static void EnsureTaps()
		{
			try
			{
				if (_audio == null) _audio = UnityEngine.Object.FindObjectOfType<PlayerDeathAudio>();
				if (_audio == null) return;

				if (_deathSource == null)
				{
					var type = typeof(PlayerDeathAudio);
					const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
					_deathSource = type.GetField("_deathSource", flags);
					_helmetSource = type.GetField("_helmetCrackSource", flags);
					_groanSource = type.GetField("_shipGroanSource", flags);
				}
				foreach (var field in new[] { _deathSource, _helmetSource, _groanSource })
				{
					var source = field == null ? null : field.GetValue(_audio) as OWAudioSource;
					if (source != null) HapticTap.Attach(source, float.NaN, null, true);
				}
			}
			catch (Exception e)
			{
				DualSenseMod.Log("Death sound taps failed: " + e.Message);
			}
		}

		/// <summary>Strength setting for a death sound, or null when the sound is not a death sound.</summary>
		public static float? GainFor(AudioType type)
		{
			switch (type)
			{
				case AudioType.Death_Crushed: return Effects.Get("death.sand")["gain"];
				case AudioType.Death_CrushedByElevator: return Effects.Get("death.elevator")["gain"];
				case AudioType.Death_Instant: return Effects.Get("death.instant")["gain"];
				case AudioType.Death_Energy:
				case AudioType.Death_BigBang:
				case AudioType.Death_Digestion:
				case AudioType.Death_Self:
				case AudioType.Death_TimeLoop:
				case AudioType.Death_Lava:
					return Effects.Get("death.other")["gain"];
				default:
					return null;
			}
		}

		/// <summary>Plays a death sound from the F1 menu so it can be felt without dying.</summary>
		public static void PlaySound(AudioType type)
		{
			EnsureTaps();
			if (_audio == null || _deathSource == null) return;
			var source = _deathSource.GetValue(_audio) as OWAudioSource;
			if (source != null) source.PlayOneShot(type);
		}

		/// <summary>Called for every one-shot on a tapped source: sets the strength of the death sound that starts.</summary>
		public static void OnOneShot(OWAudioSource source, AudioType type)
		{
			float? gain = GainFor(type);
			if (!gain.HasValue) return;
			var tap = source.GetComponent<HapticTap>();
			if (tap != null) tap.OneShotGain = gain.Value;
		}
	}

	[HarmonyPatch(typeof(OWAudioSource), "PlayOneShot", typeof(AudioType), typeof(float))]
	internal static class DeathSoundPatch
	{
		private static void Postfix(OWAudioSource __instance, AudioType type)
		{
			DeathHaptics.OnOneShot(__instance, type);
		}
	}

	[HarmonyPatch(typeof(OWAudioSource), "PlayOneShot", typeof(AudioType), typeof(int), typeof(float))]
	internal static class DeathSoundIndexPatch
	{
		private static void Postfix(OWAudioSource __instance, AudioType type)
		{
			DeathHaptics.OnOneShot(__instance, type);
		}
	}
}
