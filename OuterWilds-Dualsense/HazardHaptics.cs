using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// Haptics for the sounds a hazard makes while it hurts the player: ghost matter, fire, heat and the rest.
	/// PlayerAudioController.UpdateHazardDamage starts the right loop on one of two sources and fades it when the
	/// damage stops. The strength follows what the game computes, the damage per second the hazard does, so a
	/// thin edge of ghost matter is felt softer than the middle of it.
	/// </summary>
	internal static class HazardHaptics
	{
		private static PlayerAudioController _audio;
		private static OWAudioSource _internalSource;   // plays the heat loop
		private static OWAudioSource _externalSource;   // plays the ghost matter, fire and other loops

		// Read every frame from UpdateHazardDamage.
		private static HazardVolume.HazardType _type = HazardVolume.HazardType.NONE;
		private static float _damagePerSecond;

		// Set by the F1 menu to try a hazard without being hurt.
		private static HazardVolume.HazardType _previewType = HazardVolume.HazardType.NONE;
		private static float _previewUntil;

		private static bool Previewing { get { return Time.unscaledTime < _previewUntil; } }

		public static void EnsureTaps()
		{
			try
			{
				if (_audio == null) _audio = UnityEngine.Object.FindObjectOfType<PlayerAudioController>();
				if (_audio == null) return;

				const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
				if (_internalSource == null)
					_internalSource = typeof(PlayerAudioController).GetField("_damageAudioSource", flags).GetValue(_audio) as OWAudioSource;
				if (_externalSource == null)
					_externalSource = typeof(PlayerAudioController).GetField("_damageAudioSourceExternal", flags).GetValue(_audio) as OWAudioSource;

				Tap(_internalSource);
				Tap(_externalSource);
			}
			catch (Exception e)
			{
				DualSenseMod.Log("Hazard sound taps failed: " + e.Message);
			}
		}

		private static void Tap(OWAudioSource source)
		{
			if (source == null) return;
			HapticTap.Attach(source, float.NaN, Intensity);
			var tap = source.GetComponent<HapticTap>();
			if (tap != null) tap.ScaleProvider = Strength;
		}

		private static HazardVolume.HazardType ActiveType { get { return Previewing ? _previewType : _type; } }

		/// <summary>0..1: how hard the hazard hurts compared with the damage counted as full strength.</summary>
		private static float Intensity()
		{
			if (Previewing) return 0.7f;
			float full = Mathf.Max(Effects.Get("hazard.live")["fullDamage"], 0.1f);
			return Mathf.Clamp01(_damagePerSecond / full);
		}

		/// <summary>The strength setting of the hazard that is playing.</summary>
		private static float Strength()
		{
			switch (ActiveType)
			{
				case HazardVolume.HazardType.DARKMATTER: return Effects.Get("hazard.ghost")["gain"];
				case HazardVolume.HazardType.FIRE: return Effects.Get("hazard.fire")["gain"];
				default: return Effects.Get("hazard.heat")["gain"];
			}
		}

		public static void Observe(float damage, HazardDetector detector)
		{
			_type = detector.GetLatestHazardType();
			_damagePerSecond = damage > 0f ? damage / Mathf.Max(Time.deltaTime, 0.0001f) : 0f;
			if (damage > 0f && DualSenseMod.Instance != null && DualSenseMod.Instance.DebugLog)
				DualSenseMod.Debug(string.Format("Hazard {0}: {1:F1} damage per second", _type, _damagePerSecond));
		}

		/// <summary>F1 menu: plays the loop of a hazard like the game does while it hurts. Call every frame to hold it.</summary>
		public static void Preview(HazardVolume.HazardType type)
		{
			EnsureTaps();
			if (_audio == null) return;
			_previewType = type;
			_previewUntil = Time.unscaledTime + 0.4f;

			var source = type == HazardVolume.HazardType.HEAT ? _internalSource : _externalSource;
			if (source == null || source.isPlaying) return;

			AudioType sound = type == HazardVolume.HazardType.DARKMATTER ? AudioType.EnterVolumeDamageGhostfire_LP
				: type == HazardVolume.HazardType.FIRE ? AudioType.EnterVolumeDamageFire_LP
				: AudioType.EnterVolumeDamageHeat_LP;
			var manager = Locator.GetAudioManager();
			if (manager == null) return;
			source.clip = manager.GetSingleAudioClip(sound);
			source.Stop();
			source.FadeIn(0.2f, true, true);
		}

		public static void StopPreview()
		{
			_previewUntil = 0f;
			// The game fades the loop itself when a real hazard ends; a previewed one is faded here.
			if (_type == HazardVolume.HazardType.NONE)
			{
				if (_internalSource != null && _internalSource.isPlaying) _internalSource.FadeOut(0.4f);
				if (_externalSource != null && _externalSource.isPlaying) _externalSource.FadeOut(0.4f);
			}
		}
	}

	[HarmonyPatch(typeof(PlayerAudioController), "UpdateHazardDamage")]
	internal static class HazardDamagePatch
	{
		private static void Postfix(float damage, HazardDetector hazardDetector)
		{
			HazardHaptics.Observe(damage, hazardDetector);
		}
	}
}
