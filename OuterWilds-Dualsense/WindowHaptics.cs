using HarmonyLib;
using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// The experiment window of the High Energy Lab on Ember Twin: a NomaiGateway (six NomaiGatewaySlab, TLE_ExptWindow_01..06,
	/// its source is AudioSource_WindowCover), opened from both ends one slab pair every _slabInterval seconds. The gateway plays
	/// the big door's start clip and its stop clip for every slab, but the sliding loop in between is PlayDelayed on a source that
	/// has no clip, so the whole travel is silent and the live tap has nothing to follow. The haptic comes from the slab events
	/// instead: every slab that starts to move gets a rumble for exactly as long as it will move (worked out from its offset,
	/// speed and acceleration), scaled by the distance to the window. The stop blow is the game's own clip, already felt.
	/// The gateways of the city have a loop (NomaiDoorSlideBig_LP) and are left alone.
	/// </summary>
	[HarmonyPatch(typeof(NomaiGatewaySlab), nameof(NomaiGatewaySlab.SetOpen))]
	internal static class WindowHaptics
	{
		private const string SourceName = "AudioSource_WindowCover";

		[HarmonyPrefix]
		private static void Before(NomaiGatewaySlab __instance, out bool __state)
		{
			__state = __instance.enabled;
		}

		[HarmonyPostfix]
		private static void After(NomaiGatewaySlab __instance, bool __state)
		{
			try
			{
				if (__state || !__instance.enabled) return; // not a new start of this slab
				if (!DualSenseMod.EnsureHaptics()) return;
				var gateway = __instance.GetComponentInParent<NomaiGateway>();
				if (gateway == null) return;
				var source = Traverse.Create(gateway).Field("_audioSource").GetValue<OWAudioSource>();
				if (source == null || source.name != SourceName) return;

				var slab = Traverse.Create(__instance);
				float seconds = TravelSeconds(slab.Field("_openOffset").GetValue<float>(), slab.Field("_speed").GetValue<float>(), slab.Field("_acceleration").GetValue<float>());
				Start(seconds, __instance.transform, source.GetAudioSource());
			}
			catch (System.Exception e)
			{
				DualSenseMod.Debug("Window haptic failed: " + e.Message);
			}
		}

		/// <summary>How long NomaiGatewaySlab.FixedUpdate takes to move the slab its whole way: constant acceleration up to the speed.</summary>
		private static float TravelSeconds(float distance, float speed, float acceleration)
		{
			distance = Mathf.Abs(distance);
			if (speed <= 0f) return 0f;
			if (acceleration <= 0f) return Mathf.Min(distance / speed, 12f);
			float accelDistance = speed * speed / (2f * acceleration);
			float seconds = distance <= accelDistance
				? Mathf.Sqrt(2f * distance / acceleration)
				: speed / acceleration + (distance - accelDistance) / speed;
			return Mathf.Min(seconds, 12f);
		}

		private sealed class Active
		{
			public HapticOneShotSource Shot;
			public Transform At;
			public AudioSource Source;
			public float End;
		}

		private static readonly System.Collections.Generic.List<Active> Running = new System.Collections.Generic.List<Active>();

		/// <summary>One slab's rumble (a fixed waveform); Tick sets its strength and pan every frame from where the player is.</summary>
		private static void Start(float seconds, Transform at, AudioSource source, float delay = 0f)
		{
			var haptics = DualSenseMod.Haptics;
			var mod = DualSenseMod.Instance;
			if (haptics == null || mod == null || !mod.Enabled || !mod.AudioHaptics || seconds < 0.2f) return;
			var fx = Effects.Get("window.slide");
			var shot = new HapticOneShotSource(HapticSynth.Rumble(fx["freq"], seconds, 1f, 0.35f, fx["chop"], delay), HapticSynth.Rate, 1f);
			shot.GainLeft = 0f;
			shot.GainRight = 0f;
			haptics.AddSource(shot);
			Running.Add(new Active { Shot = shot, At = at, Source = source, End = Time.unscaledTime + delay + seconds + 0.5f });
		}

		/// <summary>Every frame: the rumbles follow the player's distance (the source's own rolloff, like the game's sounds) and side.</summary>
		public static void Tick()
		{
			if (Running.Count == 0) return;
			var camera = Locator.GetPlayerCamera();
			float strength = Effects.Get("window.slide")["strength"];
			for (int i = Running.Count - 1; i >= 0; i--)
			{
				var run = Running[i];
				if (run.Shot.Finished || Time.unscaledTime > run.End) { Running.RemoveAt(i); continue; }
				float reach = 1f, pan = 0f;
				if (camera != null && run.At != null)
				{
					var to = run.At.position - camera.transform.position;
					if (run.Source != null) reach = WorldSounds.GameRolloffAt(run.Source, to.magnitude);
					if (to.sqrMagnitude > 0.01f) pan = Vector3.Dot(to.normalized, camera.transform.right);
				}
				float angle = (Mathf.Clamp(pan, -1f, 1f) + 1f) * 0.25f * Mathf.PI;
				float level = strength * reach;
				run.Shot.GainLeft = Mathf.Cos(angle) * level;
				run.Shot.GainRight = Mathf.Sin(angle) * level;
			}
		}

		/// <summary>F1 menu: three pairs of slabs as the gateway opens (one every two seconds), right in front of the player.</summary>
		public static void Preview()
		{
			for (int i = 0; i < 3; i++)
			{
				Start(1.8f, null, null, i * 2f);
				Start(1.8f, null, null, i * 2f);
			}
		}
	}
}
