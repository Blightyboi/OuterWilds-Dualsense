using System;
using HarmonyLib;
using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// Haptics for what happens to the character on foot: jumping, landing, bumping into things and taking damage.
	/// Every effect is triggered by the game's own event (the jump, the impact it decided to play a sound for, the
	/// instant damage), never by measuring audio.
	/// </summary>
	internal static class PlayerHaptics
	{
		public static void Jump(float delay = 0f)
		{
			var fx = Effects.Get("char.jump");
			HapticSynth.Play(HapticSynth.Thump(fx["freq"], fx["durationMs"] / 1000f, fx["strength"], false, delay));
		}

		/// <summary>Landing on the ground. intensity 0..1 is the game's own measure of how hard the landing was.</summary>
		public static void Landing(FootstepHaptics.Ground ground, float intensity, float delay = 0f)
		{
			var fx = Effects.Get("char.landing");
			float mul = ground == FootstepHaptics.Ground.Soft ? fx["softMul"] : ground == FootstepHaptics.Ground.Hard ? fx["hardMul"] : fx["mediumMul"];
			float amp = fx["strength"] * mul * (0.3f + 0.7f * Mathf.Clamp01(intensity));
			float freq = fx["freq"] + (ground == FootstepHaptics.Ground.Hard ? 15f : 0f);
			HapticSynth.Play(HapticSynth.Thump(freq, fx["durationMs"] / 1000f, amp, ground == FootstepHaptics.Ground.Hard, delay));
		}

		/// <summary>Bumping into something (not a landing). level: 0 light, 1 medium, 2 heavy.</summary>
		public static void Hit(int level, float delay = 0f)
		{
			var fx = Effects.Get("char.hit");
			string key = level == 0 ? "light" : level == 1 ? "medium" : "heavy";
			HapticSynth.Play(HapticSynth.Thump(fx[key + "Freq"], fx[key + "Ms"] / 1000f, fx[key + "Strength"], level == 0, delay));
		}

		public static void Damage(InstantDamageType type, float damage, float delay = 0f)
		{
			var fx = Effects.Get("char.damage");
			float scale = 0.4f + 0.6f * Mathf.Clamp01(damage / 50f);
			switch (type)
			{
				case InstantDamageType.Impact:
					HapticSynth.Play(HapticSynth.Thump(fx["impactFreq"], 0.2f, fx["impactStrength"] * scale, false, delay));
					break;
				case InstantDamageType.Puncture:
					HapticSynth.Play(HapticSynth.Buzz(fx["punctureFreq"], fx["punctureMs"] / 1000f, fx["punctureStrength"] * scale, 0f, delay));
					break;
				default: // electrical
					HapticSynth.Play(HapticSynth.Buzz(fx["electricFreq"], fx["electricMs"] / 1000f, fx["electricStrength"] * scale, fx["electricChop"], delay));
					break;
			}
		}

		// ---- Previews for the F1 menu

		public static void PreviewLanding()
		{
			Landing(FootstepHaptics.Ground.Soft, 1f, 0f);
			Landing(FootstepHaptics.Ground.Medium, 1f, 0.7f);
			Landing(FootstepHaptics.Ground.Hard, 1f, 1.4f);
		}

		public static void PreviewHit()
		{
			Hit(0, 0f);
			Hit(1, 0.7f);
			Hit(2, 1.5f);
		}

		public static void PreviewDamage()
		{
			Damage(InstantDamageType.Impact, 50f, 0f);
			Damage(InstantDamageType.Puncture, 50f, 0.8f);
			Damage(InstantDamageType.Electrical, 50f, 1.6f);
		}

		/// <summary>Which kind of ground a landing sound belongs to, or null if the sound is not a landing.</summary>
		public static FootstepHaptics.Ground? GroundOfLanding(AudioType type)
		{
			switch (type)
			{
				case AudioType.LandingGrass:
				case AudioType.LandingSand:
				case AudioType.LandingWater:
				case AudioType.MovementLeavesLanding:
				case AudioType.MovementSnowLanding:
				case AudioType.Splash_Water_Probe:
				case AudioType.Raft_Impact_Player:
					return FootstepHaptics.Ground.Soft;
				case AudioType.LandingStone:
				case AudioType.LandingIce:
				case AudioType.LandingMetal:
				case AudioType.LandingNomaiMetal:
				case AudioType.MovementGlassLanding:
					return FootstepHaptics.Ground.Hard;
				case AudioType.LandingDirt:
				case AudioType.MovementGravelLanding:
				case AudioType.MovementWoodLanding:
				case AudioType.MovementWoodCreakLanding:
					return FootstepHaptics.Ground.Medium;
				default:
					return null;
			}
		}
	}

	[HarmonyPatch(typeof(PlayerMovementAudio), "OnJump")]
	internal static class JumpPatch
	{
		private static void Postfix()
		{
			PlayerHaptics.Jump();
		}
	}

	[HarmonyPatch(typeof(PlayerResources), "ApplyInstantDamage")]
	internal static class DamagePatch
	{
		private static void Postfix(bool __result, float damage, InstantDamageType type)
		{
			if (__result) PlayerHaptics.Damage(type, damage);
		}
	}

	/// <summary>
	/// PlayerImpactAudio decides how a collision of the player is handled (cooldowns, landing or side hit, how hard)
	/// and plays one sound for it. That decision is the event: it is read where the game makes it.
	/// </summary>
	internal static class PlayerImpactHook
	{
		public static void Handle(OWAudioSource source, AudioType type, float volume)
		{
			var mod = DualSenseMod.Instance;
			if (mod == null || !mod.Enabled || !mod.AudioHaptics) return;
			if (source.GetComponent<PlayerImpactAudio>() == null) return;

			var ground = PlayerHaptics.GroundOfLanding(type);
			if (ground.HasValue) PlayerHaptics.Landing(ground.Value, volume);
			else if (type == AudioType.ImpactLowSpeed) PlayerHaptics.Hit(0);
			else if (type == AudioType.ImpactMediumSpeed) PlayerHaptics.Hit(1);
			else if (type == AudioType.ImpactHighSpeed) PlayerHaptics.Hit(2);
		}
	}

	[HarmonyPatch(typeof(OWAudioSource), "PlayOneShot", typeof(AudioType), typeof(float))]
	internal static class PlayerImpactPatch
	{
		private static void Postfix(OWAudioSource __instance, AudioType type, float volume)
		{
			PlayerImpactHook.Handle(__instance, type, volume);
		}
	}

	[HarmonyPatch(typeof(OWAudioSource), "PlayOneShot", typeof(AudioType), typeof(int), typeof(float))]
	internal static class PlayerImpactIndexPatch
	{
		private static void Postfix(OWAudioSource __instance, AudioType type, float volume)
		{
			PlayerImpactHook.Handle(__instance, type, volume);
		}
	}
}
