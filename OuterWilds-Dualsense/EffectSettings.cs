using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace OuterWildsDualSense
{
	/// <summary>One tunable number of an effect.</summary>
	public sealed class Param
	{
		public string Key;
		public string Label;
		public float Min, Max, Default, Value;
		public bool Integer;
	}

	/// <summary>A named effect (a trigger effect or a haptic setup) made of tunable parameters.</summary>
	public sealed class EffectDef
	{
		public string Id;
		public string Group;
		public string Label;
		public string Hint;
		public readonly List<Param> Params = new List<Param>();

		/// <summary>Plays the effect once so it can be felt from the menu. Null if there is nothing to play.</summary>
		public Action Preview;

		public EffectDef Add(string key, string label, float def, float min, float max, bool integer = false)
		{
			Params.Add(new Param { Key = key, Label = label, Default = def, Value = def, Min = min, Max = max, Integer = integer });
			return this;
		}

		public Param Find(string key)
		{
			for (int i = 0; i < Params.Count; i++)
				if (Params[i].Key == key) return Params[i];
			throw new KeyNotFoundException(Id + "." + key);
		}

		public float this[string key] { get { return Find(key).Value; } }
		public int Int(string key) { return (int)Math.Round(Find(key).Value); }
	}

	/// <summary>
	/// All effect parameters in one place. The mod reads its numbers from here, the F1 menu edits them and
	/// they are saved to effects.json in the mod folder (only values that differ from the defaults).
	/// </summary>
	public static class Effects
	{
		public static readonly List<EffectDef> All = new List<EffectDef>();
		private static readonly Dictionary<string, EffectDef> ById = new Dictionary<string, EffectDef>();

		public static EffectDef Get(string id) { return ById[id]; }

		private static EffectDef Define(string id, string group, string label, string hint = null)
		{
			var def = new EffectDef { Id = id, Group = group, Label = label, Hint = hint };
			All.Add(def);
			ById[id] = def;
			return def;
		}

		static Effects()
		{
			const string jp = "Jetpack triggers";
			Define("jp.rest", jp, "R2 thrust (spring)", "Resistance of R2 during normal jetpack use. Grows from start to end strength along the travel.")
				.Add("start", "Strength at top", 2, 1, 8, true)
				.Add("end", "Strength at bottom", 5, 1, 8, true);
			Define("jp.boost", jp, "R2 boost (push-out pulses)", "Held while the booster fires.")
				.Add("freq", "Frequency Hz", 8, 1, 40, true)
				.Add("amp", "Amplitude", 8, 1, 8, true)
				.Add("zone", "Start zone", 0, 0, 9, true);
			Define("jp.lowfuel", jp, "R2 low fuel warning", "Buzz while thrusting with little fuel left.")
				.Add("threshold", "Fuel below %", 20, 0, 100)
				.Add("freq", "Frequency Hz", 10, 1, 40, true)
				.Add("amp", "Amplitude", 3, 1, 8, true)
				.Add("zone", "Start zone", 1, 0, 9, true);
			Define("jp.down", jp, "L2 thrust down")
				.Add("strength", "Strength", 2, 1, 8, true)
				.Add("zone", "Start zone", 0, 0, 9, true);

			const string ship = "Ship triggers";
			Define("ship.rest", ship, "R2/L2 resting", "Resistance of both triggers while at the flight console and not thrusting.")
				.Add("strength", "Strength", 3, 1, 8, true)
				.Add("zone", "Start zone", 1, 0, 9, true);
			Define("ship.thrust", ship, "R2/L2 thrusting", "Vibration of the trigger that matches the thrust direction. Grows with thrust.")
				.Add("freqMin", "Frequency Hz at low thrust", 12, 1, 60, true)
				.Add("freqMax", "Frequency Hz at full thrust", 24, 1, 60, true)
				.Add("ampMin", "Amplitude at low thrust", 2, 1, 8, true)
				.Add("ampMax", "Amplitude at full thrust", 5, 1, 8, true)
				.Add("zone", "Start zone", 1, 0, 9, true);

			const string hap = "Haptics";
			Define("hap.live", hap, "Engine sounds (live audio)", "How the engine sounds become haptics. Strength follows the real thrust of the jetpack and ship thrusters.")
				.Add("gain", "Master gain", 1, 0, 4)
				.Add("startThrust", "Thrust needed to start (0-1)", 0.05f, 0, 0.5f)
				.Add("gainFloor", "Strength just after start", 0.15f, 0, 1)
				.Add("curve", "Response curve (1 = linear)", 1f, 0.3f, 2.5f)
				.Add("refVolume", "Volume counted as full thrust", 0.25f, 0.05f, 1)
				.Add("bandTarget", "Loudness of the hum", 0.45f, 0.05f, 1)
				.Add("lowPass", "Low-pass Hz", 350, 100, 1000)
				.Add("highPass", "High-pass Hz", 25, 5, 100)
				.Add("rumbleLow", "Hiss rumble: low tone Hz", 62, 30, 150)
				.Add("rumbleHigh", "Hiss rumble: high tone Hz", 97, 30, 200);
			Define("hap.rumble", hap, "Game rumble (impacts, boost)", "The game's controller vibration, played as haptics.")
				.Add("lowHz", "Big motor tone Hz", 48, 20, 120)
				.Add("highHz", "Small motor tone Hz", 135, 60, 300)
				.Add("lowLevel", "Big motor level", 0.9f, 0, 1.5f)
				.Add("highLevel", "Small motor level", 0.6f, 0, 1.5f);

			const string light = "Lightbar";
			Define("light.color", light, "Lightbar colour")
				.Add("r", "Red", 255, 0, 255, true)
				.Add("g", "Green", 110, 0, 255, true)
				.Add("b", "Blue", 20, 0, 255, true);
		}

		private static string FilePath
		{
			get { return Path.Combine(DualSenseMod.Instance.ModHelper.Manifest.ModFolderPath, "effects.json"); }
		}

		/// <summary>Loads saved values over the defaults. Unknown or invalid entries are ignored.</summary>
		public static void Load()
		{
			try
			{
				if (!File.Exists(FilePath)) return;
				var saved = JsonConvert.DeserializeObject<Dictionary<string, float>>(File.ReadAllText(FilePath));
				if (saved == null) return;
				foreach (var def in All)
					foreach (var p in def.Params)
					{
						float v;
						if (saved.TryGetValue(def.Id + "." + p.Key, out v)) p.Value = Clamp(p, v);
					}
			}
			catch (Exception e)
			{
				DualSenseMod.Log("Could not load effects.json: " + e.Message);
			}
		}

		/// <summary>Saves the values that differ from their defaults.</summary>
		public static void Save()
		{
			try
			{
				var changed = new Dictionary<string, float>();
				foreach (var def in All)
					foreach (var p in def.Params)
						if (Math.Abs(p.Value - p.Default) > 1e-6f) changed[def.Id + "." + p.Key] = p.Value;
				if (changed.Count == 0)
				{
					if (File.Exists(FilePath)) File.Delete(FilePath);
				}
				else
				{
					File.WriteAllText(FilePath, JsonConvert.SerializeObject(changed, Formatting.Indented));
				}
				DualSenseMod.Log("Effect settings saved (" + changed.Count + " changed values).");
			}
			catch (Exception e)
			{
				DualSenseMod.Log("Could not save effects.json: " + e.Message);
			}
		}

		public static void Reset(EffectDef def)
		{
			foreach (var p in def.Params) p.Value = p.Default;
		}

		public static void ResetGroup(string group)
		{
			foreach (var def in All) if (def.Group == group) Reset(def);
		}

		public static void ResetAll()
		{
			foreach (var def in All) Reset(def);
		}

		public static float Clamp(Param p, float v)
		{
			if (p.Integer) v = (float)Math.Round(v);
			return Math.Max(p.Min, Math.Min(p.Max, v));
		}
	}
}
