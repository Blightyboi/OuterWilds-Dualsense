using System;
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
		private bool _blockInput = true;
		private bool _inputBlocked;
		private Rect _window = new Rect(20, 20, 560, 720);
		private Vector2 _scroll;
		private string _status = "";
		private float _statusUntil;

		private readonly HashSet<string> _openGroups = new HashSet<string>();
		private readonly HashSet<string> _openEffects = new HashSet<string>();
		private readonly Dictionary<Param, string> _editText = new Dictionary<Param, string>();

		private void Update()
		{
			if (F1Pressed()) SetOpen(!_open);
		}

		private static bool F1Pressed()
		{
			var keyboard = Keyboard.current;
			return keyboard != null && keyboard.f1Key.wasPressedThisFrame;
		}

		private void SetOpen(bool open)
		{
			_open = open;
			if (open) ApplyInputBlock();
			else ReleaseInputBlock();
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
			_window = GUILayout.Window(0x0D5E, _window, DrawWindow, "DualSense effects  (F1 closes)");
		}

		private void DrawWindow(int id)
		{
			_scroll = GUILayout.BeginScrollView(_scroll);

			string lastGroup = null;
			foreach (var def in Effects.All)
			{
				if (def.Group != lastGroup)
				{
					lastGroup = def.Group;
					DrawGroupHeader(def.Group);
				}
				if (_openGroups.Contains(def.Group)) DrawEffect(def);
			}

			GUILayout.EndScrollView();
			DrawFooter();
			GUI.DragWindow(new Rect(0, 0, 10000, 22));
		}

		private void DrawGroupHeader(string group)
		{
			bool open = _openGroups.Contains(group);
			GUILayout.BeginHorizontal();
			if (GUILayout.Button((open ? "[-]  " : "[+]  ") + group, GUILayout.Height(26)))
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

		private void DrawEffect(EffectDef def)
		{
			bool open = _openEffects.Contains(def.Id);
			GUILayout.BeginHorizontal();
			GUILayout.Space(16);
			if (GUILayout.Button((open ? "- " : "+ ") + def.Label, GUILayout.Height(22)))
			{
				if (open) _openEffects.Remove(def.Id); else _openEffects.Add(def.Id);
			}
			if (def.Preview != null && GUILayout.Button("Play", GUILayout.Width(60), GUILayout.Height(22)))
				def.Preview();
			if (GUILayout.Button("Reset", GUILayout.Width(60), GUILayout.Height(22)))
				Effects.Reset(def);
			GUILayout.EndHorizontal();

			if (!open) return;

			if (!string.IsNullOrEmpty(def.Hint))
			{
				GUILayout.BeginHorizontal();
				GUILayout.Space(32);
				GUILayout.Label(def.Hint);
				GUILayout.EndHorizontal();
			}
			foreach (var p in def.Params) DrawParam(def, p);
			GUILayout.Space(6);
		}

		private void DrawParam(EffectDef def, Param p)
		{
			GUILayout.BeginHorizontal();
			GUILayout.Space(32);

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
