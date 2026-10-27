using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OuterWildsDualSense
{
	/// <summary>
	/// F1 menu for tuning every haptic and trigger effect while playing: collapsible groups, sliders with number
	/// fields, a Play button per effect, per-effect and global reset, and saving to effects.json.
	/// </summary>
	public class DebugMenu : MonoBehaviour
	{
		private bool _open;

		/// <summary>True while the F1 menu is open (its Play buttons must be felt even if the game is paused).</summary>
		public static bool IsOpen { get; private set; }

		/// <summary>A Play button of the menu is on: only then does the open menu let the haptics through on a paused game.</summary>
		public static bool Previewing { get; private set; }
		private bool _blockInput = true;
		private bool _inputBlocked;
		private Rect _window = new Rect(20, 20, 560, 720);
		private Vector2 _scroll;
		private string _status = "";
		private float _statusUntil;

		// The effect whose Play button is on: it repeats until Stop is pressed.
		private EffectDef _playing;
		private float _nextPlay;

		// Colour picker state
		private readonly Dictionary<string, Vector3> _hsv = new Dictionary<string, Vector3>();
		private readonly Dictionary<string, string> _hexText = new Dictionary<string, string>();
		private Texture2D _hueTexture;
		private Texture2D _svTexture;
		private float _svTextureHue = -1f;
		private string _dragSv, _dragHue;
		private string _pickTarget;                 // effect waiting for a colour from the screen
		private Color _hoverColor = Color.black;
		private Coroutine _hoverLoop;

		private readonly HashSet<string> _openGroups = new HashSet<string>();
		private readonly HashSet<string> _openEffects = new HashSet<string>();
		private readonly Dictionary<Param, string> _editText = new Dictionary<Param, string>();

		private void Update()
		{
			// The menu is a tuning tool: it exists only while the "debugMenu" mod setting is on.
			var mod = DualSenseMod.Instance;
			if (mod == null || !mod.DebugMenuEnabled)
			{
				if (_open) SetOpen(false);
				return;
			}
			if (F1Pressed()) SetOpen(!_open);
			if (_open && _pickTarget != null) UpdatePicking();
			TickPreview();
		}

		private void TickPreview()
		{
			if (_playing == null) return;
			if (!_open) { StopPreview(); return; }
			if (Time.unscaledTime < _nextPlay) return;
			_playing.Preview();
			_nextPlay = _playing.PreviewPeriod <= 0f ? 0f : Time.unscaledTime + _playing.PreviewPeriod;
		}

		private void StartPreview(EffectDef def)
		{
			StopPreview();
			_playing = def;
			Previewing = true;
			_nextPlay = 0f;
		}

		private void StopPreview()
		{
			if (_playing == null) return;
			var def = _playing;
			_playing = null;
			Previewing = false;
			if (def.PreviewStop != null) def.PreviewStop();
		}

		private static bool F1Pressed()
		{
			var keyboard = Keyboard.current;
			return keyboard != null && keyboard.f1Key.wasPressedThisFrame;
		}

		private void SetOpen(bool open)
		{
			_open = open;
			IsOpen = open;
			if (open) ApplyInputBlock();
			else { StopPicking(); StopPreview(); ReleaseInputBlock(); }
		}

		private void ApplyInputBlock()
		{
			if (!_blockInput || _inputBlocked) return;
			try
			{
				OWInput.ChangeInputMode(InputMode.Menu);
				_inputBlocked = true;
			}
			catch (Exception e)
			{
				DualSenseMod.Debug("Could not block game input: " + e.Message);
			}
		}

		private void ReleaseInputBlock()
		{
			if (!_inputBlocked) return;
			_inputBlocked = false;
			try
			{
				OWInput.RestorePreviousInputs();
			}
			catch (Exception e)
			{
				DualSenseMod.Debug("Could not restore game input: " + e.Message);
			}
			EffectPreview.StopTriggers();
		}

		private void LateUpdate()
		{
			if (!_open) return;
			// The game locks and hides the cursor every frame; the menu needs it.
			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = true;
		}

		private void OnDestroy()
		{
			ReleaseInputBlock();
		}

		private void OnGUI()
		{
			if (!_open) return;
			GUI.depth = -1000;
			// Keep the whole window on the screen, whatever the resolution, so that the list can always be scrolled to its end.
			_window.height = Mathf.Min(720f, Screen.height - 40f);
			_window.x = Mathf.Clamp(_window.x, 0f, Mathf.Max(0f, Screen.width - _window.width));
			_window.y = Mathf.Clamp(_window.y, 0f, Mathf.Max(0f, Screen.height - _window.height));
			_window = GUILayout.Window(0x0D5E, _window, DrawWindow, "DualSense effects  (F1 closes)");
			DrawPickerCursor();
		}

		private void DrawWindow(int id)
		{
			_scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(_window.height - 130f));

			// Groups are paths ("World sounds/Campfire"): draw a header for every level that starts here, and the effects
			// only while all the levels above them are open.
			string[] last = new string[0];
			foreach (var def in Effects.All)
			{
				var parts = def.Group.Split('/');
				int same = 0;
				while (same < parts.Length && same < last.Length && parts[same] == last[same]) same++;
				for (int level = same; level < parts.Length; level++)
				{
					if (AncestorsOpen(parts, level)) DrawGroupHeader(string.Join("/", parts, 0, level + 1), parts[level], level);
				}
				last = parts;
				if (AncestorsOpen(parts, parts.Length)) DrawEffect(def, parts.Length);
			}

			GUILayout.EndScrollView();
			DrawFooter();
			GUI.DragWindow(new Rect(0, 0, 10000, 22));
		}

		/// <summary>True when the first <paramref name="count"/> levels of the path are all open.</summary>
		private bool AncestorsOpen(string[] parts, int count)
		{
			for (int i = 0; i < count; i++)
				if (!_openGroups.Contains(string.Join("/", parts, 0, i + 1))) return false;
			return true;
		}

		private void DrawGroupHeader(string group, string title, int level)
		{
			bool open = _openGroups.Contains(group);
			GUILayout.BeginHorizontal();
			GUILayout.Space(16 * level);
			if (GUILayout.Button((open ? "[-]  " : "[+]  ") + title, GUILayout.Height(26)))
			{
				if (open) _openGroups.Remove(group); else _openGroups.Add(group);
			}
			if (GUILayout.Button("Reset group", GUILayout.Width(100), GUILayout.Height(26)))
			{
				Effects.ResetGroup(group);
				Say("Group reset: " + group);
			}
			GUILayout.EndHorizontal();
		}

		private int _depth;

		private void DrawEffect(EffectDef def, int depth)
		{
			_depth = depth;
			bool open = _openEffects.Contains(def.Id);
			GUILayout.BeginHorizontal();
			GUILayout.Space(16 * depth);
			if (GUILayout.Button((open ? "- " : "+ ") + def.Label, GUILayout.Height(22)))
			{
				if (open) _openEffects.Remove(def.Id); else _openEffects.Add(def.Id);
			}
			if (def.Preview != null)
			{
				bool on = _playing == def;
				var before = GUI.color;
				if (on) GUI.color = new Color(1f, 0.55f, 0.45f);
				if (GUILayout.Button(on ? "Stop" : "Play", GUILayout.Width(60), GUILayout.Height(22)))
				{
					if (on) StopPreview(); else StartPreview(def);
				}
				GUI.color = before;
			}
			if (GUILayout.Button("Reset", GUILayout.Width(60), GUILayout.Height(22)))
				Effects.Reset(def);
			GUILayout.EndHorizontal();

			if (!open) return;

			if (!string.IsNullOrEmpty(def.Hint))
			{
				GUILayout.BeginHorizontal();
				GUILayout.Space(16 * depth + 16);
				GUILayout.Label(def.Hint);
				GUILayout.EndHorizontal();
			}
			if (IsColorEffect(def)) DrawColorEditor(def);
			else foreach (var p in def.Params) DrawParam(def, p);
			GUILayout.Space(6);
		}

		private void DrawParam(EffectDef def, Param p)
		{
			GUILayout.BeginHorizontal();
			GUILayout.Space(16 * _depth + 16);

			var previous = GUI.color;
			if (Math.Abs(p.Value - p.Default) > 1e-6f) GUI.color = new Color(1f, 0.9f, 0.4f); // edited
			GUILayout.Label(p.Label, GUILayout.Width(210));
			GUI.color = previous;

			float slid = GUILayout.HorizontalSlider(p.Value, p.Min, p.Max, GUILayout.MinWidth(120));
			if (Math.Abs(slid - p.Value) > 1e-6f)
			{
				p.Value = Effects.Clamp(p, slid);
				_editText.Remove(p);
			}

			string controlName = def.Id + "." + p.Key;
			GUI.SetNextControlName(controlName);
			bool focused = GUI.GetNameOfFocusedControl() == controlName;
			string shown;
			if (!focused || !_editText.TryGetValue(p, out shown)) shown = Format(p.Value);

			string typed = GUILayout.TextField(shown, GUILayout.Width(64));
			if (typed != shown)
			{
				_editText[p] = typed;
				float parsed;
				if (float.TryParse(typed.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
					p.Value = Effects.Clamp(p, parsed);
			}
			else if (!focused)
			{
				_editText.Remove(p);
			}
			GUILayout.EndHorizontal();
		}

		private static bool IsColorEffect(EffectDef def)
		{
			return def.Id.StartsWith("light.") && def.Id != "light.settings";
		}

		private static Color CurrentColor(EffectDef def)
		{
			return new Color(def.Int("r") / 255f, def.Int("g") / 255f, def.Int("b") / 255f, 1f);
		}

		private static void SetColor(EffectDef def, Color c)
		{
			def.Find("r").Value = Mathf.Round(Mathf.Clamp01(c.r) * 255f);
			def.Find("g").Value = Mathf.Round(Mathf.Clamp01(c.g) * 255f);
			def.Find("b").Value = Mathf.Round(Mathf.Clamp01(c.b) * 255f);
		}

		private static string Hex(Color c)
		{
			return string.Format("#{0:X2}{1:X2}{2:X2}", Mathf.RoundToInt(c.r * 255f), Mathf.RoundToInt(c.g * 255f), Mathf.RoundToInt(c.b * 255f));
		}

		/// <summary>Palette (hue bar + saturation/value square), hex field and the eyedropper for one colour.</summary>
		private void DrawColorEditor(EffectDef def)
		{
			EnsureTextures();
			Color color = CurrentColor(def);

			// Keep the hue when the colour is grey or black; recompute when the values were changed from outside.
			Vector3 hsv;
			Color fromHsv = _hsv.TryGetValue(def.Id, out hsv) ? Color.HSVToRGB(hsv.x, hsv.y, hsv.z) : Color.clear;
			if (Hex(fromHsv) != Hex(color))
			{
				Color.RGBToHSV(color, out hsv.x, out hsv.y, out hsv.z);
				_hsv[def.Id] = hsv;
			}

			Event e = Event.current;
			GUILayout.BeginHorizontal();
			GUILayout.Space(16 * _depth + 16);
			GUILayout.BeginVertical(GUILayout.Width(180));

			Rect sv = GUILayoutUtility.GetRect(180, 130, GUILayout.Width(180), GUILayout.Height(130));
			Rect hue = GUILayoutUtility.GetRect(180, 18, GUILayout.Width(180), GUILayout.Height(18));

			if (_svTextureHue != hsv.x) RebuildSvTexture(hsv.x);
			GUI.DrawTexture(sv, _svTexture);
			GUI.DrawTexture(hue, _hueTexture);

			// Markers.
			DrawMarker(new Vector2(sv.x + hsv.y * sv.width, sv.y + (1f - hsv.z) * sv.height), 10f);
			DrawMarker(new Vector2(hue.x + hsv.x * hue.width, hue.center.y), 14f);

			bool changed = false;
			if (e.type == EventType.MouseDown && e.button == 0)
			{
				if (sv.Contains(e.mousePosition)) { _dragSv = def.Id; _dragHue = null; }
				else if (hue.Contains(e.mousePosition)) { _dragHue = def.Id; _dragSv = null; }
			}
			if (e.type == EventType.MouseUp) { _dragSv = null; _dragHue = null; }
			if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0)
			{
				if (_dragSv == def.Id)
				{
					hsv.y = Mathf.Clamp01((e.mousePosition.x - sv.x) / sv.width);
					hsv.z = 1f - Mathf.Clamp01((e.mousePosition.y - sv.y) / sv.height);
					changed = true;
					e.Use();
				}
				else if (_dragHue == def.Id)
				{
					hsv.x = Mathf.Clamp01((e.mousePosition.x - hue.x) / hue.width);
					changed = true;
					e.Use();
				}
			}
			GUILayout.EndVertical();

			// Right side: swatch, hex field, eyedropper.
			GUILayout.BeginVertical();
			Rect swatch = GUILayoutUtility.GetRect(90, 40, GUILayout.Width(90), GUILayout.Height(40));
			var previous = GUI.color;
			GUI.color = color;
			GUI.DrawTexture(swatch, Texture2D.whiteTexture);
			GUI.color = previous;

			string hexControl = def.Id + ".hex";
			GUI.SetNextControlName(hexControl);
			bool hexFocused = GUI.GetNameOfFocusedControl() == hexControl;
			string hexShown;
			if (!hexFocused || !_hexText.TryGetValue(def.Id, out hexShown)) hexShown = Hex(color);
			string hexTyped = GUILayout.TextField(hexShown, 7, GUILayout.Width(90));
			if (hexTyped != hexShown)
			{
				_hexText[def.Id] = hexTyped;
				Color parsed;
				if (TryParseHex(hexTyped, out parsed))
				{
					SetColor(def, parsed);
					float h, s, v;
					Color.RGBToHSV(parsed, out h, out s, out v);
					_hsv[def.Id] = new Vector3(s > 0.001f ? h : hsv.x, s, v);
				}
			}
			else if (!hexFocused)
			{
				_hexText.Remove(def.Id);
			}

			bool picking = _pickTarget == def.Id;
			if (GUILayout.Button(picking ? "Click the screen..." : "Pick from screen", GUILayout.Width(120), GUILayout.Height(24)))
			{
				if (picking) StopPicking(); else StartPicking(def.Id);
			}
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();

			if (changed)
			{
				_hsv[def.Id] = hsv;
				SetColor(def, Color.HSVToRGB(hsv.x, hsv.y, hsv.z));
			}
		}

		private static bool TryParseHex(string text, out Color color)
		{
			color = Color.black;
			text = text.Trim().TrimStart('#');
			int value;
			if (text.Length != 6 || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) return false;
			color = new Color(((value >> 16) & 0xFF) / 255f, ((value >> 8) & 0xFF) / 255f, (value & 0xFF) / 255f, 1f);
			return true;
		}

		private static void DrawMarker(Vector2 center, float size)
		{
			var previous = GUI.color;
			GUI.color = Color.black;
			GUI.DrawTexture(new Rect(center.x - size / 2 - 1, center.y - size / 2 - 1, size + 2, size + 2), Texture2D.whiteTexture);
			GUI.color = Color.white;
			GUI.DrawTexture(new Rect(center.x - size / 2 + 1, center.y - size / 2 + 1, size - 2, size - 2), Texture2D.whiteTexture);
			GUI.color = previous;
		}

		private void EnsureTextures()
		{
			if (_hueTexture != null) return;
			_hueTexture = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
			for (int x = 0; x < 256; x++) _hueTexture.SetPixel(x, 0, Color.HSVToRGB(x / 255f, 1f, 1f));
			_hueTexture.Apply();
			_svTexture = new Texture2D(48, 48, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
		}

		private void RebuildSvTexture(float hue)
		{
			for (int y = 0; y < 48; y++)
				for (int x = 0; x < 48; x++)
					_svTexture.SetPixel(x, y, Color.HSVToRGB(hue, x / 47f, y / 47f));
			_svTexture.Apply();
			_svTextureHue = hue;
		}

		// ---- Eyedropper: takes the colour of the pixel under the mouse from the rendered game image.

		private void StartPicking(string effectId)
		{
			_pickTarget = effectId;
			if (_hoverLoop != null) StopCoroutine(_hoverLoop);
			_hoverLoop = StartCoroutine(SampleUnderMouse());
		}

		private void StopPicking()
		{
			_pickTarget = null;
			if (_hoverLoop != null) StopCoroutine(_hoverLoop);
			_hoverLoop = null;
		}

		private void UpdatePicking()
		{
			var mouse = Mouse.current;
			if (mouse == null) return;
			var keyboard = Keyboard.current;
			if (mouse.rightButton.wasPressedThisFrame || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame))
			{
				StopPicking();
				return;
			}
			if (!mouse.leftButton.wasPressedThisFrame) return;

			// Clicks on the menu itself are ignored, anywhere else takes the colour under the cursor.
			Vector2 p = mouse.position.ReadValue();
			if (_window.Contains(new Vector2(p.x, Screen.height - p.y))) return;

			string target = _pickTarget;
			StopPicking();
			StartCoroutine(PickOnce(target, p));
		}

		private IEnumerator PickOnce(string effectId, Vector2 screenPoint)
		{
			yield return new WaitForEndOfFrame();
			Color c = ReadPixel(screenPoint);
			var def = Effects.Get(effectId);
			SetColor(def, c);
			Say("Picked " + Hex(c));
		}

		private IEnumerator SampleUnderMouse()
		{
			while (_pickTarget != null)
			{
				yield return new WaitForEndOfFrame();
				var mouse = Mouse.current;
				if (mouse != null) _hoverColor = ReadPixel(mouse.position.ReadValue());
			}
		}

		private static Color ReadPixel(Vector2 screenPoint)
		{
			var texture = new Texture2D(1, 1, TextureFormat.RGB24, false);
			try
			{
				int x = Mathf.Clamp((int)screenPoint.x, 0, Screen.width - 1);
				int y = Mathf.Clamp((int)screenPoint.y, 0, Screen.height - 1);
				texture.ReadPixels(new Rect(x, y, 1, 1), 0, 0);
				texture.Apply();
				return texture.GetPixel(0, 0);
			}
			finally
			{
				Destroy(texture);
			}
		}

		/// <summary>A swatch next to the cursor showing the colour that a click would pick.</summary>
		private void DrawPickerCursor()
		{
			if (_pickTarget == null) return;
			Vector2 m = Event.current.mousePosition;
			var frame = new Rect(m.x + 14, m.y + 14, 64, 40);
			var previous = GUI.color;
			GUI.color = Color.black;
			GUI.DrawTexture(new Rect(frame.x - 2, frame.y - 2, frame.width + 4, frame.height + 4), Texture2D.whiteTexture);
			GUI.color = _hoverColor;
			GUI.DrawTexture(frame, Texture2D.whiteTexture);
			GUI.color = previous;
			GUI.Label(new Rect(frame.x, frame.yMax + 4, 220, 40), Hex(_hoverColor) + "  click: pick, right click: cancel");
		}

		private void DrawFooter()
		{
			GUILayout.Space(4);
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Save", GUILayout.Height(28)))
			{
				Effects.Save();
				Say("Saved to effects.json");
			}
			if (GUILayout.Button("Reload saved", GUILayout.Height(28)))
			{
				Effects.ResetAll();
				Effects.Load();
				Say("Reloaded saved values");
			}
			if (GUILayout.Button("Reset ALL to defaults", GUILayout.Height(28)))
			{
				Effects.ResetAll();
				Say("All values reset (press Save to keep)");
			}
			GUILayout.EndHorizontal();

			bool block = GUILayout.Toggle(_blockInput, "Freeze game controls while this menu is open");
			if (block != _blockInput)
			{
				_blockInput = block;
				if (block) ApplyInputBlock(); else ReleaseInputBlock();
			}

			if (Time.unscaledTime < _statusUntil) GUILayout.Label(_status);
		}

		private void Say(string text)
		{
			_status = text;
			_statusUntil = Time.unscaledTime + 4f;
		}

		private static string Format(float v)
		{
			return v.ToString("0.##", CultureInfo.InvariantCulture);
		}
	}
}
