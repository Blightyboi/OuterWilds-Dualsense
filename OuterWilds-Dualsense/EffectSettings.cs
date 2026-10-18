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
				.Add("gain", "Strength", 1.5f, 0, 8)
				.Add("tone", "Tick tone Hz", 120f, 40, 300)
				.Add("release", "Tick decay speed Hz (lower = longer tick)", 25f, 3, 200);
			Define("grav.shot", gravity, "Gravity cannon start and stop", "OW_NM_GravityCannonActivated / Deactivated, 8 s: build-up and a heavy blow at about 5.3 s.")
				.Add("gain", "Strength", 1.2f, 0, 6);

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
