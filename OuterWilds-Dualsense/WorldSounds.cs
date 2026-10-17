using System;
using HarmonyLib;
using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// Haptics for sounds that happen around the player, tied to the sound itself: whenever any Unity AudioSource in the
	/// game starts a clip whose name is on the list below, that source gets a dynamic tap, so the haptic follows
	/// exactly the clip that plays (the game picks one of several at random). It does not matter who started the
	/// sound. The strength falls off with the distance between the source and the player and the haptic leans toward
	/// the side the sound comes from.
	/// </summary>
	internal static class WorldSounds
	{
		private struct Rule
		{
			public string ClipPrefix;
			public string EffectId;
		}

		private static readonly Rule[] Rules =
		{
			new Rule { ClipPrefix = "GhostMatter_Splash", EffectId = "splash.ghost" },
			new Rule { ClipPrefix = "Lava_Splash", EffectId = "splash.lava" },
			new Rule { ClipPrefix = "SmallSplash", EffectId = "splash.water" },
			new Rule { ClipPrefix = "MediumSplash", EffectId = "splash.water" },
			new Rule { ClipPrefix = "Huge Splash", EffectId = "splash.water" },
		};

		/// <summary>Patches the AudioSource methods that start a sound. Unity's own methods, so done by hand.</summary>
		public static void Install(Harmony harmony)
		{
			Patch(harmony, new Type[0], "Play", "AfterPlay");
			Patch(harmony, new[] { typeof(ulong) }, "Play", "AfterPlay");
			Patch(harmony, new[] { typeof(float) }, "PlayDelayed", "AfterPlay");
			Patch(harmony, new[] { typeof(AudioClip) }, "PlayOneShot", "AfterOneShot");
			Patch(harmony, new[] { typeof(AudioClip), typeof(float) }, "PlayOneShot", "AfterOneShot");
		}

		private static void Patch(Harmony harmony, Type[] arguments, string method, string postfix)
		{
			try
			{
				var original = typeof(AudioSource).GetMethod(method, arguments);
				var after = new HarmonyMethod(typeof(WorldSounds).GetMethod(postfix, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
				harmony.Patch(original, null, after);
			}
			catch (Exception e)
			{
				DualSenseMod.Log("Could not watch AudioSource." + method + ": " + e.Message);
			}
		}

		private static void AfterPlay(AudioSource __instance)
		{
			Started(__instance, __instance.clip);
		}

		private static void AfterOneShot(AudioSource __instance, AudioClip clip)
		{
			Started(__instance, clip);
		}

		private static void Started(AudioSource source, AudioClip clip)
		{
			var mod = DualSenseMod.Instance;
			if (mod == null || clip == null || source == null) return;

			try
			{
				SoundWatch.Report(source, clip);
				if (!mod.Enabled || !mod.AudioHaptics || DualSenseMod.Haptics == null) return;

				string effectId = null;
				foreach (var rule in Rules)
					if (clip.name.StartsWith(rule.ClipPrefix, StringComparison.Ordinal)) { effectId = rule.EffectId; break; }
				if (effectId == null) return;

				var camera = Locator.GetPlayerCamera();
				if (camera == null) return;

				Vector3 toSound = source.transform.position - camera.transform.position;
				float strength = Falloff(toSound.magnitude);
				if (strength < 0.02f)
				{
					DualSenseMod.Debug(string.Format("World sound {0} ignored: {1:F1} m away", clip.name, toSound.magnitude));
					return;
				}

				float pan = toSound.sqrMagnitude > 0.01f ? Vector3.Dot(toSound.normalized, camera.transform.right) : 0f;
				var tap = HapticTap.AttachTo(source, pan);
				if (tap == null) return;
				tap.OneShotGain = Effects.Get(effectId)["gain"] * strength;
				DualSenseMod.Log(string.Format("World sound '{0}' on {1}: {2:F1} m, haptic strength {3:F2}, pan {4:F2}",
					clip.name, source.name, toSound.magnitude, tap.OneShotGain, pan));
			}
			catch (Exception e)
			{
				DualSenseMod.Debug("World sound haptic failed: " + e.Message);
			}
		}

		/// <summary>0 when the sound is out of reach, 1 right at the player.</summary>
		private static float Falloff(float distance)
		{
			var world = Effects.Get("splash.world");
			float range = Mathf.Max(world["range"], 0.1f);
			return Mathf.Pow(Mathf.Clamp01(1f - distance / range), world["curve"]);
		}

		/// <summary>F1 menu: plays a game sound next to the player so the haptic of its group can be tried.</summary>
		public static void Preview(AudioType type)
		{
			var mod = DualSenseMod.Instance;
			var camera = Locator.GetPlayerCamera();
			if (mod == null || DualSenseMod.Haptics == null || camera == null)
			{
				DualSenseMod.Log("Sound preview: haptics are not running");
				return;
			}
			DualSenseMod.Log("Sound preview: " + type);

			var holder = new GameObject("DualSense sound preview");
			holder.transform.position = camera.transform.position + camera.transform.forward * 2f;
			var source = holder.AddComponent<OWAudioSource>();
			source.PlayOneShot(type); // the AudioSource hook above does the rest, like for any other sound
			UnityEngine.Object.Destroy(holder, 5f);
		}
	}
}
