using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// Haptics for the sounds of the menus and the ship log. The game plays them as one-shots on a few audio sources
	/// (MenuAudioController, and the ship log controller and modes). Those sources carry a dynamic tap with the UI
	/// profile: nearly all of these sounds are clicks and chimes with nothing in the low band, so the haptic is a
	/// short tick that follows the sound's own loudness and length. Each kind of sound has its own strength.
	/// </summary>
	internal static class UiHaptics
	{
		/// <summary>
		/// A UI sound is about to be felt: sets the strength, the tone and the tick length of the tap from the settings of
		/// that very sound. Returns the strength, or null when the sound is not an interface sound.
		/// </summary>
		public static float? Apply(HapticTap tap, AudioType type)
		{
			UiSound sound;
			if (!UiSounds.TryGet(type, out sound)) return null;

			var fx = Effects.Get(sound.Id);
			float volume = 1f;
			try
			{
				volume = Locator.GetAudioManager().GetAudioEntry(type).volume;
			}
			catch (Exception)
			{
				// No entry: the sound plays at full volume.
			}

			// The game plays some of these very quietly (0.05); a root of the volume keeps them from vanishing.
			float gain = fx["gain"] * Mathf.Pow(Mathf.Clamp01(volume), Effects.Get("ui.live")["volumeCurve"]);
			tap.OneShotGain = gain;
			tap.ToneHz = fx["tone"];
			tap.ReleaseHz = fx["release"];
			return gain;
		}

		// ---- Taps on the sources that play UI sounds

		public static void TapMenu(MenuAudioController controller)
		{
			if (controller == null) return;
			DualSenseMod.EnsureHaptics();
			var source = controller.GetComponent<OWAudioSource>();
			Tap(source);
		}

		/// <summary>Taps the one-shot sources of the ship log and the menu. Safe to call repeatedly.</summary>
		public static void EnsureShipLogTaps()
		{
			try
			{
				TapMenu(Locator.GetMenuAudioController());
				TapAll(typeof(ShipLogController), "_oneShotSource");
				TapAll(typeof(ShipLogMapMode), "_oneShotSource");
				TapAll(typeof(ShipLogDetectiveMode), "_oneShotSource");
				TapAll(typeof(ShipLogSlideProjector), "_oneShotSource");
				TapTextReveal();
			}
			catch (Exception e)
			{
				DualSenseMod.Log("Ship log taps failed: " + e.Message);
			}
		}

		private static void TapAll(Type owner, string field)
		{
			var info = owner.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (info == null) return;
			// Includes inactive objects: the ship log screens are switched off until they are used.
			foreach (UnityEngine.Object found in Resources.FindObjectsOfTypeAll(owner))
			{
				var component = found as Component;
				if (component == null || !component.gameObject.scene.IsValid()) continue;
				Tap(info.GetValue(component) as OWAudioSource);
			}
		}

		/// <summary>
		/// The typing sound of the ship log: a loop the game plays while text is being revealed. The tap listens to the
		/// loop itself, so it is felt exactly while the game plays it.
		/// </summary>
		private static void TapTextReveal()
		{
			var info = typeof(ShipLogEntryDescriptionField).GetField("_audioSource", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (info == null) return;
			foreach (var field in Resources.FindObjectsOfTypeAll<ShipLogEntryDescriptionField>())
			{
				if (!field.gameObject.scene.IsValid()) continue;
				var source = info.GetValue(field) as OWAudioSource;
				if (source == null) continue;

				HapticTap.Attach(source, float.NaN, null, true);
				var tap = source.GetComponent<HapticTap>();
				if (tap == null || tap.GainOverride != null) continue;
				tap.UiProfile = true;
				tap.GainOverride = () =>
				{
					var fx = Effects.Get("ui.textreveal");
					tap.ToneHz = fx["tone"];
					tap.ReleaseHz = fx["release"];
					return fx["gain"];
				};
			}
		}

		/// <summary>F1 menu: holds the typing loop while called every frame.</summary>
		public static void HoldTextReveal()
		{
			EnsureShipLogTaps();
			var info = typeof(ShipLogEntryDescriptionField).GetField("_audioSource", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			foreach (var field in Resources.FindObjectsOfTypeAll<ShipLogEntryDescriptionField>())
			{
				if (!field.gameObject.scene.IsValid()) continue;
				var source = info.GetValue(field) as OWAudioSource;
				if (source != null && !source.isPlaying) source.Play();
				return;
			}
		}

		public static void StopTextReveal()
		{
			var info = typeof(ShipLogEntryDescriptionField).GetField("_audioSource", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			foreach (var field in Resources.FindObjectsOfTypeAll<ShipLogEntryDescriptionField>())
			{
				if (!field.gameObject.scene.IsValid()) continue;
				var source = info.GetValue(field) as OWAudioSource;
				if (source != null && source.isPlaying) source.Stop();
				return;
			}
		}

		private static void Tap(OWAudioSource source)
		{
			if (source == null) return;
			HapticTap.Attach(source, float.NaN, null, true);
			var tap = source.GetComponent<HapticTap>();
			if (tap == null) return;
			tap.UiProfile = true;
			tap.Strict = true;
			tap.OneShotGain = 0f;
		}

		// ---- F1 menu: plays the real sounds on the game's own sources

		public static void PlayMenu(AudioType type)
		{
			var menu = Locator.GetMenuAudioController();
			if (menu == null) return;
			TapMenu(menu);
			menu.GetComponent<OWAudioSource>().PlayOneShot(type);
		}

		public static void PlayShipLog(AudioType type)
		{
			EnsureShipLogTaps();
			var controller = Resources.FindObjectsOfTypeAll<ShipLogController>();
			foreach (var c in controller)
			{
				if (!c.gameObject.scene.IsValid()) continue;
				var field = typeof(ShipLogController).GetField("_oneShotSource", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				var source = field.GetValue(c) as OWAudioSource;
				if (source != null) { source.PlayOneShot(type); return; }
			}
			DualSenseMod.Log("Ship log preview: no ship log in this scene");
		}
	}

	/// <summary>The menus exist before any game scene: put the tap on their source as soon as it is created.</summary>
	[HarmonyPatch(typeof(MenuAudioController), "Awake")]
	internal static class MenuAudioPatch
	{
		private static void Postfix(MenuAudioController __instance)
		{
			try
			{
				UiHaptics.TapMenu(__instance);
			}
			catch (Exception e)
			{
				DualSenseMod.Debug("Menu tap failed: " + e.Message);
			}
		}
	}
}
