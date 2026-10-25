using HarmonyLib;
using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// Touchpad gestures as extra input for the map, the ship log map and the ship log text:
	/// one finger pans (and scrolls text), two fingers moving together rotate, a pinch zooms.
	/// Values are stick-like (-1..1) and are added to the game's own input by the patches below.
	/// </summary>
	internal static class TouchpadInput
	{
		/// <summary>True while a gesture is feeding the game (only in the map and the ship log).</summary>
		public static bool Live;
		public static bool InMap, InLog;
		public static Vector2 Pan, Rotate;
		public static float Zoom, Scroll;

		/// <summary>Rumor mode of the ship log is open (the entry graph, with the text pane on top of it).</summary>
		public static ShipLogDetectiveMode Rumor;
		private static bool _clickWas;
		private static float _clickUntil;
		public static bool InRumorUpdate;

		public static bool TextOpen
		{
			get { return Rumor != null && Traverse.Create(Rumor).Field("_descriptionField").GetValue<ShipLogEntryDescriptionField>().IsVisible(); }
		}

		/// <summary>True once per touchpad press while it is wanted (rumor mode, text closed); used for the interact command.</summary>
		public static bool TakeClick()
		{
			if (!InRumorUpdate || Time.unscaledTime > _clickUntil || Rumor == null) return false;
			if (TextOpen) { _clickUntil = 0f; return false; }
			_clickUntil = 0f;
			return true;
		}

		private static int _mask; // which fingers were down last frame (bit 0 = first, bit 1 = second)
		private static Vector2 _p1, _p2;
		private static Vector2 _pan, _rotate;
		private static float _zoom, _scroll;

		/// <summary>Called once per frame. Cheap when the map and the log are closed.</summary>
		public static void Tick()
		{
			Live = false;
			var device = DualSenseMod.Device;
			var mod = DualSenseMod.Instance;
			if (device == null || mod == null || !mod.Enabled || !device.IsOpen || OWInput.SharedInputManager == null)
			{
				Reset();
				return;
			}
			var cfg = Effects.Get("touch.pad");
			InMap = OWInput.IsInputMode(InputMode.Map);
			InLog = OWInput.IsInputMode(InputMode.ShipComputer);
			TouchState t;
			if (cfg.Int("enabled") == 0 || !(InMap || InLog) || !device.ReadTouch(out t))
			{
				Reset();
				return;
			}

			if (t.Click && !_clickWas) { _clickUntil = Time.unscaledTime + 0.25f; }
			_clickWas = t.Click;
			int mask = (t.Down1 ? 1 : 0) | (t.Down2 ? 2 : 0);
			var p1 = new Vector2(t.X1, t.Y1);
			var p2 = new Vector2(t.X2, t.Y2);
			Vector2 pan = Vector2.zero, rotate = Vector2.zero;
			float zoom = 0f, scroll = 0f;
			float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
			// Pixels per second of finger speed that count as a fully pushed stick.
			float k = 1f / (dt * Mathf.Max(cfg["speed"], 1f));

			if (mask == _mask && mask != 0)
			{
				if (mask == 3)
				{
					// Two fingers: moving together rotates, moving apart or together zooms.
					Vector2 move = ((p1 - _p1) + (p2 - _p2)) * 0.5f;
					rotate = new Vector2(move.x, -move.y) * k * cfg["rotate"];
					zoom = (Vector2.Distance(p1, p2) - Vector2.Distance(_p1, _p2)) * k * cfg["zoom"];
				}
				else
				{
					// One finger: dragging the map, so the camera goes the opposite way along X.
					Vector2 d = (mask == 1 ? p1 - _p1 : p2 - _p2) * k;
					pan = new Vector2(-d.x, d.y) * cfg["pan"];
					scroll = d.y * cfg["scroll"];
				}
			}
			_mask = mask; _p1 = p1; _p2 = p2;

			// A little smoothing: the reports are steadier than the frame times.
			float a = 1f - Mathf.Exp(-dt * 30f);
			_pan = Vector2.Lerp(_pan, pan, a);
			_rotate = Vector2.Lerp(_rotate, rotate, a);
			_zoom = Mathf.Lerp(_zoom, zoom, a);
			_scroll = Mathf.Lerp(_scroll, scroll, a);
			Pan = Clamp(_pan); Rotate = Clamp(_rotate);
			Zoom = Mathf.Clamp(_zoom, -1f, 1f); Scroll = Mathf.Clamp(_scroll, -1f, 1f);
			Live = true;
		}

		private static void Reset()
		{
			_mask = 0;
			_pan = _rotate = Pan = Rotate = Vector2.zero;
			_zoom = _scroll = Zoom = Scroll = 0f;
		}

		private static Vector2 Clamp(Vector2 v)
		{
			return new Vector2(Mathf.Clamp(v.x, -1f, 1f), Mathf.Clamp(v.y, -1f, 1f));
		}
	}

	/// <summary>
	/// Adds the touchpad to the game's input commands. Both methods are called directly by the map code
	/// (InputLibrary.look.GetAxisValue) and through OWInput, so patching the command covers every route.
	/// </summary>
	[HarmonyPatch(typeof(AbstractCommands), nameof(AbstractCommands.GetAxisValue))]
	internal static class TouchpadAxisPatch
	{
		private static void Postfix(AbstractCommands __instance, ref Vector2 __result)
		{
			if (!TouchpadInput.Live) return;
			switch (__instance.CommandType)
			{
				case InputConsts.InputCommandType.MOVE_XZ:
					if (!TouchpadInput.TextOpen) __result = Add(__result, TouchpadInput.Pan);
					break;
				case InputConsts.InputCommandType.LOOK:
					if (TouchpadInput.InMap) __result = Add(__result, TouchpadInput.Rotate);
					break;
			}
		}

		private static Vector2 Add(Vector2 a, Vector2 b)
		{
			return new Vector2(Mathf.Clamp(a.x + b.x, -1f, 1f), Mathf.Clamp(a.y + b.y, -1f, 1f));
		}
	}

	[HarmonyPatch(typeof(AbstractCommands), nameof(AbstractCommands.GetValue))]
	internal static class TouchpadValuePatch
	{
		private static void Postfix(AbstractCommands __instance, ref float __result)
		{
			if (!TouchpadInput.Live) return;
			switch (__instance.CommandType)
			{
				case InputConsts.InputCommandType.THRUST_UP: // map zoom in
					if (TouchpadInput.Zoom > 0f) __result = Mathf.Clamp01(__result + TouchpadInput.Zoom);
					break;
				case InputConsts.InputCommandType.THRUST_DOWN: // map zoom out
					if (TouchpadInput.Zoom < 0f) __result = Mathf.Clamp01(__result - TouchpadInput.Zoom);
					break;
				case InputConsts.InputCommandType.LOOK_Y: // ship log text scroll
					if (TouchpadInput.InLog) __result = Mathf.Clamp(__result + TouchpadInput.Scroll, -1f, 1f);
					break;
			}
		}
	}
}

