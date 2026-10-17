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

			const string steps = "Footsteps";
			Define("step.base", steps, "Step thump", "A haptic thump on every footstep, at the moment the foot lands. Faster movement is stronger.")
				.Add("strength", "Strength", 0.7f, 0, 1.5f)
				.Add("freq", "Tone Hz", 55, 20, 150)
				.Add("durationMs", "Length ms", 90, 30, 300)
				.Add("speedScale", "Stronger when running", 0.5f, 0, 1)
				.Add("panAmount", "Left/right foot emphasis", 0.7f, 0, 1);
			Define("step.surface", steps, "Ground types", "Soft: grass, sand, snow, foliage, water. Hard: stone, metal, glass, ice. The rest is medium.")
				.Add("softMul", "Soft ground strength", 0.6f, 0, 2)
				.Add("mediumMul", "Medium ground strength", 1f, 0, 2)
				.Add("hardMul", "Hard ground strength", 1.3f, 0, 2)
				.Add("softFreqSub", "Soft ground: tone lower by Hz", 15, 0, 40)
				.Add("hardFreqAdd", "Hard ground: tone higher by Hz", 35, 0, 100);

			const string chr = "Character";
			Define("char.jump", chr, "Jump", "A push-off thump when the character jumps.")
				.Add("strength", "Strength", 0.6f, 0, 1.5f)
				.Add("freq", "Tone Hz", 70, 20, 150)
				.Add("durationMs", "Length ms", 110, 30, 300);
			Define("char.landing", chr, "Landing", "Strength follows how hard the game counts the landing. Ground types as for footsteps.")
				.Add("strength", "Strength", 0.9f, 0, 1.5f)
				.Add("freq", "Tone Hz", 45, 20, 150)
				.Add("durationMs", "Length ms", 160, 40, 400)
				.Add("softMul", "Soft ground strength", 0.6f, 0, 2)
				.Add("mediumMul", "Medium ground strength", 1f, 0, 2)
				.Add("hardMul", "Hard ground strength", 1.3f, 0, 2);
			Define("char.hit", chr, "Collisions", "Bumping into something that is not a landing: light, medium and heavy (the game's own three classes).")
				.Add("lightStrength", "Light: strength", 0.5f, 0, 1.5f)
				.Add("lightFreq", "Light: tone Hz", 80, 20, 200)
				.Add("lightMs", "Light: length ms", 90, 30, 500)
				.Add("mediumStrength", "Medium: strength", 0.9f, 0, 1.5f)
				.Add("mediumFreq", "Medium: tone Hz", 55, 20, 200)
				.Add("mediumMs", "Medium: length ms", 160, 30, 600)
				.Add("heavyStrength", "Heavy: strength", 1.3f, 0, 1.5f)
				.Add("heavyFreq", "Heavy: tone Hz", 38, 20, 200)
				.Add("heavyMs", "Heavy: length ms", 300, 30, 800);
			Define("char.damage", chr, "Damage", "When the character is hurt. Bigger damage is stronger. The impact thump is on top of the collision above.")
				.Add("impactStrength", "Impact: strength", 0.6f, 0, 1.5f)
				.Add("impactFreq", "Impact: tone Hz", 50, 20, 200)
				.Add("punctureStrength", "Puncture: strength", 0.8f, 0, 1.5f)
				.Add("punctureFreq", "Puncture: tone Hz", 150, 40, 300)
				.Add("punctureMs", "Puncture: length ms", 120, 30, 600)
				.Add("electricStrength", "Electrical: strength", 0.9f, 0, 1.5f)
				.Add("electricFreq", "Electrical: tone Hz", 110, 40, 300)
				.Add("electricMs", "Electrical: length ms", 350, 50, 1000)
				.Add("electricChop", "Electrical: chop Hz", 28, 0, 80);

			const string fatal = "Deaths (the game's own death sounds)";
			Define("death.live", fatal, "Death sounds as haptics", "The death sounds are played as haptics with their own loudness. Raise the gains if they are too weak.")
				.Add("bandGain", "Low-frequency part gain", 3f, 0.5f, 10)
				.Add("rumbleGain", "Creak/hiss rumble gain", 2.5f, 0, 10);
			Define("death.sand", fatal, "Crushed by sand", "Quiet creaking that builds up, a big crunch at the end, then silence.")
				.Add("gain", "Strength", 1f, 0, 3);
			Define("death.elevator", fatal, "Crushed by an elevator", "One short, hard crunch.")
				.Add("gain", "Strength", 1f, 0, 3);
			Define("death.instant", fatal, "Deadly collision / sudden death", "A loud blow followed by a long ringing that fades over about four seconds.")
				.Add("gain", "Strength", 1f, 0, 3);
			Define("death.other", fatal, "Other deaths", "Energy, supernova, digestion, lava, time loop, big bang and the rest.")
				.Add("gain", "Strength", 1f, 0, 3);

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
