using HarmonyLib;
using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// Gyro roll: tilting the controller sideways rolls the ship (and the jetpack in zero gravity) like the stick does,
	/// and keeps rolling for as long as it is tilted. The tilt comes from gravity in the accelerometer, so it does not drift.
	/// Hold L3 and press R3 to make the current tilt the level one.
	/// </summary>
	internal static class GyroInput
	{
		/// <summary>Roll input, -1..1, positive = clockwise (right side down).</summary>
		public static float Roll;
		/// <summary>Pitch (tilt forward/back) and yaw (turn left/right), -1..1; they hold like the roll.</summary>
		public static float Pitch, Yaw;
		/// <summary>L3 is held: the flashlight (R3) must not react, the pair is the recalibration.</summary>
		public static bool L3Held;

		private static bool _wasOn, _haveZero, _r3Was, _wasControl;
		private static float _zero, _angle, _zeroPitch, _pitchAngle, _yawAngle;
		private static Vector2 _filtered, _filteredPitch;
		private static float _logAt;

		public static bool On
		{
			get { var m = DualSenseMod.Instance; return m != null && m.Gyro && TouchpadInput.Available; }
		}

		/// <summary>Where the gyro would roll right now: the cockpit, or the jetpack without gravity.</summary>
		public static bool InControl()
		{
			if (!On) return false;
			if (OWInput.IsInputMode(InputMode.ShipCockpit)) return true;
			return OWInput.IsInputMode(InputMode.Character) && PlayerState.InZeroG();
		}

		/// <summary>Adds the gyro to a rotation input: x pitch, y yaw, z roll (as the game's own stick code does).</summary>
		public static void Apply(ref Vector3 input)
		{
			input.x = Mathf.Clamp(input.x - Pitch, -1f, 1f);
			input.y = Mathf.Clamp(input.y + Yaw, -1f, 1f);
			input.z = Mathf.Clamp(input.z - Roll, -1f, 1f);
		}

		private static float Stick(float angle, float dead, float max)
		{
			return Mathf.Clamp01((Mathf.Abs(angle) - dead) / (max - dead)) * Mathf.Sign(angle);
		}

		public static void Tick()
		{
			Roll = 0f; Pitch = 0f; Yaw = 0f;
			L3Held = false;
			if (!On) { _wasOn = false; _wasControl = false; GyroPrompt.Show(false); return; }
			var device = DualSenseMod.Device;
			TouchState t;
			if (!device.ReadTouch(out t)) { GyroPrompt.Show(false); return; }

			var cfg = Effects.Get("gyro.roll");
			float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
			// Gravity direction in the controller's plane; filtered so that haptics and thrust shaking do not move it.
			var raw = new Vector2(t.AccelX, t.AccelY);
			float a = 1f - Mathf.Exp(-dt * Mathf.Max(cfg["smoothing"], 1f));
			_filtered = _wasOn ? Vector2.Lerp(_filtered, raw, a) : raw;
			_angle = Mathf.Atan2(_filtered.x, _filtered.y) * Mathf.Rad2Deg;
			_filteredPitch = _wasOn ? Vector2.Lerp(_filteredPitch, new Vector2(t.AccelZ, t.AccelY), a) : new Vector2(t.AccelZ, t.AccelY);
			_pitchAngle = Mathf.Atan2(_filteredPitch.x, _filteredPitch.y) * Mathf.Rad2Deg;
			// Turning left/right has no reference in gravity: the turn rate is added up into a held angle (recentred with L3+R3).
			// Raw gyro is about 16 per degree per second (full scale +-2000 dps).
			float rate = t.GyroY / 16f;
			if (Mathf.Abs(rate) > 1.5f) _yawAngle = Mathf.Clamp(_yawAngle + rate * dt, -90f, 90f);

			// The zero is the tilt at the moment the gyro was switched on.
			if (!_wasOn || !_haveZero) { _zero = _angle; _zeroPitch = _pitchAngle; _yawAngle = 0f; _haveZero = true; }
			_wasOn = true;
			L3Held = t.L3;
			if (t.L3 && t.R3 && !_r3Was) { _zero = _angle; _zeroPitch = _pitchAngle; _yawAngle = 0f; }
			_r3Was = t.R3;

			bool control = InControl();
			// Taking the controls (sitting down at the helm, leaving gravity with the jetpack) makes the pose held right now the level one.
			if (control && !_wasControl) { _zero = _angle; _zeroPitch = _pitchAngle; _yawAngle = 0f; }
			_wasControl = control;
			GyroPrompt.Show(control && OWInput.UsingGamepad());
			if (DualSenseMod.Instance.DebugLog && Time.unscaledTime > _logAt)
			{
				_logAt = Time.unscaledTime + 2f;
				DualSenseMod.Debug("Gyro: gyro " + t.GyroX + " " + t.GyroY + " " + t.GyroZ + ", pitch " + _pitchAngle.ToString("0.0") + ", yaw " + _yawAngle.ToString("0.0") + ", accel " + t.AccelX + " " + t.AccelY + " " + t.AccelZ + ", angle " + _angle.ToString("0.0") + ", zero " + _zero.ToString("0.0"));
			}
			if (!control) return;

			float dead = cfg["deadzone"], max = Mathf.Max(cfg["maxAngle"], dead + 1f);
			// Signs as found in the game: with the default strength 1 a tilt to the right rolls right, nose down pitches down.
			Roll = Mathf.Clamp(-Stick(Mathf.DeltaAngle(_zero, _angle), dead, max) * cfg["roll"], -1f, 1f);
			Pitch = Mathf.Clamp(-Stick(Mathf.DeltaAngle(_zeroPitch, _pitchAngle), dead, max) * cfg["pitch"], -1f, 1f);
			Yaw = Mathf.Clamp(-Stick(_yawAngle, dead, max) * cfg["yaw"], -1f, 1f);
		}
	}

	/// <summary>Adds the gyro roll to the ship and the jetpack rotation input (z is roll).</summary>
	[HarmonyPatch(typeof(ShipThrusterController), "ReadRotationalInput")]
	internal static class GyroShipPatch
	{
		private static void Postfix(ShipThrusterController __instance, ref Vector3 __result)
		{
			if (GyroInput.Roll == 0f && GyroInput.Pitch == 0f && GyroInput.Yaw == 0f || !OWInput.IsInputMode(InputMode.ShipCockpit | InputMode.LandingCam)) return;
			if (OWInput.IsInputMode(InputMode.ShipCockpit) && OWInput.IsPressed(InputLibrary.freeLook)) return;
			var t = Traverse.Create(__instance);
			var resources = t.Field("_shipResources").GetValue<ShipResources>();
			var landing = t.Field("_landingManager").GetValue<LandingPadManager>();
			if (resources != null && !resources.AreThrustersUsable()) return;
			if (landing != null && landing.IsLanded()) return;
			GyroInput.Apply(ref __result);
		}
	}

	[HarmonyPatch(typeof(JetpackThrusterController), "ReadRotationalInput")]
	internal static class GyroJetpackPatch
	{
		private static void Postfix(ref Vector3 __result)
		{
			if (!OWInput.IsInputMode(InputMode.Character) || !PlayerState.InZeroG()) return;
			GyroInput.Apply(ref __result);
		}
	}

	/// <summary>While L3 is held the flashlight / ship lights (R3) are left alone: L3+R3 is the gyro recalibration.</summary>
	[HarmonyPatch(typeof(AbstractCommands), nameof(AbstractCommands.IsNewlyPressed))]
	internal static class GyroFlashlightPatch
	{
		private static void Postfix(AbstractCommands __instance, ref bool __result)
		{
			if (__result && GyroInput.L3Held && __instance.CommandType == InputConsts.InputCommandType.FLASHLIGHT) __result = false;
		}
	}

	/// <summary>"L3 + R3  Recalibrate gyro" in the upper right while the gyro is steering.</summary>
	internal static class GyroPrompt
	{
		public static ScreenPrompt Prompt;
		private static object _owner;
		private static Sprite _l3, _r3;

		private static Sprite Load(string name)
		{
			var tex = Resources.Load("ButtonPrompts/PS5/" + name) as Texture2D;
			return tex == null ? null : Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
		}

		public static Sprite SecondIcon()
		{
			if (_r3 == null) _r3 = Load("PS5_Right_Stick_Click");
			return _r3;
		}

		public static void Show(bool visible)
		{
			if (!visible) { if (Prompt != null) Prompt.SetVisibility(false); return; }
			var manager = Locator.GetPromptManager();
			if (manager == null) return;
			string text = TouchpadPrompts.Text(TouchpadPrompts.Recalibrate);
			if (Prompt == null || _owner != manager)
			{
				if (_l3 == null) _l3 = Load("PS5_Left_Stick_Click");
				if (_l3 == null || SecondIcon() == null) return;
				Prompt = new ScreenPrompt(text, _l3);
				_owner = manager;
				manager.AddScreenPrompt(Prompt, PromptPosition.UpperRight, true);
			}
			if (Prompt.GetText() != text) Prompt.SetText(text);
			Prompt.SetVisibility(true);
		}
	}
}