namespace OuterWildsDualSense
{
	/// <summary>Rumor mode tracking and the touchpad click that opens the entry text there.</summary>
	[HarmonyPatch(typeof(ShipLogDetectiveMode), nameof(ShipLogDetectiveMode.EnterMode))]
	internal static class RumorEnterPatch
	{
		private static void Postfix(ShipLogDetectiveMode __instance) { TouchpadInput.Rumor = __instance; }
	}

	/// <summary>Only the rumor mode's own update may take the click, other code also asks for the interact button.</summary>
	[HarmonyPatch(typeof(ShipLogDetectiveMode), nameof(ShipLogDetectiveMode.UpdateMode))]
	internal static class RumorUpdatePatch
	{
		private static void Prefix() { TouchpadInput.InRumorUpdate = true; }
		private static void Finalizer() { TouchpadInput.InRumorUpdate = false; }
	}

	[HarmonyPatch(typeof(ShipLogDetectiveMode), nameof(ShipLogDetectiveMode.ExitMode))]
	internal static class RumorExitPatch
	{
		private static void Postfix() { TouchpadInput.Rumor = null; }
	}

	[HarmonyPatch(typeof(AbstractCommands), nameof(AbstractCommands.IsNewlyPressed))]
	internal static class TouchpadClickPatch
	{
		private static void Postfix(AbstractCommands __instance, ref bool __result)
		{
			if (!__result && __instance.CommandType == InputConsts.InputCommandType.INTERACT && TouchpadInput.TakeClick())
				__result = true;
		}
	}
}
