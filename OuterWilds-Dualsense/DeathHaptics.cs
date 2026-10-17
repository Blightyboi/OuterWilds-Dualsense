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
	/// Inside a ship two more sounds can join: the ship groaning when the player is digested, and the ship
	/// exploding.
	/// </summary>
	internal static class DeathHaptics
	{
		private static PlayerDeathAudio _audio;
		private static FieldInfo _deathSource;
		private static FieldInfo _helmetSource;
		private static FieldInfo _groanSource;
		private static ShipAudioController _shipAudio;
		private static FieldInfo _loudShipSource;

		/// <summary>True when the ship that is exploding is close enough to hit the player.</summary>
		public static bool ShipExplosionNear;

		/// <summary>Puts dynamic taps on the death sound sources. Safe to call repeatedly.</summary>
		public static void EnsureTaps()
		{
			try
			{
				const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

				if (_audio == null) _audio = UnityEngine.Object.FindObjectOfType<PlayerDeathAudio>();
				if (_audio != null)
				{
					if (_deathSource == null)
					{
						var type = typeof(PlayerDeathAudio);
						_deathSource = type.GetField("_deathSource", flags);
						_helmetSource = type.GetField("_helmetCrackSource", flags);
						_groanSource = type.GetField("_shipGroanSource", flags);
					}
					Tap(_deathSource.GetValue(_audio) as OWAudioSource, null, false);
					Tap(_helmetSource.GetValue(_audio) as OWAudioSource, null, false);
					Tap(_groanSource.GetValue(_audio) as OWAudioSource, () => Effects.Get("death.groan")["gain"], false);
				}

				if (_shipAudio == null) _shipAudio = UnityEngine.Object.FindObjectOfType<ShipAudioController>();
				if (_shipAudio != null)
				{
					if (_loudShipSource == null) _loudShipSource = typeof(ShipAudioController).GetField("_loudShipSource", flags);
					// This source plays other loud ship sounds as well: only the explosion is turned into haptics.
					Tap(_loudShipSource.GetValue(_shipAudio) as OWAudioSource, null, true);
				}
			}
			catch (Exception e)
			{
				DualSenseMod.Log("Death sound taps failed: " + e.Message);
			}
		}

		private static void Tap(OWAudioSource source, Func<float> gain, bool strict)
		{
			if (source == null) return;
			HapticTap.Attach(source, float.NaN, null, true);
			var tap = source.GetComponent<HapticTap>();
			if (tap == null) return;
			tap.GainOverride = gain;
			tap.Strict = strict;
			if (strict) tap.OneShotGain = 0f;
		}

		/// <summary>Strength setting for a sound, or null when the sound is not one of the death sounds.</summary>
		public static float? GainFor(AudioType type)
		{
			switch (type)
			{
				case AudioType.Death_Crushed: return Effects.Get("death.sand")["gain"];
				case AudioType.Death_CrushedByElevator: return Effects.Get("death.elevator")["gain"];
				case AudioType.Death_Instant: return Effects.Get("death.instant")["gain"];
				case AudioType.Death_Energy: return Effects.Get("death.energy")["gain"];
				case AudioType.Death_BigBang: return Effects.Get("death.bigbang")["gain"];
				case AudioType.Death_Digestion: return Effects.Get("death.digestion")["gain"];
				case AudioType.Death_Lava: return Effects.Get("death.lava")["gain"];
				case AudioType.Death_TimeLoop: return Effects.Get("death.timeloop")["gain"];
				case AudioType.PlayerSuitHelmetCrack: return Effects.Get("death.helmet")["gain"];
				case AudioType.ShipDamageShipExplosion: return ShipExplosionNear ? Effects.Get("death.shipexplosion")["gain"] : 0f;
				default: return null;
			}
		}

		public enum Where { Death, Helmet, Groan, ShipExplosion }

		/// <summary>Plays a game sound from the F1 menu so it can be felt without dying.</summary>
		public static void PlaySound(AudioType type, Where where = Where.Death)
		{
			EnsureTaps();
			try
			{
				switch (where)
				{
					case Where.Death:
						if (_audio != null) (_deathSource.GetValue(_audio) as OWAudioSource).PlayOneShot(type);
						break;
					case Where.Helmet:
						if (_audio != null) (_helmetSource.GetValue(_audio) as OWAudioSource).PlayOneShot(type);
						break;
					case Where.Groan:
						if (_audio != null) (_groanSource.GetValue(_audio) as OWAudioSource).Play();
						break;
					case Where.ShipExplosion:
						ShipExplosionNear = true;
						if (_shipAudio != null) (_loudShipSource.GetValue(_shipAudio) as OWAudioSource).PlayOneShot(type);
						break;
				}
			}
			catch (Exception e)
			{
				DualSenseMod.Log("Could not play the sound: " + e.Message);
			}
		}

		/// <summary>Called for every one-shot on a tapped source: sets the strength of the sound that starts.</summary>
		public static void OnOneShot(OWAudioSource source, AudioType type, AudioClip clip)
		{
			var tap = source.GetComponent<HapticTap>();
			if (tap == null) return;
			float? gain = GainFor(type);
			if (gain.HasValue)
			{
				tap.OneShotGain = gain.Value;
				DualSenseMod.Log(string.Format("Death sound {0}: clip '{1}', {2:F2} s, strength {3:F2}",
					type, clip != null ? clip.name : "?", clip != null ? clip.length : 0f, gain.Value));
			}
			else if (tap.Strict)
			{
				tap.OneShotGain = 0f;
			}
		}
	}

	[HarmonyPatch(typeof(OWAudioSource), "PlayOneShot", typeof(AudioType), typeof(float))]
	internal static class DeathSoundPatch
	{
		private static void Postfix(OWAudioSource __instance, AudioType type, AudioClip __result)
		{
			DeathHaptics.OnOneShot(__instance, type, __result);
		}
	}

	[HarmonyPatch(typeof(OWAudioSource), "PlayOneShot", typeof(AudioType), typeof(int), typeof(float))]
	internal static class DeathSoundIndexPatch
	{
		private static void Postfix(OWAudioSource __instance, AudioType type, AudioClip __result)
		{
			DeathHaptics.OnOneShot(__instance, type, __result);
		}
	}

	/// <summary>Whether the exploding ship is close enough to hit the player, decided the way the game decides it.</summary>
	[HarmonyPatch(typeof(ExplosionController), "Play")]
	internal static class ShipExplosionPatch
	{
		private static void Prefix(ExplosionController __instance)
		{
			try
			{
				var sphere = __instance.GetComponent<SphereCollider>();
				float reach = __instance.transform.localScale.x * (sphere != null ? sphere.radius : 0f);
				DeathHaptics.ShipExplosionNear = Vector3.Distance(__instance.transform.position, Locator.GetPlayerTransform().position) < reach;
			}
			catch (Exception)
			{
				DeathHaptics.ShipExplosionNear = false;
			}
		}
	}
}
