using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// Riding a Giant's Deep tornado. The game has no rumble for it, only the fluid: TornadoFluidVolume.GetPointFluidVelocity
	/// returns the velocity the air has at a point (_verticalSpeed = 300 m/s up, plus a pull toward the axis that is 100 m/s
	/// from 20 m out and falls to 0 on the axis), and the detector's drag pushes the body toward it. Every physics step the
	/// game asks that of the player's detector (or of the ship's, when the player is inside it), so a postfix there sees
	/// exactly when and how hard the tornado pushes:
	///  - strength: how far the body is still from the air's speed (full when thrown in, less once carried, never below
	///    a floor so the turbulence stays), calmer near the axis, where the pull is zero;
	///  - a blow when the detector first enters the volume, and a fading tail and a soft lurch when it leaves;
	///  - the haptic leans toward the side the pull goes to (toward the axis), more the further from the axis;
	///  - the triggers (R2 and L2) go to full resistance while the tornado holds the player.
	/// Nothing here runs outside a tornado: all of it is switched by the game asking for the tornado's velocity.
	/// </summary>
	[HarmonyPatch(typeof(TornadoFluidVolume), "GetPointFluidVelocity")]
	internal static class TornadoHaptics
	{
		private const float AxisRadius = 20f; // the game's: the pull is full from this distance out

		private static readonly FieldInfo VerticalSpeed = typeof(TornadoFluidVolume).GetField("_verticalSpeed", BindingFlags.Instance | BindingFlags.NonPublic);
		private static readonly FieldInfo Pivot = typeof(TornadoFluidVolume).GetField("_tornadoPivot", BindingFlags.Instance | BindingFlags.NonPublic);

		// Written from the physics step (the postfix), read in Tick.
		private static float _level;        // 0-1: how far the body is from the air's speed
		private static float _edge = 1f;    // 0 on the axis, 1 at 20 m and beyond
		private static float _pan;          // -1 left .. 1 right: where the pull goes
		private static float _seen = -10f;  // time of the last call
		private static bool _entered;       // set on the first call after being out
		private static string _who = "";    // what the tornado holds (player or ship) and which one, for the log
		private static bool _logged;
		private static float _loggedAt;

		private static float _previewUntil;
		private static HapticRideSource _source;
		private static HapticAudio _owner;
		private static bool _wasOn;
		private static float _exitTime = -10f;
		private static float _lastTarget;

		/// <summary>True while a tornado pushes the player or the ship the player is in.</summary>
		public static bool Riding
		{
			get { return Time.unscaledTime - _seen < 0.25f && !OWTime.IsPaused(); }
		}

		[HarmonyPostfix]
		private static void After(TornadoFluidVolume __instance, FluidDetector detector, Vector3 __result)
		{
			try
			{
				if (detector == null) return;
				bool player = detector.CompareTag("PlayerDetector");
				bool ship = !player && detector.CompareTag("ShipDetector") && PlayerState.IsInsideShip();
				if (!player && !ship) return;
				var body = detector.GetAttachedOWRigidbody();
				if (body == null) return;

				float full = VerticalSpeed != null ? Mathf.Max((float)VerticalSpeed.GetValue(__instance), 1f) : 300f;
				float level = Mathf.Clamp01((__result - body.GetVelocity()).magnitude / full);

				float edge = 1f, pan = 0f;
				var pivot = Pivot != null ? Pivot.GetValue(__instance) as Transform : null;
				if (pivot != null)
				{
					Vector3 toAxis = Vector3.ProjectOnPlane(pivot.position - detector.transform.position, pivot.up);
					float distance = toAxis.magnitude;
					edge = Mathf.Clamp01(distance / AxisRadius);
					var camera = Locator.GetPlayerCamera();
					if (camera != null && distance > 0.1f)
						pan = Mathf.Clamp(Vector3.Dot(toAxis / distance, camera.transform.right) * edge, -1f, 1f);
				}

				if (Time.unscaledTime - _seen > 0.25f) _entered = true; // first call after being out
				_who = (player ? "player" : "ship") + " in " + __instance.name;
				_level = level;
				_edge = edge;
				_pan = pan;
				_seen = Time.unscaledTime;
			}
			catch (Exception e)
			{
				DualSenseMod.Debug("Tornado haptic failed: " + e.Message);
			}
		}

		/// <summary>F1 menu: feels the ride for about two seconds without a tornado.</summary>
		public static void Preview()
		{
			_level = 0.8f;
			_edge = 0.8f;
			_pan = 0.5f;
			_previewUntil = Time.unscaledTime + 2.1f;
			_entered = true;
		}

		/// <summary>Called every frame from DualSenseMod.Update.</summary>
		public static void Tick()
		{
			var haptics = DualSenseMod.Haptics;
			var mod = DualSenseMod.Instance;
			if (haptics == null || mod == null) return;

			// One line when a tornado takes hold of the player and one when it lets go: shows what is held, for how long,
			// and whether the triggers were set back (the ride ends 0.25 s after the game stops asking).
			bool riding = Riding;
			if (riding && !_logged) { _logged = true; _loggedAt = Time.unscaledTime; DualSenseMod.Log("Tornado holds the " + _who + "; triggers at full resistance."); }
			else if (!riding && _logged) { _logged = false; DualSenseMod.Log("Tornado let go after " + (Time.unscaledTime - _loggedAt).ToString("F1") + " s; triggers back to normal."); }

			bool previewing = Time.unscaledTime < _previewUntil;
			bool on = mod.Enabled && mod.AudioHaptics && (previewing || Riding);
			if (_source == null || _owner != haptics)
			{
				if (!on) return;
				_source = haptics.AddSource(new HapticRideSource());
				_owner = haptics;
			}

			var fx = Effects.Get("tornado.ride");
			float master = fx["gain"];
			float floor = fx["floor"];
			float release = Mathf.Max(fx["release"], 0.05f);
			_source.LowHz = fx["lowHz"];
			_source.HighHz = fx["highHz"];
			_source.WobbleHz = fx["wobbleHz"];

			float target = 0f;
			if (on)
			{
				// Calm on the axis, violent from 20 m out.
				float place = Mathf.Lerp(fx["center"], 1f, _edge);
				target = master * (floor + (1f - floor) * _level) * place;
				_lastTarget = target;
				_exitTime = -10f;

				if (_entered)
				{
					_entered = false;
					_source.KickRequest = Mathf.Clamp01(fx["jolt"] * _level);
				}

				// Lean toward the side the pull goes to; keep the middle level equal to the plain stereo one.
				float pan = Mathf.Clamp(_pan * fx["lean"], -1f, 1f);
				float angle = (pan + 1f) * 0.25f * Mathf.PI;
				_source.GainLeft = Mathf.Cos(angle) * 1.4142f;
				_source.GainRight = Mathf.Sin(angle) * 1.4142f;
			}
			else if (_wasOn)
			{
				// Out of the tornado: a soft lurch, then the rumble fades over `release` seconds, not at once.
				_exitTime = Time.unscaledTime;
				_source.KickRequest = Mathf.Clamp01(fx["lurch"] * _lastTarget);
			}
			if (!on && Time.unscaledTime - _exitTime < release)
				target = _lastTarget * (1f - (Time.unscaledTime - _exitTime) / release);

			_source.Target = target;
			_wasOn = on;
		}

		/// <summary>
		/// Both triggers at full resistance for as long as a tornado pushes the player or the ship the player is in, whatever
		/// else they were doing. Applied last, over the game's own effects. Does nothing outside a tornado.
		/// </summary>
		public static void ApplyTriggers(ref TriggerEffect left, ref TriggerEffect right)
		{
			if (!Riding || PlayerState.IsDead()) return;
			var fx = Effects.Get("tornado.triggers");
			var effect = TriggerEffect.Resistance(fx.Int("zone"), fx.Int("strength"));
			left = effect;
			right = effect;
		}
	}
}
