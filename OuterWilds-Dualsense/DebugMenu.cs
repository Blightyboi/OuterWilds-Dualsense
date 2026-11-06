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
		private Rect _window = new Rect(20, 20, 960, 760);
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
			HapticAudio.PreviewScope = true;
			try { _playing.Preview(); }
			finally { HapticAudio.PreviewScope = false; }
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
			_window.width = Mathf.Min(960f, Screen.width - 40f);
			_window.height = Mathf.Min(760f, Screen.height - 40f);
			_window.x = Mathf.Clamp(_window.x, 0f, Mathf.Max(0f, Screen.width - _window.width));
			_window.y = Mathf.Clamp(_window.y, 0f, Mathf.Max(0f, Screen.height - _window.height));
			_window = GUILayout.Window(0x0D5E, _window, DrawWindow, "DualSense effects  (F1 closes)");
			DrawPickerCursor();
		}

		// ---- Look: the sidebar tabs, the group banners and the effect cards are three clearly different things.

		private GUIStyle _sTab, _sTabOn, _sSection, _sSub, _sCard, _sCardTitle, _sHint, _sPlay, _sStop, _sSmall, _sTitle, _sCount;

		private static Texture2D Solid(Color c)
		{
			var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
			t.SetPixel(0, 0, c);
			t.Apply();
			t.hideFlags = HideFlags.HideAndDontSave;
			return t;
		}

		private static GUIStyle Flat(Color normal, Color hover, Color text, int size, FontStyle font, TextAnchor anchor, int pad)
		{
			var s = new GUIStyle(GUI.skin.button);
			var n = Solid(normal);
			var h = Solid(hover);
			s.normal.background = s.focused.background = s.onNormal.background = s.onFocused.background = n;
			s.hover.background = s.active.background = s.onHover.background = s.onActive.background = h;
			s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = text;
			s.onNormal.textColor = s.onHover.textColor = s.onActive.textColor = s.onFocused.textColor = text;
			s.fontSize = size;
			s.fontStyle = font;
			s.alignment = anchor;
			s.richText = true;
			s.border = new RectOffset(0, 0, 0, 0);
			s.padding = new RectOffset(pad, pad, 2, 2);
			s.margin = new RectOffset(0, 0, 2, 2);
			return s;
		}

		private void EnsureStyles()
		{
			if (_sTab != null) return;
			_sTab = Flat(new Color(0.15f, 0.16f, 0.19f), new Color(0.24f, 0.26f, 0.31f), new Color(0.85f, 0.87f, 0.9f), 14, FontStyle.Normal, TextAnchor.MiddleLeft, 12);
			_sTabOn = Flat(new Color(0.95f, 0.58f, 0.16f), new Color(1f, 0.66f, 0.26f), new Color(0.1f, 0.07f, 0.02f), 14, FontStyle.Bold, TextAnchor.MiddleLeft, 12);
			_sSection = Flat(new Color(0.1f, 0.38f, 0.5f), new Color(0.14f, 0.46f, 0.6f), Color.white, 15, FontStyle.Bold, TextAnchor.MiddleLeft, 10);
			_sSub = Flat(new Color(0.24f, 0.31f, 0.38f), new Color(0.3f, 0.39f, 0.47f), new Color(0.92f, 0.95f, 1f), 13, FontStyle.Bold, TextAnchor.MiddleLeft, 10);
			_sCardTitle = Flat(new Color(0f, 0f, 0f, 0f), new Color(1f, 1f, 1f, 0.08f), new Color(0.95f, 0.95f, 0.95f), 13, FontStyle.Normal, TextAnchor.MiddleLeft, 4);
			_sPlay = Flat(new Color(0.2f, 0.55f, 0.32f), new Color(0.26f, 0.66f, 0.4f), Color.white, 12, FontStyle.Bold, TextAnchor.MiddleCenter, 4);
			_sStop = Flat(new Color(0.75f, 0.28f, 0.22f), new Color(0.86f, 0.35f, 0.28f), Color.white, 12, FontStyle.Bold, TextAnchor.MiddleCenter, 4);
			_sSmall = Flat(new Color(0.3f, 0.31f, 0.35f), new Color(0.4f, 0.41f, 0.46f), new Color(0.9f, 0.9f, 0.92f), 12, FontStyle.Normal, TextAnchor.MiddleCenter, 4);
			_sCard = new GUIStyle(GUI.skin.box);
			_sCard.normal.background = Solid(new Color(0.11f, 0.12f, 0.14f));
			_sCard.padding = new RectOffset(8, 8, 4, 6);
			_sCard.margin = new RectOffset(0, 0, 3, 3);
			_sHint = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 12, fontStyle = FontStyle.Italic };
			_sHint.normal.textColor = new Color(0.65f, 0.68f, 0.72f);
			_sTitle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
			_sTitle.normal.textColor = new Color(0.95f, 0.58f, 0.16f);
			_sCount = new GUIStyle(GUI.skin.label) { fontSize = 12 };
			_sCount.normal.textColor = new Color(0.65f, 0.68f, 0.72f);
		}

		private string _section;
		private string _search = "";
		private List<string> _sections;
		private readonly Dictionary<string, int> _sectionCount = new Dictionary<string, int>();

		private static string Top(EffectDef def)
		{
			int slash = def.Group.IndexOf('/');
			return slash < 0 ? def.Group : def.Group.Substring(0, slash);
		}

		private static bool IsEdited(EffectDef def)
		{
			foreach (var p in def.Params) if (Math.Abs(p.Value - p.Default) > 1e-6f) return true;
			return false;
		}

		private void EnsureSections()
		{
			if (_sections != null) return;
			_sections = new List<string>();
			foreach (var def in Effects.All)
			{
				string top = Top(def);
				if (!_sections.Contains(top)) _sections.Add(top);
				int n;
				_sectionCount.TryGetValue(top, out n);
				_sectionCount[top] = n + 1;
			}
			if (_section == null && _sections.Count > 0) _section = _sections[0];
		}

		private void DrawWindow(int id)
		{
			EnsureStyles();
			EnsureSections();

			// Search across all sections.
			GUILayout.BeginHorizontal();
			GUILayout.Label("Search", GUILayout.Width(50));
			string searched = GUILayout.TextField(_search, GUILayout.Height(24));
			if (searched != _search) { _search = searched; _scroll = Vector2.zero; }
			if (_search.Length > 0 && GUILayout.Button("x", _sSmall, GUILayout.Width(28), GUILayout.Height(24)))
			{
				_search = "";
				_scroll = Vector2.zero;
			}
			GUILayout.EndHorizontal();
			GUILayout.Space(4);

			GUILayout.BeginHorizontal();
			DrawSidebar();
			GUILayout.Space(8);
			GUILayout.BeginVertical();
			DrawContent();
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();

			DrawFooter();
			GUI.DragWindow(new Rect(0, 0, 10000, 22));
		}

		/// <summary>The sections (first level of the group path) as a column of tabs; the open one is orange.</summary>
		private void DrawSidebar()
		{
			var edited = new HashSet<string>();
			foreach (var def in Effects.All) if (IsEdited(def)) edited.Add(Top(def));

			bool searching = _search.Trim().Length >= 2;
			GUILayout.BeginVertical(GUILayout.Width(210));
			foreach (var section in _sections)
			{
				bool on = !searching && section == _section;
				string text = section + "  <color=#8a8f99>" + _sectionCount[section] + "</color>" + (edited.Contains(section) ? "  <color=#ffd966>*</color>" : "");
				if (GUILayout.Button(text, on ? _sTabOn : _sTab, GUILayout.Height(32)))
				{
					_section = section;
					_search = "";
					_scroll = Vector2.zero;
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.Label("*  has values changed from the defaults", _sCount);
			GUILayout.EndVertical();
		}

		private bool Matches(EffectDef def)
		{
			string q = _search.Trim();
			return def.Label.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
				|| def.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
				|| def.Group.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private int CountIn(string group)
		{
			int n = 0;
			foreach (var def in Effects.All) if (def.Group == group || def.Group.StartsWith(group + "/")) n++;
			return n;
		}

		private void DrawContent()
		{
			bool searching = _search.Trim().Length >= 2;

			// Title row of the section.
			GUILayout.BeginHorizontal();
			GUILayout.Label(searching ? "Search: " + _search.Trim() : _section, _sTitle);
			GUILayout.FlexibleSpace();
			if (!searching)
			{
				if (GUILayout.Button("Expand all", _sSmall, GUILayout.Width(90), GUILayout.Height(24))) SetSectionOpen(true);
				if (GUILayout.Button("Collapse all", _sSmall, GUILayout.Width(90), GUILayout.Height(24))) SetSectionOpen(false);
				if (GUILayout.Button("Reset section", _sSmall, GUILayout.Width(100), GUILayout.Height(24)))
				{
					Effects.ResetGroup(_section);
					Say("Section reset: " + _section);
				}
			}
			GUILayout.EndHorizontal();
			GUILayout.Space(4);

			_scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(_window.height - 190f));
			string[] last = new string[0];
			string lastGroup = null;
			int shown = 0;
			foreach (var def in Effects.All)
			{
				var parts = def.Group.Split('/');
				if (searching)
				{
					if (!Matches(def)) continue;
					shown++;
					if (def.Group != lastGroup)
					{
						lastGroup = def.Group;
						GUILayout.Space(4);
						GUILayout.Label(def.Group.Replace("/", "   >   "), _sSub);
					}
					DrawEffect(def, 0);
					continue;
				}

				if (parts[0] != _section) continue;
				shown++;

				// Subgroups: a banner for every level below the section that starts here; the effects only while all the
				// banners above them are open.
				int same = 0;
				while (same < parts.Length && same < last.Length && parts[same] == last[same]) same++;
				for (int level = Math.Max(same, 1); level < parts.Length; level++)
				{
					if (AncestorsOpen(parts, level)) DrawGroupHeader(string.Join("/", parts, 0, level + 1), parts[level], level);
				}
				last = parts;
				if (AncestorsOpen(parts, parts.Length)) DrawEffect(def, 14 * Math.Max(parts.Length - 1, 0));
			}
			if (shown == 0) GUILayout.Label(searching ? "Nothing found." : "Empty.", _sHint);
			GUILayout.EndScrollView();
		}

		private void SetSectionOpen(bool open)
		{
			foreach (var def in Effects.All)
			{
				if (Top(def) != _section) continue;
				var parts = def.Group.Split('/');
				for (int level = 1; level < parts.Length; level++)
				{
					string path = string.Join("/", parts, 0, level + 1);
					if (open) _openGroups.Add(path); else _openGroups.Remove(path);
				}
			}
		}

		/// <summary>True when the levels 1..count-1 of the path (everything below the section) are all open.</summary>
		private bool AncestorsOpen(string[] parts, int count)
		{
			for (int i = 1; i < count; i++)
				if (!_openGroups.Contains(string.Join("/", parts, 0, i + 1))) return false;
			return true;
		}

		private void DrawGroupHeader(string group, string title, int level)
		{
			bool open = _openGroups.Contains(group);
			GUILayout.Space(level == 1 ? 6 : 2);
			GUILayout.BeginHorizontal();
			GUILayout.Space(14 * (level - 1));
			string text = (open ? "v   " : ">   ") + (level == 1 ? title.ToUpperInvariant() : title) + "   <color=#9db4c4>" + CountIn(group) + "</color>";
			if (GUILayout.Button(text, level == 1 ? _sSection : _sSub, GUILayout.Height(level == 1 ? 30 : 26)))
			{
				if (open) _openGroups.Remove(group); else _openGroups.Add(group);
			}
			if (GUILayout.Button("Reset", _sSmall, GUILayout.Width(56), GUILayout.Height(level == 1 ? 30 : 26)))
			{
				Effects.ResetGroup(group);
				Say("Group reset: " + group);
			}
			GUILayout.EndHorizontal();
		}

		private int _depth;

		/// <summary>One effect as a dark card: the title with Play and Reset on the right, the values below when opened.</summary>
		private void DrawEffect(EffectDef def, int indent)
		{
			_depth = 0;
			bool open = _openEffects.Contains(def.Id);
			GUILayout.BeginHorizontal();
			GUILayout.Space(indent);
			GUILayout.BeginVertical(_sCard);

			GUILayout.BeginHorizontal();
			string title = (open ? "v   " : ">   ") + def.Label + (IsEdited(def) ? "   <color=#ffd966>(changed)</color>" : "");
			if (GUILayout.Button(title, _sCardTitle, GUILayout.Height(24)))
			{
				if (open) _openEffects.Remove(def.Id); else _openEffects.Add(def.Id);
			}
			if (def.Preview != null)
			{
				bool on = _playing == def;
				if (GUILayout.Button(on ? "Stop" : "Play", on ? _sStop : _sPlay, GUILayout.Width(56), GUILayout.Height(24)))
				{
					if (on) StopPreview(); else StartPreview(def);
				}
			}
			if (GUILayout.Button("Reset", _sSmall, GUILayout.Width(56), GUILayout.Height(24)))
				Effects.Reset(def);
			GUILayout.EndHorizontal();

			if (open)
			{
				if (!string.IsNullOrEmpty(def.Hint)) GUILayout.Label(def.Hint, _sHint);
				if (IsColorEffect(def)) DrawColorEditor(def);
				else foreach (var p in def.Params) DrawParam(def, p);
			}

			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
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
