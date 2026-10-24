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
			/// <summary>Crackling: the tap makes a short tick for every pop instead of following a steady level.</summary>
			public bool Crackle;
			/// <summary>An elevator ride: the strength follows the cab's real speed (the game's smooth-step lift), not just the fade of the loop.</summary>
			public bool Lift;
			/// <summary>The rule only applies to sources with this text in the name of the object or one of its parents (a clip the game also plays elsewhere).</summary>
			public string UnderName;
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

			// Campfires: the crackle of a lit fire (Campfire fades it in and out, half volume while smoldering). The same
			// clip also plays on the player's own source while dozing off at a fire, which AttachLoop tells apart.
			new Rule { ClipPrefix = "OW_TH_Campfire_loop", EffectId = "fire.crackle", Loop = true, GameRolloff = true, Crackle = true },
			new Rule { ClipPrefix = "Dreamfire Crackling", EffectId = "fire.crackle", Loop = true, GameRolloff = true, Crackle = true },
			new Rule { ClipPrefix = "OW_PR_CampfireAmbience", EffectId = "fire.crackle", Loop = true, GameRolloff = true, Crackle = true },
			// Lighting a fire (Campfire.SetState, burning a slide reel, the post-credits scene): a blow with real low end.
			new Rule { ClipPrefix = "Ignite_CampFire", EffectId = "fire.ignite", GameRolloff = true },
			// The miner's pickaxe on Timber Hearth: a short sharp hit with nothing in the low band, so a tick.
			new Rule { ClipPrefix = "pickaxe_", EffectId = "pick.hit", GameRolloff = true, Crackle = true },

			// Nomai interface orbs (door and planet locator orbs, light switches...), NomaiOrbAudio. The player moves them with
			// the gaze: the drag loop and the grab tick are the player's own action, so no distance; the loop's volume is the
			// game's own speed-based fade. Rolling (an orb moving on its own) and the slot click follow the sound's range.
			new Rule { ClipPrefix = "Orb_Roll_Energy_Loop", EffectId = "orb.drag", Loop = true },
			new Rule { ClipPrefix = "OW_NM_OrbMoveGlass_lp", EffectId = "orb.roll", Loop = true, GameRolloff = true },
			new Rule { ClipPrefix = "Orb_End_v3", EffectId = "orb.grab", Crackle = true },
			new Rule { ClipPrefix = "OW_NM_OrbDeSelect_Energy", EffectId = "orb.slot", GameRolloff = true, Crackle = true },

			// The Nomai signal dish ("planet locator", Attlerock): SignalDishRingController turns three rings with a loop whose
			// volume is the real ring speed (0-60 degrees a second); when all rings point at the chosen target and the player
			// stands at the dish, SignalDishController fades in that target's sound (Sun, Giant's Deep, Brittle Hollow) on its
			// own source. The three target clips are also played elsewhere, so they only count under the dish.
			new Rule { ClipPrefix = "OW_NM_DoorSlide_Big_LP_01", EffectId = "mech.big", Loop = true, GameRolloff = true },
			new Rule { ClipPrefix = "OW_SUN_Ambience", EffectId = "dish.sun", Loop = true, GameRolloff = true, UnderName = "SignalDish" },
			new Rule { ClipPrefix = "OW_GD_Tornado", EffectId = "dish.giants", Loop = true, GameRolloff = true, UnderName = "SignalDish" },
			new Rule { ClipPrefix = "OW_NM_WHAmbience2", EffectId = "dish.hollow", Loop = true, GameRolloff = true, UnderName = "SignalDish" },

			// Giant's Deep tornadoes (TornadoController): each has a source on an AudioRail that the game fades in while the player
			// or the probe is in its sector, over the formation, and out over the collapse or when the player dives under water.
			// Listed after the observatory and dish rules, which take the same clip under their own objects.
			new Rule { ClipPrefix = "OW_GD_Tornado", EffectId = "tornado.roar", Loop = true, GameRolloff = true },

			// Nomai holograms (HologramProjector: Brittle Hollow's observatory with its six orb tracks, Giant's Deep, the
			// orbital probe cannon): the hologram appearing and fading, a long swell with a lot of low end. The projector's
			// loop (NomaiHologram_LP) has no clip in the game's sound table, so there is nothing to follow there.
			// Brittle Hollow's southern observatory: the simulation of Giant's Deep tornadoes (Audio_Observatory, two AudioRail
			// sources, UpTornado and DownTornado, that slide along a rail to the point nearest the player). The same clip is
			// the real tornadoes of Giant's Deep, which are not covered yet, so it only counts under the observatory's audio.
			new Rule { ClipPrefix = "OW_GD_Tornado", EffectId = "obs.tornado", Loop = true, GameRolloff = true, UnderName = "Audio_Observatory" },
			new Rule { ClipPrefix = "Hologram_Enter_v2", EffectId = "hologram.shot", GameRolloff = true },
			new Rule { ClipPrefix = "Hologram_Exit_v2", EffectId = "hologram.shot", GameRolloff = true },

			// Timber Hearth: the elevator (Elevator: start and arrival one-shots, a loop it fades in while it runs), geysers
			// (GeyserAudioController), the water wheel, the waterfall and the rush under water. Strength follows the
			// source's own 3D rolloff, what the player actually hears.
			new Rule { ClipPrefix = "elevatorstart", EffectId = "lift.shot", GameRolloff = true },
			new Rule { ClipPrefix = "elevatorstop", EffectId = "lift.shot", GameRolloff = true },
			new Rule { ClipPrefix = "elevatorloop", EffectId = "lift.loop", Loop = true, GameRolloff = true, Lift = true },
			new Rule { ClipPrefix = "OW_TH_GeyserStart", EffectId = "geyser.shot", GameRolloff = true },
			new Rule { ClipPrefix = "OW_TH_GeyserEnd", EffectId = "geyser.shot", GameRolloff = true },
			new Rule { ClipPrefix = "TH_Geyser_Loop", EffectId = "geyser.loop", Loop = true, GameRolloff = true },
			new Rule { ClipPrefix = "OW_TH_Waterwheel_loop", EffectId = "water.wheel", Loop = true, GameRolloff = true },
			new Rule { ClipPrefix = "OW_TH_Waterfall_loop", EffectId = "water.fall", Loop = true, GameRolloff = true },
			new Rule { ClipPrefix = "OW_TH_Underwater", EffectId = "water.under", Loop = true },
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
					if (clip.name.StartsWith(rule.ClipPrefix, StringComparison.Ordinal) && (rule.UnderName == null || IsUnder(source, rule.UnderName))) { found = rule; break; }
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
				float strength = match.Distance ? Falloff(toSound.magnitude) : match.GameRolloff ? GameRolloffAt(source, toSound.magnitude) : 1f;
				if (strength < 0.02f)
				{
					DualSenseMod.Debug(string.Format("World sound {0} ignored: {1:F1} m away", clip.name, toSound.magnitude));
					return;
				}

				float pan = toSound.sqrMagnitude > 0.01f ? Vector3.Dot(toSound.normalized, camera.transform.right) : 0f;
				var tap = HapticTap.AttachTo(source, pan);
				if (tap == null) return;
				var fx = Effects.Get(match.EffectId);
				if (match.Crackle)
				{
					tap.UiProfile = true;
					tap.ToneHz = fx["tone"];
					tap.ReleaseHz = fx["release"];
				}
				tap.OneShotGain = fx["gain"] * strength;
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

			// The crackle also plays on the player's own source while dozing off at a fire: that one has its own strength and
			// silences the fires in the world meanwhile, so the two are not felt on top of each other.
			bool sleeping = rule.Crackle && IsPlayerSource(source);
			string id = sleeping ? "fire.sleep" : rule.EffectId;
			if (sleeping) _sleepSource = source;
			bool distance = rule.Distance;
			bool rolloff = rule.GameRolloff && !sleeping;
			bool crackle = rule.Crackle;
			Func<float> ride = rule.Lift ? LiftSpeed(source) : null;
			tap.FollowVolume = true;
			if (crackle)
			{
				// Pops, not a steady level: a short tick for every pop (the interface profile with its own tone and length).
				tap.UiProfile = true;
				var fx = Effects.Get(id);
				tap.ToneHz = fx["tone"];
				tap.ReleaseHz = fx["release"];
			}
			tap.GainOverride = () =>
			{
				var fx = Effects.Get(id);
				if (crackle)
				{
					tap.ToneHz = fx["tone"];
					tap.ReleaseHz = fx["release"];
				}
				float gain = fx["gain"];
				var camera = Locator.GetPlayerCamera();
				if (source == null || camera == null) return gain;
				float meters = (source.transform.position - camera.transform.position).magnitude;
				if (distance) gain *= Falloff(meters);
				else if (rolloff) gain *= GameRolloffAt(source, meters);
				if (ride != null) gain *= ride();
				if (crackle && !sleeping && _sleepSource != null && _sleepSource.isPlaying && _sleepSource.volume > 0.01f) gain = 0f;
				return gain;
			};
			var path = new System.Text.StringBuilder(source.name);
			for (Transform up = source.transform.parent; up != null && path.Length < 160; up = up.parent) path.Append(" < ").Append(up.name);
			DualSenseMod.Log(string.Format("World loop '{0}' on {1} is tapped as {2} (3D blend {3:F2}, {4:F0}-{5:F0} m, rolloff {6}, zone volume {7})",
				source.clip.name, path, id, source.spatialBlend, source.minDistance, source.maxDistance, source.rolloffMode, source.GetComponent<AudioVolume>() != null));
		}

		private static bool IsUnder(AudioSource source, string text)
		{
			for (Transform t = source.transform; t != null; t = t.parent)
				if (t.name.IndexOf(text, StringComparison.Ordinal) >= 0) return true;
			return false;
		}

		private static AudioSource _sleepSource;

		private static readonly System.Reflection.FieldInfo LiftStart = typeof(Elevator).GetField("_initLiftTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		private static readonly System.Reflection.FieldInfo LiftLength = typeof(Elevator).GetField("_liftDuration", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

		/// <summary>
		/// The share of the top speed the elevator has right now. Elevator.Update moves the cab with a smooth step over
		/// _liftDuration, so its speed is 6t(1-t) of the way per duration; divided by its peak (1.5) that is 4t(1-t).
		/// Returns null when the elevator or its fields cannot be found (the loop then just follows its fade).
		/// </summary>
		private static Func<float> LiftSpeed(AudioSource source)
		{
			var lift = source.GetComponentInParent<Elevator>();
			if (lift == null)
			{
				float best = 5f;
				foreach (var other in UnityEngine.Object.FindObjectsOfType<Elevator>())
				{
					float d = (other.transform.position - source.transform.position).magnitude;
					if (d < best) { best = d; lift = other; }
				}
			}
			if (lift == null || LiftStart == null || LiftLength == null)
			{
				DualSenseMod.Log("Elevator loop on " + source.name + ": no Elevator found, the ride follows the loop's fade only");
				return null;
			}
			DualSenseMod.Log("Elevator loop on " + source.name + " follows " + lift.name);
			return () =>
			{
				if (lift == null) return 0f;
				float t = Mathf.Clamp01((Time.time - (float)LiftStart.GetValue(lift)) / Mathf.Max((float)LiftLength.GetValue(lift), 0.1f));
				return 4f * t * (1f - t);
			};
		}

		/// <summary>True for a source that belongs to the player (the one that plays the fire while dozing off) or the F1 sleep preview.</summary>
		private static bool IsPlayerSource(AudioSource source)
		{
			if (source.name == SleepPreviewName) return true;
			var player = Locator.GetPlayerTransform();
			return source.GetComponentInParent<PlayerAudioController>() != null || (player != null && source.transform.IsChildOf(player));
		}

		public const string SleepPreviewName = "DualSense sleep preview";

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
		public static void HoldLoop(string key, AudioType type, string holderName = "DualSense loop preview")
		{
			if (_loopHolder != null && _loopKey == key) return;
			StopLoop();
			var camera = Locator.GetPlayerCamera();
			if (camera == null || DualSenseMod.Haptics == null) return;

			_loopKey = key;
			_loopHolder = new GameObject(holderName);
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
