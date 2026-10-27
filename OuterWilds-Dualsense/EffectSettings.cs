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

		/// <summary>
		/// Seconds between repeats while the Play button is on. 0 = refresh every frame (for effects that hold while
		/// refreshed, such as triggers and colours); otherwise the length of the effect plus a short pause.
		/// </summary>
		public float PreviewPeriod;

		/// <summary>Called when the preview is stopped, to release anything that was being held.</summary>
		public Action PreviewStop;

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
			const string jp = "Triggers/Jetpack";
			Define("jp.rest", jp, "R2 thrust (spring)", "Resistance of R2 during normal jetpack use. Grows from start to end strength along the travel.")
				.Add("start", "Strength at top", 2, 1, 8, true)
				.Add("end", "Strength at bottom", 5, 1, 8, true);
			Define("jp.boost", jp, "R2 boost (push-out pulses)", "Held while the booster fires.")
				.Add("freq", "Frequency Hz", 12, 1, 40, true)
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

			const string ship = "Triggers/Ship";
			Define("ship.rest", ship, "R2/L2 resting", "Resistance of both triggers while at the flight console and not thrusting.")
				.Add("strength", "Strength", 3, 1, 8, true)
				.Add("zone", "Start zone", 1, 0, 9, true);
			Define("ship.thrust", ship, "R2/L2 thrusting", "Vibration of the trigger that matches the thrust direction. Grows with thrust.")
				.Add("freqMin", "Frequency Hz at low thrust", 12, 1, 60, true)
				.Add("freqMax", "Frequency Hz at full thrust", 24, 1, 60, true)
				.Add("ampMin", "Amplitude at low thrust", 2, 1, 8, true)
				.Add("ampMax", "Amplitude at full thrust", 5, 1, 8, true)
				.Add("zone", "Start zone", 1, 0, 9, true);

			const string hap = "Player/Engine sounds";
			Define("hap.live", hap, "Engine sounds (live audio)", "How the engine sounds become haptics. Strength follows the real thrust of the jetpack and ship thrusters.")
				.Add("gain", "Master gain", 1, 0, 4)
				.Add("startThrust", "Thrust needed to start (0-1)", 0f, 0, 0.5f)
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

			Define("touch.pad", "Touchpad", "Touchpad gestures (map, ship log)", "One finger pans the map and scrolls log text, two fingers moving together rotate the map, a pinch zooms. Negative numbers flip a direction. Only works in the map and the ship log, over USB.")
				.Add("enabled", "On (1) / off (0)", 1, 0, 1, true)
				.Add("prompts", "On-screen prompt (1) / off (0)", 1, 0, 1, true)
				.Add("speed", "Finger speed px/s for full stick", 1500, 200, 5000)
				.Add("pan", "Pan", 1, -3, 3)
				.Add("rotate", "Rotate", 1, -3, 3)
				.Add("zoom", "Zoom (pinch)", 1, -3, 3)
				.Add("scroll", "Text scroll", 1, -3, 3);

			Define("gyro.roll", "Gyro", "Gyro roll (ship, jetpack in zero gravity)", "Tilting the controller sideways rolls like the stick. The on/off switch is in the mod settings of the pause menu. Hold L3 and press R3 to set the level. Negative multiplier flips the direction.")
				.Add("deadzone", "Dead zone degrees", 8, 0, 30)
				.Add("maxAngle", "Tilt for full roll degrees", 45, 15, 90)
				.Add("roll", "Roll strength (negative flips)", 1, -3, 3)
				.Add("pitch", "Pitch strength, tilt forward/back (negative flips)", 1, -3, 3)
				.Add("yaw", "Yaw strength, turn left/right (negative flips)", 1, -3, 3)
				.Add("smoothing", "Smoothing Hz (lower = smoother)", 8, 1, 30);

			const string tools = "Tools";
			Define("tool.equip", tools, "Tool taken out", "Tool_Take_Out_02, 0.45 s, low end only 0.02: the signalscope, the translator and the probe launcher all use it (PlayEquipTool).")
				.Add("gain", "Strength", 10f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("tool.unequip", tools, "Tool put away", "Tool_Put_Away_01, 0.64 s, low end 0.02: the same three tools.")
				.Add("gain", "Strength", 12f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("tool.click", tools, "Tool mode click", "OW_SP_AnalogClick2_v2, 0.25 s, almost silent: switching the signalscope frequency and the probe launcher mode.")
				.Add("gain", "Strength", 12f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("scope.identify", tools, "Signalscope: signal identified", "LogUpdated_001, 0.5 s, a loud chime (0.29) with nothing in the low band.")
				.Add("gain", "Strength", 4f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("scope.static", tools, "Signalscope: static", "signalscope_static, a 19 s loop of hiss (0.01), the noise of the scope.")
				.Add("gain", "Strength", 15f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("flashlight.switch", tools, "Flashlight on and off", "flashlightOn / flashlightOff, 0.5 s, nothing in the low band: a click.")
				.Add("gain", "Strength", 15f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("flashlight.flicker", tools, "Flashlight flickering", "Flashlight_Malfunction_02/03, 0.65 s, nothing in the low band: the flashlight stutters.")
				.Add("gain", "Strength", 10f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("probe.photo", tools, "Probe: photo", "Probe_SnapShot_01..03, 0.45 s, no low end: the probe takes a picture.")
				.Add("gain", "Strength", 10f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("mallow.replace", tools, "Marshmallow: new one and toss", "Marshmallow_Replace_01 (also the menu tab sound, so only on the player) and _02: a soft pop.")
				.Add("gain", "Strength", 12f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("mallow.puff", tools, "Marshmallow: blowing it out", "mallowpuff1/3/4, 1.4 s, no low end.")
				.Add("gain", "Strength", 12f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("mallow.eat", tools, "Marshmallow: eating", "OW_PR_MarshmallowEatUnburnt / EatBurnt, 2.3-3 s, almost silent.")
				.Add("gain", "Strength", 12f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("repair.loop", tools, "Repair tool: repairing", "Repair_Loop, a 16.6 s loop of a quiet whir (0.04).")
				.Add("gain", "Strength", 8f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("repair.done", tools, "Repair tool: finished", "ShipRepair_Finish, 3 s, low end up to 0.02.")
				.Add("gain", "Strength", 8f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("scope.zoom", tools, "Signalscope: zoom", "Signalscope_Zoom_Loop, a 5.8 s loop (0.05) without low end: the hum made from the volume.")
				.Add("gain", "Strength", 8f, 0, 20);
			Define("probe.launch", tools, "Probe launcher: launch", "OW_PR_ThrowProbe, 2 s, a launch thump: low end up to 0.22.")
				.Add("gain", "Strength", 2.5f, 0, 15);
			Define("probe.launch.water", tools, "Probe launcher: launch under water", "OW_PR_ProbeUnderwaterLaunch, 2.5 s, low end up to 0.19.")
				.Add("gain", "Strength", 3f, 0, 15);
			Define("probe.retrieve", tools, "Probe launcher: probe retrieved", "OW_PR_ProbeRetrieval, 2 s, low end up to 0.05.")
				.Add("gain", "Strength", 10f, 0, 20);
			Define("probe.flight", tools, "Probe in flight", "OW_PR_ProbeInAirSound, a 2 s loop on the probe itself, a steady low end of 0.02. Falls off with the distance to the probe.")
				.Add("gain", "Strength", 7f, 0, 20);
			Define("probe.attach", tools, "Probe sticking to a surface", "Probe_Attach_v3_01/02, 0.42 s, low end up to 0.14, on the probe.")
				.Add("gain", "Strength", 3.8f, 0, 15);
			Define("mallow.equip", tools, "Marshmallow stick pulled out", "OW_PR_PullOutStick, 0.5 s, low end up to 0.17.")
				.Add("gain", "Strength", 3.2f, 0, 15);
			Define("mallow.ignite", tools, "Marshmallow catching fire", "Ignite_Marshmallow_01..03 (also the menu key rebind sound, so only on the player), 0.8 s, low end up to 0.05.")
				.Add("gain", "Strength", 8f, 0, 20);

			const string damage = "Ship/Damage";
			Define("dmg.spark", damage, "Spark of a broken part", "Spark_01..13, 0.5 s one-shots (peak 0.9) without low end.")
				.Add("gain", "Strength", 12f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("dmg.electric", damage, "Electrical damage hiss", "OW_SP_ElectricalDamageLP, a 10 s loop (0.02) without low end.")
				.Add("gain", "Strength", 10f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("dmg.glass", damage, "Cockpit glass cracking", "glass_crack_01..03, 1.8 s, nothing in the low band.")
				.Add("gain", "Strength", 12f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("dmg.oxyleak", damage, "Ship oxygen leak", "OW_PR_OxygenLeakingFromSuit_loop on the ship, a 1 s hiss (0.01).")
				.Add("gain", "Strength", 12f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("suit.leak", "Player/Suit", "Suit leak (punctured by spikes)", "OW_PR_OxygenLeakingFromSuit_loop_louder, a 1 s hiss (0.11) without low end; the game fades it with the number of holes.")
				.Add("gain", "Strength", 6f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("suit.patch", "Player/Suit", "Patching a suit hole", "Fix_Puncture_01..08, 0.8 s, a sharp start (0.09) and silence.")
				.Add("gain", "Strength", 12f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("gasp.light", "Player/Gasps", "Gasp: light (first loop wake-up)", "gasp_light9, 4.7 s, peak 0.2 without low end: a soft sigh, so ticks.")
				.Add("gain", "Strength", 12f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("gasp.medium", "Player/Gasps", "Gasp: medium (waking up, leaving a dream)", "gasp_normal11..13, 1.4-2.8 s, a breath at the start with low end up to 0.02.")
				.Add("gain", "Strength", 6f, 0, 20);
			Define("gasp.heavy", "Player/Gasps", "Gasp: heavy (waking after suffocating)", "gasp_traumatic3 and 7, 5-5.8 s, low end up to 0.08 in the first 1.5 s.")
				.Add("gain", "Strength", 2f, 0, 10);
			Define("dmg.failure", damage, "Power failure", "Power_Failure_v2_02, 3.4 s, low end up to 0.04.")
				.Add("gain", "Strength", 3.5f, 0, 15);
			Define("dmg.fuelleak", damage, "Fuel tank leak", "Ship_FuelLeak, a 16 s loop (0.05), low end 0.01.")
				.Add("gain", "Strength", 12f, 0, 40);

			const string items = "Items";
			Define("item.pickup", items, "Picking up an item", "Pickup_Rock_01 (scroll, shared stone, conversation stone) and Pickup_Ceramic_01 (warp core), 2.7-2.9 s: a knock at the start (low end up to 0.03) and then silence.")
				.Add("gain", "Strength", 12f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("item.drop", items, "Putting an item down", "PutDown_Rock_01 and PutDown_Ceramic_01, 2.7-2.9 s: a knock at the start (low end up to 0.02).")
				.Add("gain", "Strength", 12f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("stone.insert", items, "Shared stone into its socket", "linkingstone_in, 1.3 s, almost silent (peak 0.03).")
				.Add("gain", "Strength", 20f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("stone.remove", items, "Shared stone out of its socket", "linkingstone_out, 1.3 s, almost silent (0.01).")
				.Add("gain", "Strength", 20f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("core.insert", items, "Warp core into its receptacle", "WarpCore_Insert_V3_01, 3.2 s, low end up to 0.14.")
				.Add("gain", "Strength", 3.8f, 0, 15);
			Define("core.remove", items, "Warp core out of its receptacle", "WarpCore_Remove_V3_01, 3.3 s, low end up to 0.13.")
				.Add("gain", "Strength", 3.8f, 0, 15);

			const string steps = "Player/Footsteps";
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

			const string chr = "Player/Character";
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

			const string fatal = "Player/Deaths (the game's own sounds)";
			Define("death.live", fatal, "Death sounds as haptics", "The death sounds are played as haptics with their own loudness. Raise the gains if they are too weak.")
				.Add("bandGain", "Low-frequency part gain", 3f, 0.5f, 10)
				.Add("rumbleGain", "Creak/hiss rumble gain", 2.5f, 0, 10);
			Define("death.sand", fatal, "Crushed by sand", "Crushed_To_Death_V2_01, 5.7 s: quiet creaking that builds, a big crunch at 4.5 s, then silence.")
				.Add("gain", "Strength", 1f, 0, 3);
			Define("death.elevator", fatal, "Crushed by an elevator", "CrushedByElevator, 1.2 s: one short, hard crunch.")
				.Add("gain", "Strength", 1f, 0, 3);
			Define("death.instant", fatal, "Deadly collision / sudden death", "InstantDeath2_Long_Ringing, 4.9 s: a loud blow, then a ringing that fades over about four seconds. Used for collisions and for every death by health running out, which includes ghost matter, fire, heat and electricity, and for any death without a sound of its own.")
				.Add("gain", "Strength", 1f, 0, 3);
			Define("death.energy", fatal, "Burning (sun, supernova, dream explosion)", "Incinerate_v3_01/02, 3 s: strong at first, gone after two seconds.")
				.Add("gain", "Strength", 1f, 0, 3);
			Define("death.digestion", fatal, "Eaten (anglerfish)", "Anglerfish_Awake4, 4.9 s: a steady growl that fades. The anglerfish of Dark Bramble. The game code uses the same sound for two creatures that look like DLC content (a swamp water monster and a ghost that haunts rooms); they were not checked in the game.")
				.Add("gain", "Strength", 1f, 0, 3);
			Define("death.lava", fatal, "Lava", "Lava_Splash_01/02, 2.7 s: a splash that hits at once and dies away.")
				.Add("gain", "Strength", 1f, 0, 3);
			Define("death.bigbang", fatal, "Big bang (Eye of the Universe finale)", "BigBang_EndFlash, 4.8 s: a swelling roar that lasts and then fades. The player is killed by it when the cosmic inflation at the Eye of the Universe reaches them; the supernova is not this one, it is under Burning.")
				.Add("gain", "Strength", 1f, 0, 3);
			Define("death.timeloop", fatal, "Time loop ending", "OW NM Flashback stinger, 3.9 s: swells up until the end.")
				.Add("gain", "Strength", 1f, 0, 3);
			Define("death.helmet", fatal, "Helmet cracking", "Helmet_Glass_Crack_08, 0.7 s: breaking glass with nothing in the low band, so only a light tick. Plays on top of a death sound.")
				.Add("gain", "Strength", 1f, 0, 3);
			Define("death.groan", fatal, "Ship creaking (eaten inside the ship)", "ShipEatenGroan (OW_SP_MetalCreak), about 4.6 s: soft metal creaking, starts a second after the death sound.")
				.Add("gain", "Strength", 1f, 0, 3);
			Define("death.shipexplosion", fatal, "Ship explosion", "OW_SP_ShipExploding, 4 s: very strong at the start, gone after three seconds. Only when the player is close enough to be hit.")
				.Add("gain", "Strength", 1f, 0, 3);

			const string hazard = "Player/Hazards (while they hurt)";
			Define("hazard.live", hazard, "Hazard damage", "The sound of a hazard is felt with a strength that follows the damage per second the game computes for it.")
				.Add("fullDamage", "Damage per second counted as full strength", 20f, 1, 100);
			Define("hazard.ghost", hazard, "Ghost matter", "OW_NM_BlackHole_Lp, a steady low drone that plays while ghost matter hurts. Dying of it uses the sudden death sound.")
				.Add("gain", "Strength", 1.5f, 0, 5);
			Define("hazard.fire", hazard, "Fire", "Player_On_Fire_Loop, a soft crackle while the character burns.")
				.Add("gain", "Strength", 3f, 0, 5);
			Define("hazard.heat", hazard, "Heat and other damage", "Heat_Damage_Loop_01, a soft hiss in hot places. Also used for the hazards that have no loop of their own.")
				.Add("gain", "Strength", 3f, 0, 5);

			const string world = "World sounds";
			const string splash = "World sounds/Splashes";
			const string fire = "World sounds/Campfire";
			Define("splash.world", world, "Reach of sounds around you", "Splashes, the gravity cannon and similar sounds are felt whenever the game plays them, whoever causes them. Strength falls off with the distance to the sound; one-shots also lean toward the side they come from.")
				.Add("range", "Reach (m)", 25f, 2, 100)
				.Add("curve", "Falloff curve (1 = linear)", 1.5f, 0.5f, 4f);
			Define("splash.ghost", splash, "Splash in ghost matter", "GhostMatter_Splash_v4_*, 1.8 s, hits at once. The child Arkose on Timber Hearth throws a rock into ghost matter every four to eight seconds.")
				.Add("gain", "Strength", 1.5f, 0, 5);
			Define("splash.lava", splash, "Splash in lava", "Lava_Splash_01/02, 2.7 s.")
				.Add("gain", "Strength", 1.2f, 0, 5);
			Define("splash.water", splash, "Splash in water", "SmallSplash, MediumSplash and the raft and large splashes.")
				.Add("gain", "Strength", 1f, 0, 5);
			Define("splash.other", splash, "Other splashes")
				.Add("gain", "Strength", 1f, 0, 5);

			const string memories = "World sounds/Memories (flashback, memory uplink)";
			Define("mem.base", memories, "Base loop", "OW NM Flashback 082818_3 AP base (flashback after death, 30 s) and 082818 AP loop (memory uplink). The game fades it in and out and the haptic follows that.")
				.Add("gain", "Strength", 1.5f, 0, 5);
			Define("mem.overlay", memories, "Overlay loop", "OW NM Flashback 082818 AP overlay1 / loop overlay AP. Quiet, builds up with the scene.")
				.Add("gain", "Strength", 2.5f, 0, 6);
			Define("mem.slam", memories, "Memory uplink start (slam)", "OW NM Flashback 081718 AP slam: a heavy blow that rings out for about four seconds.")
				.Add("gain", "Strength", 1.2f, 0, 5);
			Define("mem.end", memories, "End stinger", "OW NM Flashback 082818_2 AP stinger, 3.4 s: swells until the very end.")
				.Add("gain", "Strength", 1.2f, 0, 5);

			const string gravity = "World sounds/Gravity (crystals, beam, cannon)";
			Define("grav.crystal", gravity, "Gravity crystal", "OW_NM_GravityCrystalAmbience_Louder, a steady hum with plenty of low end. The game fades it in when you enter the crystal's area, and by distance.")
				.Add("gain", "Strength", 1.5f, 0, 6);
			Define("grav.flicker", gravity, "Gravity crystal (flickering)", "OW_NM_FlickeryGravityCrystalAmbience: almost nothing in the low band, so only a faint tremble comes out of it. Turn it up if you want it felt.")
				.Add("gain", "Strength", 4f, 0, 10);
			Define("grav.beam", gravity, "Gravity beam (tractor beam) hum", "OW_NM_TractorBeamLP, a steady 34 s loop with plenty of low end. Felt by distance: strongest inside the beam.")
				.Add("gain", "Strength", 1.5f, 0, 6);
			Define("grav.beamswitch", gravity, "Gravity beam power up and down", "OW_NM_TractorBeamPowerUp (6.6 s, a surge at the start, then fading) and PowerDown (4.3 s, a soft rising hum). The power-up clip is also used by other Nomai devices switching on.")
				.Add("gain", "Strength", 1.5f, 0, 6);
			Define("grav.cannon", gravity, "Gravity cannon hum", "OW_NM_GravityCannonAmbience, a steady 56 s loop that the game fades in while the cannon is active. Also used for the Vessel's ambience.")
				.Add("gain", "Strength", 1.5f, 0, 6);
			Define("fire.crackle", fire, "Campfire crackle", "OW_TH_Campfire_loop_01 (also the dream fire and the lodge fire): a quiet 20 s hiss with pops and nothing in the low band, so every pop becomes a short tick. Felt by the fire's own sound range; half strength while the fire smolders.")
				.Add("gain", "Strength", 6f, 0, 8)
				.Add("tone", "Tick tone Hz", 140f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 40f, 3, 200);
			Define("fire.sleep", fire, "Campfire crackle while dozing off", "The same clip on the player's own source while sleeping at a fire (fades in over 3 s). The fires around are silent in the haptics meanwhile.")
				.Add("gain", "Strength", 4f, 0, 8)
				.Add("tone", "Tick tone Hz", 120f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 25f, 3, 200);
			Define("grav.shot", gravity, "Gravity cannon start and stop", "OW_NM_GravityCannonActivated / Deactivated, 8 s: build-up and a heavy blow at about 5.3 s.")
				.Add("gain", "Strength", 1.2f, 0, 6);
			Define("fire.ignite", fire, "Lighting a fire", "Ignite_CampFire_01..04, about 1.8 s with a blow at the start and real low end. Played whenever a campfire is lit anywhere (also when a slide reel is burned and in the post-credits scene).")
				.Add("gain", "Strength", 3.5f, 0, 10);
			Define("pick.hit", "World sounds/Miner", "Pickaxe hit", "pickaxe_01/02, 0.15 s and nothing in the low band, so a short tick. The miner on Timber Hearth.")
				.Add("gain", "Strength", 2f, 0, 10)
				.Add("tone", "Tick tone Hz", 150f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);

			const string lift = "World sounds/Elevator";
			Define("lift.shot", lift, "Elevator start and arrival", "elevatorstart (1.3 s, a blow at the start) and elevatorstop (1.8 s). Played by Elevator for every elevator on Timber Hearth.")
				.Add("gain", "Strength", 2.2f, 0, 8);
			Define("lift.loop", lift, "Elevator ride", "elevatorloop, a 5 s loop with a lot of low end that Elevator fades in while the cab moves.")
				.Add("gain", "Strength", 6f, 0, 8);

			const string geyser = "World sounds/Geysers and water";
			Define("geyser.shot", geyser, "Geyser start and end", "OW_TH_GeyserStart (4.3 s, a blow) and OW_TH_GeyserEnd (3.4 s). A geyser is active 10 s and quiet 10 s.")
				.Add("gain", "Strength", 3f, 0, 10);
			Define("geyser.loop", geyser, "Geyser roar", "TH_Geyser_Loop_v3, a 15 s loop the geyser fades in while it is active.")
				.Add("gain", "Strength", 2f, 0, 10);
			Define("water.wheel", geyser, "Water wheel", "OW_TH_Waterwheel_loop, a steady 27 s loop with low end. Felt by the sound's own range.")
				.Add("gain", "Strength", 1.7f, 0, 10);
			Define("water.fall", geyser, "Waterfall", "OW_TH_Waterfall_loop_01, a 57 s wide rush with moderate low end.")
				.Add("gain", "Strength", 2.5f, 0, 10);
			Define("water.under", geyser, "Under water", "OW_TH_Underwater / UnderwaterRushing, 32 s loops with a lot of low end, heard while the player is under water. Follows the game's own volume.")
				.Add("gain", "Strength", 1.7f, 0, 10);

			const string orb = "World sounds/Nomai orbs";
			Define("orb.drag", orb, "Dragging an orb with the gaze", "Orb_Roll_Energy_Loop_v2_01 (7.7 s, hissy energy sound, little low end, so a rumble that follows the sound's level). NomaiOrbAudio fades it with the orb's speed while the player drags and plays it at half volume at most, so the strength is high here.")
				.Add("gain", "Strength", 4f, 0, 20);
			Define("orb.roll", orb, "Orb rolling on its own", "OW_NM_OrbMoveGlass_lp_01, a quiet glassy loop (volume at most 0.2) that plays while an orb moves, dragged or not.")
				.Add("gain", "Strength", 10f, 0, 30);
			Define("orb.grab", orb, "Grabbing an orb", "Orb_End_v3_01, 1 s with a short blow at the start: a tick.")
				.Add("gain", "Strength", 1.5f, 0, 10)
				.Add("tone", "Tick tone Hz", 130f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 25f, 3, 200);
			Define("orb.slot", orb, "Orb settling in a slot", "OW_NM_OrbDeSelect_Energy_02, 0.8 s and very quiet: a tick. Opens a door or sets the planet locator.")
				.Add("gain", "Strength", 2.5f, 0, 10)
				.Add("tone", "Tick tone Hz", 170f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);

			const string mech = "World sounds/Nomai mechanisms";
			Define("mech.big", mech, "Big Nomai parts turning (dish rings, airlocks)", "OW_NM_DoorSlide_Big_LP_01 (NomaiAirlockSlide_LP), a 7 s loop with a lot of low end. On Attlerock SignalDishRingController plays it on each of the three rings of the signal dish and sets its volume to the ring's speed. Also the Nomai airlocks on other planets.")
				.Add("gain", "Strength", 3f, 0, 15);
			const string gd = "World sounds/Giant's Deep";
			Define("tornado.roar", gd, "Tornado roar", "OW_GD_Tornado_v2_01..04 (24-30 s, low end 0.25, the strongest natural sound measured). Every TornadoController has a source on an AudioRail that follows the nearest point of the tornado; the game fades it by the player's sector, the formation and collapse, and under water. Strength follows that fade and the sound's range.")
				.Add("gain", "Strength", 1.5f, 0, 10);
			Define("tornado.ride", gd, "Being carried by a tornado", "TornadoFluidVolume pushes the player's detector (or the ship's, when the player is inside) toward the air's speed: 300 m/s up and a pull toward the axis (100 m/s from 20 m out, zero on the axis). Strength = floor + (1 - floor) x how far the body still is from the air's speed, calmer near the axis. A blow when you are thrown in, a soft lurch and a fading tail when you leave, and the haptic leans toward the side the pull goes to. Not a sound: the real force.")
				.Add("gain", "Strength", 1f, 0, 4)
				.Add("floor", "Strength that stays once carried (0-1)", 0.45f, 0, 1)
				.Add("center", "Strength on the axis compared to the edge (0-1)", 0.6f, 0, 1)
				.Add("jolt", "Blow when thrown in", 1f, 0, 3)
				.Add("lurch", "Soft lurch when leaving", 0.35f, 0, 2)
				.Add("release", "Fade after leaving (s)", 0.7f, 0.1f, 3)
				.Add("lean", "Lean toward the pull (0-1)", 0.8f, 0, 1)
				.Add("lowHz", "Low tone Hz", 42f, 20, 120)
				.Add("highHz", "High tone Hz", 90f, 40, 220)
				.Add("wobbleHz", "Turbulence Hz", 3.5f, 0.5f, 12);
			Define("tornado.triggers", "Triggers/Tornado", "R2 and L2 held by the tornado", "While a tornado pushes the player or the ship the player is in, both triggers are at full resistance, over everything else, until you are out of it.")
				.Add("strength", "Resistance (8 = the most)", 8, 1, 8, true)
				.Add("zone", "Start zone", 0, 0, 9, true);
			const string isl = "World sounds/Giant's Deep/Islands";
			Define("island.lift", isl, "Island sucked up by a tornado", "OW_GD_IslandSuckedInTornado, 2.8 s with a blow (low end up to 0.25). IslandController.OnIslandEnteredTornadoEvent -> IslandAudioController plays it on the island's source; the rain zones and the shore waves switch off while the island is in the air.")
				.Add("gain", "Strength", 2.1f, 0, 10);
			Define("island.splash", isl, "Island crashing into the sea", "OW_GD_IslandCrashingInWater_v2, 11.9 s, a heavy blow with a long tail (low end up to 0.29). IslandController.OnSpawnSplash: the island's fluid detector enters water -> IslandAudioController plays it; the rain and the waves come back.")
				.Add("gain", "Strength", 2f, 0, 10);
			Define("island.waves", isl, "Waves on the shore", "OW_GD_WavesAgainstBeach and OW_GD_WavesAgainstRock, 51 s and 39 s, almost no low end (0.02), so this is a faint rumble that follows the sound's level. ShoreAudioRail sources the game fades in while the player is on the island and the island is not in the air.")
				.Add("gain", "Strength", 5f, 0, 20);
			
			const string sea = "World sounds/Giant's Deep/Ocean";
			Define("gd.current", sea, "Underwater current", "OW_GD_UnderwaterCurrent, 26 s, low end 0.12: an even rush under the surface. The AudioShell_UnderwaterCurrent source in Audio_GDInterior; the game fades it, so the strength follows that.")
				.Add("gain", "Strength", 2f, 0, 10);
			Define("gd.ambience", sea, "Ocean, underwater and cave beds", "OW_GD_AmbienceOcean (the surface), OW_GD_AmbienceUndewater (under water and the core) and OW_GD_AmbienceCave: 95-120 s, very quiet (low end 0.035). AudioVolume zones that fade in over 2 s, so a faint steady rumble.")
				.Add("gain", "Strength", 3.5f, 0, 15);
			const string spark = "World sounds/Giant's Deep/Electricity";
			Define("gd.barrier", spark, "Electric barrier", "OW_GD_ElectricBarrier_Idle_Loop, 11.5 s, low end 0.10: the hum of the barrier around the core (AudioShell_ElectricBarrier in Audio_GDInterior).")
				.Add("gain", "Strength", 2f, 0, 10);
			Define("shock.hit", spark, "Electric shock", "ElectricityVolume.ApplyShock (the barrier and the jellyfish): when a detector that is not insulated is hit, and again every second while it stays. The player gets the blow of the 10 m/s kick (the damage buzz is separate); the ship, with the player inside, gets the blow and the electrical buzz. The shock sounds themselves have no low end (0.01), so this follows the event.")
				.Add("strength", "Blow: strength", 1f, 0, 1.5f)
				.Add("freq", "Blow: tone Hz", 55, 20, 200)
				.Add("ms", "Blow: length ms", 220, 30, 800)
				.Add("buzzStrength", "Ship buzz: strength", 0.9f, 0, 1.5f)
				.Add("buzzFreq", "Ship buzz: tone Hz", 110, 40, 300)
				.Add("buzzMs", "Ship buzz: length ms", 350, 50, 1000)
				.Add("buzzChop", "Ship buzz: chop Hz", 28, 0, 80);
			const string hollow = "World sounds/Brittle Hollow";
			Define("bh.meteor", hollow, "Meteor hitting the planet", "Meteor_Impact_01..03_b, 3.8 s, a blow with real low end (up to 0.45). MeteorController.Impact plays it on the meteor's own source where it hits and damages the fragment under it by 20-80; a fragment that runs out of integrity then breaks away 0.1 s later (next effect).")
				.Add("gain", "Strength", 1.5f, 0, 10);
			Define("bh.fragment", hollow, "A fragment breaking away", "Fragment_Break, 8.5 s, a long heavy rumble (low end 0.24 on average, up to 0.41). DetachableFragment.Detach plays it on the fragment's floating source, at most once a second. The same fragments also break by themselves on the planet's own schedule, not only from meteors.")
				.Add("gain", "Strength", 1f, 0, 10);
			Define("bh.hole.approach", hollow, "Black hole: approaching", "OW_NM_WHAmbience2_v2, 34 s with a lot of low end (0.31, the strongest ambience measured). The BlackHoleAmbience source sits in BlackHole_BH and the game grades it by distance with its own rolloff, so it grows as you fly to the hole. The signal dish plays the same clip for Brittle Hollow (dish.hollow).")
				.Add("gain", "Strength", 1.6f, 0, 10);
			Define("bh.hole.transit", hollow, "Black hole: passing through", "BlackHole_02 (SingularityOnPlayerEnterExit), 1.5 s, low end 0.33 with a blow up to 0.57. Falling into the hole is silent in the game; this plays when a white hole lets the player out, on the player's own source. It is also the sound of dying in a black hole.")
				.Add("gain", "Strength", 1f, 0, 10);
			Define("bh.hole.emission", hollow, "Black hole: swallowing something", "OW_NM_BHEnterExitPlayer_v2, 2 s, a blow up to 0.55. BlackHoleVolume.Vanish plays it at the hole when a ship, a probe or a meteor vanishes while the player is in the hole's audio sector.")
				.Add("gain", "Strength", 1f, 0, 10);
			Define("bh.platform", hollow, "A platform breaking", "Platform_Break_V2_01..04, 3.4 s, a crunch with low end (up to 0.18). The breakable platforms of the crash site (Prefab_NOM_BreakablePlatform, 15 sources); the sound is set in the scene.")
				.Add("gain", "Strength", 2.5f, 0, 10);
			Define("bh.forge", hollow, "Black hole forge hum", "Forge_Loop_V3_03, 9 s, low end 0.24: a steady hum on AudioSource_BlackHoleForge. The game never changes it: BlackHoleForgeMovement only moves the forge between its three levels (5 s, eased) when a slot is triggered, so this is constant around the forge.")
				.Add("gain", "Strength", 1f, 0, 10);
			Define("bh.ambience", hollow, "Surface and under-crust beds", "BH_Ambience_Surface (121 s, low end 0.015) and BH_Ambience_Below_Crust (98 s, 0.03): very quiet AudioVolume zones of Volumes_BH that fade in over 2 s, so a faint steady rumble.")
				.Add("gain", "Strength", 5f, 0, 20);
			const string suit = "Player/Suit";
			Define("suit.suit", suit, "Putting on and taking off the suit", "OW_PR_SuitOn (2.6 s) and OW_PR_SuitOff (2.0 s): cloth and straps, low end 0.01-0.04, so the haptic follows the rustle. Not played when the game does it instantly.")
				.Add("gain", "Strength", 2.5f, 0, 10)
				.Add("tone", "Tick tone Hz", 90f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 12f, 3, 200);
			Define("suit.helmet", suit, "Putting on and taking off the helmet", "OW_PR_HelmetOn (1.4 s, peak 0.1) and OW_PR_HelmetOff (2.0 s): soft clicks, almost silent, so a light tick.")
				.Add("gain", "Strength", 4f, 0, 15)
				.Add("tone", "Tick tone Hz", 130f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 25f, 3, 200);
			Define("suit.refuel", suit, "Refuelling the jetpack", "OW_SP_RefuelJetpack_v2_short, 1.9 s: a quiet start and a burst at 0.8 s with low end up to 0.12. PlayerRecoveryPoint plays it when the player uses a recovery point and the fuel is not full.")
				.Add("gain", "Strength", 3.5f, 0, 15);
			Define("suit.medkit", suit, "Healing (medkit)", "medkit, 1.2 s, no low end at all (a quiet clank and hiss), so a tick that follows the sound. PlayerRecoveryPoint plays it when the player uses a recovery point and health is not full.")
				.Add("gain", "Strength", 3f, 0, 15)
				.Add("tone", "Tick tone Hz", 150f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 20f, 3, 200);
			Define("suit.breath", suit, "Breathing in the suit", "breathing_suit3, 26 s, low end 0.01-0.02 with a clear in-and-out rhythm. PlayerBreathingAudio fades it in while there is no oxygen around, the helmet is on and the player is not suffocating, so it only plays when the suit's air is being used.")
				.Add("gain", "Strength", 8f, 0, 30);
			Define("suit.breath.low", suit, "Breathing, suit air nearly gone", "breathing_lowO2_6, 15 s, a faster and rougher breath (low end up to 0.04). Replaces the normal breathing when the oxygen falls to the game's critical level.")
				.Add("gain", "Strength", 8f, 0, 30);
			const string qm = "World sounds/Quantum Moon";
			Define("qm.shuttle", qm, "Nomai shuttle impact", "OW_NM_ShuttleLight, 8 s: a heavy first blow (low end 0.29) that dies out in about two seconds. ImpactAudio on the shuttle's body.")
				.Add("gain", "Strength", 2.5f, 0, 10);
			Define("qm.lightning", qm, "Lightning", "OW Quantum Lightning 091118 AP 01..04, 6 s, low end up to 0.18. QuantumLightningObject flashes at random every 5-15 s in the Eye state. Within 40 m the game also pulses its own rumble, which comes on top of this.")
				.Add("gain", "Strength", 2.5f, 0, 10);
			Define("qm.cairn", qm, "Solanum raising a cairn", "Solanum_RocksForm, 3.2 s, rocks forming (low end 0.03), a tick that follows the rustle. Audio_Solanum's AudioSource_Cairns.")
				.Add("gain", "Strength", 2f, 0, 10)
				.Add("tone", "Tick tone Hz", 90f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 15f, 3, 200);
			Define("qm.symbol", qm, "Solanum's symbol appearing", "Solanum_IconAppear_V3, 6.9 s (low end 0.04, up to 0.08): a tick that follows the sound. SolanumAnimController plays it when a symbol is revealed.")
				.Add("gain", "Strength", 2f, 0, 10)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 12f, 3, 200);
			Define("qm.eye", qm, "The Eye state pulling you", "QuantumMoonEyeFluidVolume pulls the player's detector (or the ship's, when the player is inside) toward the pole at up to 20 m/s and inward at up to 10 m/s, stronger toward the outer radius. The same ride as the tornado but gentle: a soft rumble that leans toward the pull. Not a sound: the real force.")
				.Add("gain", "Strength", 0.8f, 0, 4)
				.Add("floor", "Strength that stays once carried (0-1)", 0.3f, 0, 1)
				.Add("center", "Strength at the inner radius compared to the outer (0-1)", 0.3f, 0, 1)
				.Add("jolt", "Blow when it takes hold", 0.2f, 0, 3)
				.Add("lurch", "Soft lurch when leaving", 0.15f, 0, 2)
				.Add("release", "Fade after leaving (s)", 0.8f, 0.1f, 3)
				.Add("lean", "Lean toward the pull (0-1)", 0.8f, 0, 1)
				.Add("lowHz", "Low tone Hz", 34f, 20, 120)
				.Add("highHz", "High tone Hz", 66f, 40, 220)
				.Add("wobbleHz", "Turbulence Hz", 2.5f, 0.5f, 12);
			const string door = "World sounds/Nomai doors";
			Define("door.small", door, "Small door sliding", "OW_NM_DoorSlide_LP_02, 5 s, low end 0.10: the loop NomaiMultiPartDoor plays while the door moves (Play on Cycle, cut when it ends). Orb-operated doors of the ruins.")
				.Add("gain", "Strength", 2f, 0, 10);
			Define("door.small.start", door, "Small door starting", "OW_NM_DoorStart_01..09, 1 s, almost no low end (0.03): a short tick that follows the sound.")
				.Add("gain", "Strength", 3f, 0, 15)
				.Add("tone", "Tick tone Hz", 100f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 25f, 3, 200);
			Define("door.small.stop", door, "Small door stopping", "Nomai_Stone_Door_End_V2_01..11, 2.2 s: a stone thud at the start (low end up to 0.14).")
				.Add("gain", "Strength", 3f, 0, 15);
			Define("door.big", door, "Big door sliding", "OW_NM_DoorSlide_Big_LP_05, 2.4 s loop with a lot of low end (0.25): NomaiGateway's slide (the shrine gates of the Quantum Moon, the city doors of Ember Twin).")
				.Add("gain", "Strength", 2.5f, 0, 10);
			Define("door.big.start", door, "Big door starting", "OW_NM_DoorStart_Big_01..07, 1.7 s, low end 0.11 (up to 0.23): the gateway starts to move.")
				.Add("gain", "Strength", 2.5f, 0, 10);
			Define("door.big.stop", door, "Big door stopping", "Nomai_Stone_Door_End_Big_V2_01..11, 2.7 s: a heavy stone thud at the start (low end up to 0.13). Also played by the Nomai airlocks and the Memory Uplink statue.")
				.Add("gain", "Strength", 3.5f, 0, 15);
			Define("obs.tornado", mech, "Observatory: tornado simulation", "Brittle Hollow, southern observatory: the simulation of Giant's Deep tornadoes plays OW_GD_Tornado_v2 (27 s, low end 0.25) on two sources (UpTornado, DownTornado) that slide along a rail to the point nearest the player. The real tornadoes of Giant's Deep use the same clip and are not covered yet.")
				.Add("gain", "Strength", 1f, 0, 10);
			Define("hologram.shot", mech, "Hologram appearing and fading", "Hologram_Enter_v2_01 (7.4 s) and Hologram_Exit_v2_01 (4.1 s): a steady swell with real low end (0.2). HologramProjector plays them when a hologram is made or removed: Brittle Hollow's observatory (orb tracks), Giant's Deep, the orbital probe cannon.")
				.Add("gain", "Strength", 1.5f, 0, 10);
			Define("scroll.insert", mech, "Scroll into the wall", "OW_NM_InsertScroll, 1 s, peak 0.17 and almost nothing in the low band: the player slides a Nomai scroll into a wall socket (ScrollSocket). Every blip of the sound becomes a short tick.")
				.Add("gain", "Strength", 12f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("scroll.remove", mech, "Scroll out of the wall", "OW_NM_RemoveScroll, 1 s, peak 0.14, no low end: the scroll is pulled out of the socket.")
				.Add("gain", "Strength", 12f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 30f, 3, 200);
			Define("translator.text", mech, "Translator: reading Nomai text", "OW_PR_TranslatorTranslateNew, a 3 s loop, a steady 0.11 and nothing in the low band: the translator tool plays it on the player while a Nomai text is translated (also when it is translated automatically). Every blip of the sound becomes a short tick. While reading, the game freezes time; the suit triggers stay on.")
				.Add("gain", "Strength", 3f, 0, 30)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 40f, 3, 200);
			Define("nomai.textreveal", mech, "Nomai wall text appearing", "nomai_textbranchout_noenergy2, a 3.5 s loop, almost silent (0.01) and no low end: the text of Nomai walls and recordings revealing itself. Every blip becomes a short tick.")
				.Add("gain", "Strength", 10f, 0, 40)
				.Add("tone", "Tick tone Hz", 110f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 40f, 3, 200);
			Define("pool.loop", mech, "Projection pool: ambient", "OW_NM_SkypeLP, 76 s loop, low end 0.13-0.28 (the strongest part in the middle): the pool of a Nomai remote camera (NomaiRemoteCameraPlatform) hums while it is near.")
				.Add("gain", "Strength", 1f, 0, 10);
			Define("pool.shot", mech, "Projection pool: entering and leaving", "Pool_Enter_v3_Fade (4.5 s, low end up to 0.20) and Pool_Exit_v3 (2.4 s, up to 0.22): a swell when the player steps into the pool or out of it.")
				.Add("gain", "Strength", 2.5f, 0, 15);
			Define("hole.loop", mech, "Black and white holes: hum", "Warp_Loop_01_v2, 3.4 s loop, steady low end 0.21: the singularity of every black and white hole (Singularity_BlackHole_LP and Singularity_WhiteHole_LP share the clip).")
				.Add("gain", "Strength", 1f, 0, 10);
			Define("pedestal.slide", mech, "Pedestal sliding", "Slot_Linking_Stone_Loop_02, 2.2 s loop, low end only 0.03 (the sound is mostly mid): the Nomai pedestals (PedestalAnimator) move. The contact thud at the end is the door-stop clip, see Small door stopping.")
				.Add("gain", "Strength", 5f, 0, 15);
			Define("nomai.computer", mech, "Nomai computer hum", "OW_NM_ComputerAmbienceLP, 13.5 s loop, low end only 0.01-0.03: a very quiet hum.")
				.Add("gain", "Strength", 8f, 0, 20);
			Define("nomai.recorder", mech, "Nomai recorder", "BeaconIdea4, 38 s, no low end at all (0.00, overall 0.01): almost nothing to feel, the haptic is a hum made from the volume.")
				.Add("gain", "Strength", 8f, 0, 20);
			Define("dish.sun", mech, "Signal dish: Sun found", "All three rings point at the Sun and the player stands at the dish: OW_SUN_Ambience_v4, 106 s, low end 0.12 (the weakest of the three, so the default is higher). Counted only on the dish's own source.")
				.Add("gain", "Strength", 4f, 0, 15);
			Define("dish.giants", mech, "Signal dish: Giant's Deep found", "OW_GD_Tornado_v2 (the tornado), 27 s, low end 0.25.")
				.Add("gain", "Strength", 2f, 0, 15);
			Define("dish.hollow", mech, "Signal dish: Brittle Hollow found", "OW_NM_WHAmbience2_v2 (the white hole), 34 s, low end 0.31 (the strongest, so the default is lower). The Eye has no sound: only the rings turn.")
				.Add("gain", "Strength", 1.6f, 0, 15);

			const string uiShared = "Menus and ship log/Shared";
			Define("ui.live", uiShared, "Feel of interface sounds", "Almost all menu and ship log sounds are clicks and chimes with nothing in the low band, so each one becomes a short tick with the sound's own length and loudness. These are the defaults; every sound below can override its tone and tick length.")
				.Add("toneLow", "Tick tone, low Hz", 120f, 40, 300)
				.Add("toneHigh", "Tick tone, high Hz", 190f, 40, 300)
				.Add("rumbleGain", "Loudness to strength", 8f, 0.5f, 30)
				.Add("bandGain", "Low-band part gain", 3f, 0, 10)
				.Add("reactionHz", "Reaction speed Hz", 80f, 10, 200)
				.Add("releaseHz", "Tick decay speed Hz (lower = longer tick)", 15f, 3, 200)
				.Add("volumeCurve", "How much the game's quiet volume counts (1 = fully)", 0.25f, 0.05f, 1f);

			// One entry for every interface sound, in two groups.
			foreach (var sound in UiSounds.All)
			{
				string group = sound.ShipLog ? "Menus and ship log/Ship log sounds" : "Menus and ship log/Menu sounds";
				string hint = string.Format("{0}, {1:0.##} s, the game plays it at volume {2:0.##}.", sound.Clip, sound.Seconds, sound.Volume);
				string names = sound.Alias != AudioType.None ? sound.Type + " / " + sound.Alias : sound.Type.ToString();
				Define(sound.Id, group, names + " (" + sound.Clip + ")", hint)
					.Add("gain", "Strength", sound.DefaultGain, 0, 30)
					.Add("tone", "Tick tone Hz (0 = shared setting)", 0f, 0, 300)
					.Add("release", "Tick decay speed Hz (0 = shared setting)", 0f, 0, 200);
			}

			Define("ui.textreveal", "Menus and ship log/Ship log sounds", "Text appearing on the screen (ShipLogTextReveal_LP)", "OW_SP_ConsoleReadoutLP, a 3 s loop like typing. The game plays it while the text of an entry or a rumour is being revealed and stops it when the text is complete.")
				.Add("gain", "Strength", 3f, 0, 30)
				.Add("tone", "Tick tone Hz (0 = shared setting)", 0f, 0, 300)
				.Add("release", "Tick decay speed Hz (0 = shared setting)", 0f, 0, 200);

			const string light = "Lightbar";
			Define("light.settings", light, "Lightbar behaviour", "The colour follows the planet or place where the character is.")
				.Add("fadeSeconds", "Blend time between places (s)", 1f, 0, 5)
				.Add("brightness", "Brightness", 1f, 0, 1);
			Define("light.color", light, "Default (menu and open space)")
				.Add("r", "Red", 255, 0, 255, true)
				.Add("g", "Green", 110, 0, 255, true)
				.Add("b", "Blue", 20, 0, 255, true);
			Define("light.timber", light, "Timber Hearth")
				.Add("r", "Red", 6, 0, 255, true)
				.Add("g", "Green", 255, 0, 255, true)
				.Add("b", "Blue", 6, 0, 255, true);
			Define("light.attlerock", light, "Attlerock")
				.Add("r", "Red", 50, 0, 255, true)
				.Add("g", "Green", 54, 0, 255, true)
				.Add("b", "Blue", 65, 0, 255, true);
			Define("light.hollow", light, "Brittle Hollow")
				.Add("r", "Red", 132, 0, 255, true)
				.Add("g", "Green", 23, 0, 255, true)
				.Add("b", "Blue", 186, 0, 255, true);
			Define("light.lantern", light, "Hollow's Lantern")
				.Add("r", "Red", 255, 0, 255, true)
				.Add("g", "Green", 40, 0, 255, true)
				.Add("b", "Blue", 0, 0, 255, true);
			Define("light.deep", light, "Giant's Deep")
				.Add("r", "Red", 78, 0, 255, true)
				.Add("g", "Green", 83, 0, 255, true)
				.Add("b", "Blue", 48, 0, 255, true);
			Define("light.bramble", light, "Dark Bramble")
				.Add("r", "Red", 82, 0, 255, true)
				.Add("g", "Green", 255, 0, 255, true)
				.Add("b", "Blue", 169, 0, 255, true);
			Define("light.comet", light, "The Interloper")
				.Add("r", "Red", 0, 0, 255, true)
				.Add("g", "Green", 230, 0, 255, true)
				.Add("b", "Blue", 255, 0, 255, true);
			Define("light.twinA", light, "Ash Twin")
				.Add("r", "Red", 255, 0, 255, true)
				.Add("g", "Green", 175, 0, 255, true)
				.Add("b", "Blue", 40, 0, 255, true);
			Define("light.twinB", light, "Ember Twin")
				.Add("r", "Red", 237, 0, 255, true)
				.Add("g", "Green", 58, 0, 255, true)
				.Add("b", "Blue", 0, 0, 255, true);
			Define("light.quantum", light, "Quantum Moon")
				.Add("r", "Red", 117, 0, 255, true)
				.Add("g", "Green", 20, 0, 255, true)
				.Add("b", "Blue", 173, 0, 255, true);
			Define("light.whitehole", light, "White Hole Station")
				.Add("r", "Red", 230, 0, 255, true)
				.Add("g", "Green", 230, 0, 255, true)
				.Add("b", "Blue", 255, 0, 255, true);
			Define("light.cannon", light, "Orbital Probe Cannon")
				.Add("r", "Red", 243, 0, 255, true)
				.Add("g", "Green", 239, 0, 255, true)
				.Add("b", "Blue", 103, 0, 255, true);
			Define("light.sunstation", light, "Sun Station")
				.Add("r", "Red", 255, 0, 255, true)
				.Add("g", "Green", 210, 0, 255, true)
				.Add("b", "Blue", 60, 0, 255, true);
			Define("light.eye", light, "Eye of the Universe")
				.Add("r", "Red", 120, 0, 255, true)
				.Add("g", "Green", 80, 0, 255, true)
				.Add("b", "Blue", 255, 0, 255, true);

			Arrange();
		}

		/// <summary>
		/// Groups are paths ("World sounds/Campfire"). Puts every group's effects together, in the order the groups first
		/// appear, with a parent's own effects before its sub-groups, so the menu can draw them as a tree.
		/// </summary>
		private static void Arrange()
		{
			var first = new Dictionary<string, int>();
			for (int i = 0; i < All.Count; i++)
			{
				string path = "";
				foreach (var part in All[i].Group.Split('/'))
				{
					path = path.Length == 0 ? part : path + "/" + part;
					if (!first.ContainsKey(path)) first[path] = i;
				}
			}
			var order = new Dictionary<EffectDef, int>();
			for (int i = 0; i < All.Count; i++) order[All[i]] = i;
			All.Sort((a, b) =>
			{
				var pa = a.Group.Split('/');
				var pb = b.Group.Split('/');
				string path = "";
				for (int i = 0; i < Math.Min(pa.Length, pb.Length); i++)
				{
					path = path.Length == 0 ? pa[i] : path + "/" + pa[i];
					if (pa[i] != pb[i])
					{
						string other = path.Substring(0, path.Length - pa[i].Length) + pb[i];
						int c = first[path].CompareTo(first[other]);
						if (c != 0) return c;
					}
				}
				if (pa.Length != pb.Length) return pa.Length.CompareTo(pb.Length);
				return order[a].CompareTo(order[b]);
			});
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
			foreach (var def in All) if (def.Group == group || def.Group.StartsWith(group + "/")) Reset(def);
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
