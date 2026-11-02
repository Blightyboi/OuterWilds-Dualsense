using HarmonyLib;
using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// The experiment window of the High Energy Lab on Ember Twin (six panels, TLE_ExptWindow_01..06, one multi-part Nomai door
	/// whose source is AudioSource_WindowCover): its panels play the big door's start and stop clips, but the live tap hears nothing
	/// from that source (the logged strength is real, the output peak stays 0), so the window opens and closes without any
	/// haptic. The haptic comes from the event instead: NomaiMultiPartDoor.Cycle starts a slow rumble for the whole travel (the
	/// door's own cycle time) and a blow when it ends, scaled by the distance to the window.
	/// </summary>
	[HarmonyPatch(typeof(NomaiMultiPartDoor), nameof(NomaiMultiPartDoor.Cycle))]
	internal static class WindowHaptics
	{
		private const string SourceName = "AudioSource_WindowCover";

		[HarmonyPostfix]
		private static void After(NomaiMultiPartDoor __instance)
		{
			try
			{
				if (!DualSenseMod.EnsureHaptics() || !__instance.enabled) return;
				var door = Traverse.Create(__instance);
				if (door.Field("_cycleTimer").GetValue<float>() > 0.05f) return; // a cycle that was already running
				var source = door.Field("_audioSource").GetValue<OWAudioSource>();
				if (source == null || source.name != SourceName) return;

				var camera = Locator.GetPlayerCamera();
				if (camera == null) return;
				var toDoor = __instance.transform.position - camera.transform.position;
				float pan = toDoor.sqrMagnitude > 0.01f ? Vector3.Dot(toDoor.normalized, camera.transform.right) : 0f;
				Play(door.Field("_totalCycleTime").GetValue<float>(), Reach(toDoor.magnitude), pan);
			}
			catch (System.Exception e)
			{
				DualSenseMod.Debug("Window haptic failed: " + e.Message);
			}
		}

		private static float Reach(float meters)
		{
			var fx = Effects.Get("window.slide");
			return Mathf.Pow(Mathf.Clamp01(1f - meters / Mathf.Max(fx["range"], 0.1f)), 1.5f);
		}

		/// <summary>Rumble for the whole travel, a blow as the last panel stops.</summary>
		public static void Play(float seconds, float reach, float pan)
		{
			var fx = Effects.Get("window.slide");
			float strength = fx["strength"] * reach;
			if (strength < 0.02f || seconds < 0.2f) return;
			var rumble = HapticSynth.Rumble(fx["freq"], seconds, strength * 0.6f, 0f, fx["chop"]);
			var stop = HapticSynth.Thump(fx["freq"] * 1.2f, 0.35f, strength, true, Mathf.Max(0f, seconds - 0.15f));
			HapticSynth.Play(HapticSynth.Mix(rumble, stop), pan);
		}

		/// <summary>F1 menu: a window travel of 6 seconds right in front of the player.</summary>
		public static void Preview()
		{
			Play(6f, 1f, 0f);
		}
	}
}
