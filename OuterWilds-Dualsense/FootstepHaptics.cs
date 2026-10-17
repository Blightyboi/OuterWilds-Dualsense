using System;
using HarmonyLib;
using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// A haptic thump for every footstep. The trigger is the game's own footstep event
	/// (PlayerMovementAudio.PlayFootstep), so it lands with the foot and not with the sound. Strength depends on
	/// the ground (soft/medium/hard) and on how fast the player moves, and the thump is panned to the foot.
	/// </summary>
	internal static class FootstepHaptics
	{
		public enum Ground { Soft, Medium, Hard }

		public static Ground Classify(SurfaceType surface)
		{
			switch (surface)
			{
				case SurfaceType.Grass:
				case SurfaceType.Fabric:
				case SurfaceType.Foliage:
				case SurfaceType.Sand:
				case SurfaceType.Snow:
				case SurfaceType.Water:
					return Ground.Soft;
				case SurfaceType.Stone:
				case SurfaceType.Bone:
				case SurfaceType.Vine:
				case SurfaceType.QuantumRock:
				case SurfaceType.Obsidian:
				case SurfaceType.Crystal:
				case SurfaceType.Glass:
				case SurfaceType.Ceramic:
				case SurfaceType.Metal:
				case SurfaceType.MetalNomai:
				case SurfaceType.Ice:
					return Ground.Hard;
				default:
					return Ground.Medium; // dirt, gravel, wood, planks
			}
		}

		/// <summary>Plays one step. foot: -1 left, +1 right. speedFraction: 0 standing .. 1 running. delaySeconds: for previews.</summary>
		public static void Play(int foot, Ground ground, float speedFraction, float delaySeconds = 0f)
		{
			var haptics = DualSenseMod.Haptics;
			var mod = DualSenseMod.Instance;
			if (haptics == null || mod == null || !mod.Enabled || !mod.AudioHaptics) return;

			var step = Effects.Get("step.base");
			var surface = Effects.Get("step.surface");

			float surfaceMul = ground == Ground.Soft ? surface["softMul"] : ground == Ground.Hard ? surface["hardMul"] : surface["mediumMul"];
			float freq = step["freq"] + (ground == Ground.Hard ? surface["hardFreqAdd"] : ground == Ground.Soft ? -surface["softFreqSub"] : 0f);
			float speedFactor = Mathf.Lerp(1f - step["speedScale"], 1f, Mathf.Clamp01(speedFraction));
			float amplitude = step["strength"] * surfaceMul * speedFactor;

			float[] wave = HapticSynth.Thump(freq, step["durationMs"] / 1000f, amplitude, ground == Ground.Hard, delaySeconds);
			HapticSynth.Play(wave, foot * step["panAmount"]);
		}

		/// <summary>Preview: soft, medium and hard ground one after another, so they can be compared.</summary>
		public static void PreviewSurfaces()
		{
			Play(-1, Ground.Soft, 0.6f, 0f);
			Play(1, Ground.Soft, 0.6f, 0.45f);
			Play(-1, Ground.Medium, 0.6f, 1.1f);
			Play(1, Ground.Medium, 0.6f, 1.55f);
			Play(-1, Ground.Hard, 0.6f, 2.2f);
			Play(1, Ground.Hard, 0.6f, 2.65f);
		}

		/// <summary>Preview: walking and then running on medium ground.</summary>
		public static void PreviewGait()
		{
			for (int i = 0; i < 4; i++) Play(i % 2 == 0 ? -1 : 1, Ground.Medium, 0.4f, i * 0.55f);
			for (int i = 0; i < 4; i++) Play(i % 2 == 0 ? -1 : 1, Ground.Medium, 1f, 2.6f + i * 0.33f);
		}
	}

	[HarmonyPatch(typeof(PlayerMovementAudio))]
	internal static class FootstepPatches
	{
		private static int _foot = 1;

		// The game calls PlayFootstep from the left/right foot events, so note which foot it is first.
		[HarmonyPrefix]
		[HarmonyPatch("OnLeftFootGrounded")]
		private static void LeftFoot() { _foot = -1; }

		[HarmonyPrefix]
		[HarmonyPatch("OnRightFootGrounded")]
		private static void RightFoot() { _foot = 1; }

		[HarmonyPostfix]
		[HarmonyPatch("PlayFootstep")]
		private static void Step()
		{
			try
			{
				var player = Locator.GetPlayerController();
				if (player == null) return;

				float run = Mathf.Max(player.GetRunSpeedMagnitude(), 0.1f);
				float speed = Mathf.Clamp01(player.GetRelativeGroundVelocity().magnitude / run);
				FootstepHaptics.Play(_foot, FootstepHaptics.Classify(player.GetGroundSurface()), speed);
			}
			catch (Exception e)
			{
				DualSenseMod.Debug("Footstep haptic failed: " + e.Message);
			}
		}
	}
}
