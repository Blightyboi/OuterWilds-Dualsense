using System;
using System.Reflection;

namespace OuterWildsDualSense
{
	/// <summary>
	/// Haptics for the memory scenes: the flashback that plays after the player dies, and the memory uplink.
	/// FlashbackAudioController plays them on three sources: a one-shot source (slam at the start, stinger at the
	/// end), a base loop and an overlay loop whose volume the game raises and lowers to build the scene. The loops are
	/// tapped dynamically and follow their volume, so the haptic swells and fades the way the sound does.
	/// </summary>
	internal static class MemoryHaptics
	{
		private static FlashbackAudioController _controller;
		private static FieldInfo _oneShotSource;
		private static FieldInfo _baseSource;
		private static FieldInfo _overlaySource;

		public static void EnsureTaps()
		{
			try
			{
				if (_controller == null) _controller = UnityEngine.Object.FindObjectOfType<FlashbackAudioController>();
				if (_controller == null) return;

				const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
				if (_oneShotSource == null)
				{
					var type = typeof(FlashbackAudioController);
					_oneShotSource = type.GetField("_oneShotSource", flags);
					_baseSource = type.GetField("_baseSource", flags);
					_overlaySource = type.GetField("_overlaySourceOne", flags);
				}

				Tap(_oneShotSource.GetValue(_controller) as OWAudioSource, null, false);
				Tap(_baseSource.GetValue(_controller) as OWAudioSource, () => Effects.Get("mem.base")["gain"], true);
				Tap(_overlaySource.GetValue(_controller) as OWAudioSource, () => Effects.Get("mem.overlay")["gain"], true);
			}
			catch (Exception e)
			{
				DualSenseMod.Log("Memory sound taps failed: " + e.Message);
			}
		}

		private static void Tap(OWAudioSource source, Func<float> gain, bool followVolume)
		{
			if (source == null) return;
			HapticTap.Attach(source, float.NaN, null, true);
			var tap = source.GetComponent<HapticTap>();
			if (tap == null) return;
			tap.GainOverride = gain;
			tap.FollowVolume = followVolume;
		}

		/// <summary>Strength setting for the one-shots of the memory scenes, or null for other sounds.</summary>
		public static float? OneShotGain(AudioType type)
		{
			switch (type)
			{
				case AudioType.MemoryUplink_Start: return Effects.Get("mem.slam")["gain"];
				case AudioType.MemoryUplink_End:
				case AudioType.Flashback_End: return Effects.Get("mem.end")["gain"];
				default: return null;
			}
		}

		// ---- F1 menu: plays the real sounds on the game's own sources

		public static void PlayOneShot(AudioType type)
		{
			EnsureTaps();
			var source = _controller == null ? null : _oneShotSource.GetValue(_controller) as OWAudioSource;
			if (source != null) source.PlayOneShot(type);
		}

		/// <summary>Holds a loop while called every frame: starts it if it is not playing.</summary>
		public static void HoldLoop(bool overlay, AudioType type)
		{
			EnsureTaps();
			if (_controller == null) return;
			var source = (overlay ? _overlaySource : _baseSource).GetValue(_controller) as OWAudioSource;
			if (source == null || source.isPlaying) return;
			source.AssignAudioLibraryClip(type);
			source.loop = true;
			source.SetLocalVolume(1f);
			source.Play();
		}

		public static void StopLoop(bool overlay)
		{
			if (_controller == null) return;
			var source = (overlay ? _overlaySource : _baseSource).GetValue(_controller) as OWAudioSource;
			if (source != null && source.isPlaying) source.FadeOut(0.3f);
		}
	}
}
