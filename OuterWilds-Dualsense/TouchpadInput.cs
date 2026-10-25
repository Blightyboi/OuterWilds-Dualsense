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
		private static bool _clickWas, _down1Was, _down2Was;
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

		private static float _rebuildAt;
		private static bool _ps5Applied;

		/// <summary>The mod is on and a DualSense is open.</summary>
		public static bool Available
		{
			get
			{
				var d = DualSenseMod.Device; var m = DualSenseMod.Instance;
				return d != null && m != null && m.Enabled && d.IsOpen;
			}
		}

		/// <summary>The button set is read when the game starts, usually before our device is open: reload it once.</summary>
		private static void ApplyPs5Set()
		{
			if (_ps5Applied || !Available) return;
			var lib = ButtonPromptLibrary.SharedInstance;
			if (lib == null) return;
			_ps5Applied = true;
			try
			{
				lib.SetConfigTextures(PlayerData.GetButtonPromptImageSetting());
				var manager = Locator.GetPromptManager();
				if (manager != null) manager.RebuildUI();
			}
			catch (System.Exception e) { DualSenseMod.Debug("PS5 button set failed: " + e.Message); }
		}

		/// <summary>
		/// The game takes the last device that actuated an input action as the current one; the touchpad is not
		/// bound to any action, so a touch is announced to it here (same steps as InputManager.UpdateLastInputCommandType).
		/// </summary>
		private static void SwitchToGamepad()
		{
			var manager = OWInput.SharedInputManager as InputManager;
			if (manager == null || OWInput.UsingGamepad()) return;
			manager.LastCommandInputType = InputConsts.InputType.GamePad;
			try
			{
				var handler = Traverse.Create(manager).Field("OnUpdateInputDevice").GetValue<System.Delegate>();
				if (handler != null) handler.DynamicInvoke();
			}
			catch (System.Exception e) { DualSenseMod.Debug("Touchpad device switch failed: " + e.Message); }
			DeviceSwitched();
		}

		/// <summary>Called when the game switches between controller and keyboard prompts.</summary>
		public static void DeviceSwitched() { _rebuildAt = Time.unscaledTime + 0.8f; }

		/// <summary>
		/// The game rebuilds prompt icons one element per 0.1 s after a switch, and the ship log lists can be left
		/// with a mix of keyboard and controller icons. Ask for a second full rebuild once the first has settled.
		/// </summary>
		private static void RebuildPrompts()
		{
			_rebuildAt = 0f;
			try
			{
				var manager = Locator.GetPromptManager();
				if (manager != null) manager.RebuildUI();
				var log = Object.FindObjectOfType<ShipLogController>();
				if (log == null) return;
				foreach (var field in new[] { "_upperRightPromptListMap", "_upperRightPromptListDetective", "_centerPromptList" })
				{
					var list = Traverse.Create(log).Field(field).GetValue<ScreenPromptList>();
					if (list != null) list.RebuildAllScreenPromptUIElements();
				}
			}
			catch (System.Exception) { }
		}

		/// <summary>Called once per frame. Cheap when the map and the log are closed.</summary>
		public static void Tick()
		{
			if (_rebuildAt > 0f && Time.unscaledTime >= _rebuildAt) RebuildPrompts();
			ApplyPs5Set();
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

			bool fresh = (t.Down1 && !_down1Was) || (t.Down2 && !_down2Was) || (t.Click && !_clickWas);
			_down1Was = t.Down1; _down2Was = t.Down2;
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
			if (fresh || Pan.sqrMagnitude + Rotate.sqrMagnitude + Zoom * Zoom + Scroll * Scroll > 0.01f) SwitchToGamepad();
			// Only for a player on the controller: on keyboard and mouse the game shows key prompts and ours would mix in.
			TouchpadPrompts.Show(cfg.Int("prompts") != 0 && OWInput.UsingGamepad() ? WantedPrompt() : -1);
		}

		private static int WantedPrompt()
		{
			if (InMap) return TouchpadPrompts.Map;
			if (Rumor == null) return -1;
			return TextOpen ? -1 : TouchpadPrompts.Open; // with the text open the game's own scroll prompt carries the touchpad
		}

		private static void Reset()
		{
			TouchpadPrompts.Show(-1);
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
		private static void Postfix() { TouchpadInput.Rumor = null; TouchpadPrompts.RemoveLog(); }
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

namespace OuterWildsDualSense
{
	/// <summary>One short on-screen prompt with the game's own PS5 touchpad icon.</summary>
	internal static class TouchpadPrompts
	{
		private static Sprite _icon;

		/// <summary>The game's own "scroll text" prompt of the ship log (right stick); the touchpad is added to it.</summary>
		public static readonly System.Collections.Generic.HashSet<ScreenPrompt> ScrollPrompts = new System.Collections.Generic.HashSet<ScreenPrompt>(); // one per ship log mode

		public static Sprite Icon()
		{
			if (_icon == null)
			{
				var tex = Resources.Load("ButtonPrompts/PS5/PS5_Touch_Pad") as Texture2D;
				if (tex != null) _icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
			}
			return _icon;
		}

		public const int Map = 0, Open = 1;

		// Texts per prompt, in the order of TextTranslation.Language: English, Spanish (LA), German, French, Italian,
		// Polish, Portuguese (BR), Japanese, Russian, Chinese (simplified), Korean, Turkish.
		private static readonly string[][] Texts =
		{
			new[] { "Pan, rotate, zoom", "Mover, girar, zoom", "Schwenken, drehen, zoomen", "Déplacer, tourner, zoom", "Sposta, ruota, zoom", "Przesuń, obróć, zoom", "Mover, girar, zoom", "移動・回転・ズーム", "Двигать, вращать, зум", "移动、旋转、缩放", "이동, 회전, 확대/축소", "Kaydır, döndür, yakınlaştır" },
			new[] { "Open entry", "Abrir entrada", "Eintrag öffnen", "Ouvrir l'entrée", "Apri voce", "Otwórz wpis", "Abrir entrada", "記録を開く", "Открыть запись", "打开条目", "항목 열기", "Girişi aç" },
		};

		private static string Text(int id)
		{
			int lang = 0;
			try { var t = TextTranslation.Get(); if (t != null) lang = (int)t.GetLanguage(); } catch (System.Exception) { }
			var row = Texts[id];
			return lang >= 0 && lang < row.Length ? row[lang] : row[0];
		}

		private sealed class Slot
		{
			public ScreenPrompt Prompt;
			public object Owner; // the prompt manager or list the prompt was added to
			public ScreenPromptElement Element;
		}

		private static readonly Slot MapSlot = new Slot(), LogSlot = new Slot();

		public static void Show(int id)
		{
			// The ship log has its own prompt list (upper right of the log screen); the map uses the screen's.
			ShowIn(MapSlot, id == Map ? id : -1, null);
			ShowIn(LogSlot, id == Open ? id : -1, TouchpadInput.Rumor);
		}

		/// <summary>Removes the ship log prompt, as the game does with its own when a log mode is left.</summary>
		public static void RemoveLog()
		{
			if (LogSlot.Prompt == null) return;
			try { var m = Locator.GetPromptManager(); if (m != null) m.RemoveScreenPrompt(LogSlot.Prompt); } catch (System.Exception) { }
			LogSlot.Prompt = null; LogSlot.Element = null; LogSlot.Owner = null;
		}

		private static void ShowIn(Slot slot, int id, ShipLogDetectiveMode rumor)
		{
			if (id < 0)
			{
				if (slot.Prompt != null) slot.Prompt.SetVisibility(false);
				return;
			}
			var manager = Locator.GetPromptManager();
			if (manager == null) return;
			ScreenPromptList list = null;
			if (rumor != null)
			{
				list = Traverse.Create(rumor).Field("_upperRightPromptList").GetValue<ScreenPromptList>();
				if (list == null) return;
			}
			object owner = list != null ? (object)list : manager;
			string text = Text(id);
			if (slot.Prompt == null || slot.Owner != owner)
			{
				if (Icon() == null) return;
				slot.Prompt = new ScreenPrompt(text, Icon());
				slot.Owner = owner;
				if (list != null) slot.Element = manager.AddScreenPrompt(slot.Prompt, list, TextAnchor.MiddleRight, -1, true, false);
				else manager.AddScreenPrompt(slot.Prompt, PromptPosition.UpperRight, true);
			}
			if (slot.Prompt.GetText() != text) slot.Prompt.SetText(text);
			slot.Prompt.SetVisibility(true);
		}
	}
}

namespace OuterWildsDualSense
{
	/// <summary>Tells the touchpad prompts when the game switches between keyboard and controller icons.</summary>
	[HarmonyPatch(typeof(InputManager), nameof(InputManager.UpdateLastInputCommandType))]
	internal static class InputDeviceSwitchWatch
	{
		private static InputConsts.InputType _last;

		private static void Postfix(InputManager __instance, UnityEngine.InputSystem.InputAction.CallbackContext context)
		{
			if (__instance.LastCommandInputType == _last) return;
			_last = __instance.LastCommandInputType;
			TouchpadInput.DeviceSwitched();
		}
	}
}

namespace OuterWildsDualSense
{
	/// <summary>Adds the touchpad to the ship log's own "scroll text" prompt: right stick / touchpad.</summary>
	[HarmonyPatch(typeof(ShipLogEntryDescriptionField), "Start")]
	internal static class ScrollPromptCapture
	{
		private static void Postfix(ShipLogEntryDescriptionField __instance)
		{
			var prompt = Traverse.Create(__instance).Field("_scrollPromptGamepad").GetValue<ScreenPrompt>();
			if (prompt == null) return;
			TouchpadPrompts.ScrollPrompts.Add(prompt);
			// The game builds the prompt's element inside Start, before we know the prompt: build it again with the touchpad.
			var manager = Locator.GetPromptManager();
			if (manager != null) manager.TriggerRebuild(prompt);
		}
	}

	[HarmonyPatch(typeof(ScreenPrompt), nameof(ScreenPrompt.GetSpriteList))]
	internal static class ScrollPromptSprites
	{
		private static void Postfix(ScreenPrompt __instance, ref System.Collections.Generic.List<Sprite> __result)
		{
			if (!TouchpadPrompts.ScrollPrompts.Contains(__instance) || !TouchpadInput.Available) return;
			var icon = TouchpadPrompts.Icon();
			if (icon == null || __result == null) return;
			if (__result.Count == 1) __result.Add(icon);
			else if (__result.Count != 2) DualSenseMod.Debug("Scroll prompt has " + __result.Count + " images, touchpad not added.");
		}
	}

	/// <summary>Puts a slash between the two images of that prompt.</summary>
	[HarmonyPatch(typeof(ScreenPromptElement), "BuildInCommandImage")]
	internal static class ScrollPromptSlash
	{
		private static void Postfix(ScreenPromptElement __instance, ref System.Collections.Generic.List<string> stringList)
		{
			var data = Traverse.Create(__instance).Field("_promptData").GetValue<ScreenPrompt>();
			if (data == null || !TouchpadPrompts.ScrollPrompts.Contains(data)) return;
			for (int i = 0; i + 1 < stringList.Count; i++)
			{
				if (stringList[i] == "<CMD>" && stringList[i + 1] == "<CMD>") { stringList.Insert(i + 1, "/"); break; }
			}
		}
	}

	/// <summary>With a DualSense in use and the mod on, the game shows the PS5 button set instead of the PS4 one.</summary>
	[HarmonyPatch(typeof(PlayerData), nameof(PlayerData.GetButtonPromptImageSetting))]
	internal static class Ps5ButtonSetPatch
	{
		private static void Postfix(ref ButtonPromptImgSet __result)
		{
			if (__result == ButtonPromptImgSet.DUALSHOCK_4 && TouchpadInput.Available) __result = ButtonPromptImgSet.DUALSENSEPS5;
		}
	}
}
