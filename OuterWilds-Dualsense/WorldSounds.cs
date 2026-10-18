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
			/// <summary>A loop the game fades in and out: the haptic follows the source volume (and the distance if Distance).</summary>
			public bool Loop;
			/// <summary>The strength falls off with the distance to the sound. Off when the game does the fading by distance itself.</summary>
			public bool Distance;
			/// <summary>The strength follows the source's own 3D rolloff (what the player actually hears), not the shared reach setting.</summary>
			public bool GameRolloff;
		}

		private static readonly Rule[] Rules =
		{
			// Splashes: one-shots, strength fixed when they start.
			new Rule { ClipPrefix = "GhostMatter_Splash", EffectId = "splash.ghost", Distance = true },
			new Rule { ClipPrefix = "Lava_Splash", EffectId = "splash.lava", Distance = true },
			new Rule { ClipPrefix = "SmallSplash", EffectId = "splash.water", Distance = true },
			new Rule { ClipPrefix = "MediumSplash", EffectId = "splash.water", Distance = true },
			new Rule { ClipPrefix = "Huge Splash", EffectId = "splash.water", Distance = true },

			// Gravity crystals: the game fades the loop by the player's distance itself.
			new Rule { ClipPrefix = "OW_NM_GravityCrystalAmbience", EffectId = "grav.crystal", Loop = true },
			new Rule { ClipPrefix = "OW_NM_FlickeryGravityCrystalAmbience", EffectId = "grav.flicker", Loop = true },

			// Tractor beam (the gravity beam): its hum, and the power-up and power-down.
			new Rule { ClipPrefix = "OW_NM_TractorBeamLP", EffectId = "grav.beam", Loop = true, Distance = true },
			new Rule { ClipPrefix = "OW_NM_TractorBeamPowerUp", EffectId = "grav.beamswitch", Distance = true },
			new Rule { ClipPrefix = "OW_NM_TractorBeamPowerDown", EffectId = "grav.beamswitch", Distance = true },

			// Gravity cannon: ambient loop faded by the game when it is active, plus its start and stop.
			new Rule { ClipPrefix = "OW_NM_GravityCannonAmbience", EffectId = "grav.cannon", Loop = true, GameRolloff = true },
			new Rule { ClipPrefix = "OW_NM_GravityCannonActivated", EffectId = "grav.shot", Distance = true },
			new Rule { ClipPrefix = "OW_NM_GravityCannonDeactivated", EffectId = "grav.shot", Distance = true },
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

				Rule? found = null;
				foreach (var rule in Rules)
					if (clip.name.StartsWith(rule.ClipPrefix, StringComparison.Ordinal)) { found = rule; break; }
				if (found == null) return;
				var match = found.Value;

				var camera = Locator.GetPlayerCamera();
				if (camera == null) return;

				if (match.Loop)
				{
					AttachLoop(source, match);
					return;
				}

				Vector3 toSound = source.transform.position - camera.transform.position;
				float strength = match.Distance ? Falloff(toSound.magnitude) : 1f;
				if (strength < 0.02f)
				{
					DualSenseMod.Debug(string.Format("World sound {0} ignored: {1:F1} m away", clip.name, toSound.magnitude));
					return;
				}

				float pan = toSound.sqrMagnitude > 0.01f ? Vector3.Dot(toSound.normalized, camera.transform.right) : 0f;
				var tap = HapticTap.AttachTo(source, pan);
				if (tap == null) return;
				tap.OneShotGain = Effects.Get(match.EffectId)["gain"] * strength;
				DualSenseMod.Log(string.Format("World sound '{0}' on {1}: {2:F1} m, haptic strength {3:F2}, pan {4:F2}",
					clip.name, source.name, toSound.magnitude, tap.OneShotGain, pan));
			}
			catch (Exception e)
			{
				DualSenseMod.Debug("World sound haptic failed: " + e.Message);
			}
		}

		/// <summary>
		/// A loop the game fades in and out: strength = setting x the source's volume (x distance falloff, evaluated every
		/// frame, if the game does not fade by distance itself).
		/// </summary>
		private static void AttachLoop(AudioSource source, Rule rule)
		{
			var tap = HapticTap.AttachTo(source, float.NaN);
			if (tap == null || tap.GainOverride != null) return; // already set up
			string id = rule.EffectId;
			bool distance = rule.Distance;
			bool rolloff = rule.GameRolloff;
			tap.FollowVolume = true;
			tap.GainOverride = () =>
			{
				float gain = Effects.Get(id)["gain"];
				var camera = Locator.GetPlayerCamera();
				if (distance && camera != null && source != null)
					gain *= Falloff((source.transform.position - camera.transform.position).magnitude);
				return gain;
			};
			DualSenseMod.Log(string.Format("World loop '{0}' on {1} is tapped (3D blend {2:F2}, {3:F0}-{4:F0} m, rolloff {5})",
				source.clip.name, source.name, source.spatialBlend, source.minDistance, source.maxDistance, source.rolloffMode));
		}

		/// <summary>How loud the source is at this distance by its own Unity rolloff settings (1 = right at it, 0 = out of earshot).</summary>
		private static float GameRolloffAt(AudioSource source, float distance)
		{
			float min = source.minDistance, max = Mathf.Max(source.maxDistance, min + 0.01f);
			float level;
			if (distance <= min) level = 1f;
			else if (distance >= max) level = 0f;
			else
			{
				switch (source.rolloffMode)
				{
					case AudioRolloffMode.Linear: level = 1f - (distance - min) / (max - min); break;
					case AudioRolloffMode.Custom: level = Mathf.Clamp01(source.GetCustomCurve(AudioSourceCurveType.CustomRolloff).Evaluate((distance - min) / (max - min))); break;
					default: level = Mathf.Clamp01(min / distance); break;
				}
			}
			return Mathf.Lerp(1f, level, source.spatialBlend);
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

		private static GameObject _loopHolder;
		private static string _loopKey;

		/// <summary>F1 menu: holds a game loop next to the player while called every frame.</summary>
		public static void HoldLoop(string key, AudioType type)
		{
			if (_loopHolder != null && _loopKey == key) return;
			StopLoop();
			var camera = Locator.GetPlayerCamera();
			if (camera == null || DualSenseMod.Haptics == null) return;

			_loopKey = key;
			_loopHolder = new GameObject("DualSense loop preview");
			_loopHolder.transform.position = camera.transform.position + camera.transform.forward * 2f;
			var source = _loopHolder.AddComponent<OWAudioSource>();
			source.AssignAudioLibraryClip(type);
			source.loop = true;
			source.Play(); // the AudioSource hook sets up the haptic like for any other loop
		}

		public static void StopLoop()
		{
			if (_loopHolder != null) UnityEngine.Object.Destroy(_loopHolder);
			_loopHolder = null;
			_loopKey = null;
		}
	}
}
