using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// What the "Play" buttons of the F1 menu do. Trigger previews override everything else on the triggers
	/// for a few seconds, haptic previews run through the same output as the real effects.
	/// </summary>
	internal static class EffectPreview
	{
		private static TriggerEffect _left = TriggerEffect.Off, _right = TriggerEffect.Off;
		private static float _triggerUntil;
		private static float _rumbleUntil;

		public static bool TriggersActive { get { return Time.unscaledTime < _triggerUntil; } }
		public static bool RumbleActive { get { return Time.unscaledTime < _rumbleUntil; } }

		public static void GetTriggers(out TriggerEffect left, out TriggerEffect right)
		{
			left = _left;
			right = _right;
		}

		public static void Triggers(TriggerEffect left, TriggerEffect right, float seconds = 0.4f)
		{
			_left = left;
			_right = right;
			_triggerUntil = Time.unscaledTime + seconds;
		}

		public static void StopTriggers()
		{
			_triggerUntil = 0f;
		}

		/// <summary>Attaches the Play buttons to the effect definitions.</summary>
		public static void Wire()
		{
			var rest = Effects.Get("jp.rest");
			rest.Preview = () => Triggers(TriggerEffect.Off, TriggerEffect.ResistanceSlope(rest.Int("start"), rest.Int("end")));

			var boost = Effects.Get("jp.boost");
			boost.Preview = () => Triggers(TriggerEffect.Off, TriggerEffect.Vibration(boost.Int("zone"), boost.Int("freq"), boost.Int("amp")));

			var low = Effects.Get("jp.lowfuel");
			low.Preview = () => Triggers(TriggerEffect.Off, TriggerEffect.Vibration(low.Int("zone"), low.Int("freq"), low.Int("amp")));

			var down = Effects.Get("jp.down");
			down.Preview = () => Triggers(TriggerEffect.Resistance(down.Int("zone"), down.Int("strength")), TriggerEffect.Off);

			var shipRest = Effects.Get("ship.rest");
			shipRest.Preview = () => Triggers(
				TriggerEffect.Resistance(shipRest.Int("zone"), shipRest.Int("strength")),
				TriggerEffect.Resistance(shipRest.Int("zone"), shipRest.Int("strength")));

			var thrust = Effects.Get("ship.thrust");
			thrust.Preview = () =>
			{
				// Low thrust on L2, full thrust on R2, so both ends of the range can be compared.
				var lowFx = TriggerEffect.Vibration(thrust.Int("zone"), thrust.Int("freqMin"), thrust.Int("ampMin"));
				var fullFx = TriggerEffect.Vibration(thrust.Int("zone"), thrust.Int("freqMax"), thrust.Int("ampMax"));
				Triggers(lowFx, fullFx);
			};

			var windFx = Effects.Get("tornado.triggers");
			windFx.Preview = () =>
			{
				var held = TriggerEffect.Resistance(windFx.Int("zone"), windFx.Int("strength"));
				Triggers(held, held);
			};

			var live = Effects.Get("hap.live");
			live.Preview = () => PlayHum(live);

			Effects.Get("step.base").Preview = FootstepHaptics.PreviewGait;
			Effects.Get("step.surface").Preview = FootstepHaptics.PreviewSurfaces;

			Effects.Get("char.jump").Preview = () => PlayerHaptics.Jump();
			Effects.Get("char.landing").Preview = PlayerHaptics.PreviewLanding;
			Effects.Get("char.hit").Preview = PlayerHaptics.PreviewHit;
			Effects.Get("char.damage").Preview = PlayerHaptics.PreviewDamage;

			Effects.Get("death.sand").Preview = () => DeathHaptics.PlaySound(AudioType.Death_Crushed);
			Effects.Get("death.elevator").Preview = () => DeathHaptics.PlaySound(AudioType.Death_CrushedByElevator);
			Effects.Get("death.instant").Preview = () => DeathHaptics.PlaySound(AudioType.Death_Instant);
			Effects.Get("death.energy").Preview = () => DeathHaptics.PlaySound(AudioType.Death_Energy);
			Effects.Get("death.bigbang").Preview = () => DeathHaptics.PlaySound(AudioType.Death_BigBang);
			Effects.Get("death.digestion").Preview = () => DeathHaptics.PlaySound(AudioType.Death_Digestion);
			Effects.Get("death.lava").Preview = () => DeathHaptics.PlaySound(AudioType.Death_Lava);
			Effects.Get("death.timeloop").Preview = () => DeathHaptics.PlaySound(AudioType.Death_TimeLoop);
			Effects.Get("death.helmet").Preview = () => DeathHaptics.PlaySound(AudioType.PlayerSuitHelmetCrack, DeathHaptics.Where.Helmet);
			Effects.Get("death.groan").Preview = () => DeathHaptics.PlaySound(AudioType.None, DeathHaptics.Where.Groan);
			Effects.Get("death.shipexplosion").Preview = () => DeathHaptics.PlaySound(AudioType.ShipDamageShipExplosion, DeathHaptics.Where.ShipExplosion);

			foreach (var def in Effects.All)
			{
				if (!def.Id.StartsWith("light.") || def.Id == "light.settings") continue;
				string id = def.Id;
				def.Preview = () => PlanetLight.Show(id);
			}

			Effects.Get("hazard.ghost").Preview = () => HazardHaptics.Preview(HazardVolume.HazardType.DARKMATTER);
			Effects.Get("hazard.fire").Preview = () => HazardHaptics.Preview(HazardVolume.HazardType.FIRE);
			Effects.Get("hazard.heat").Preview = () => HazardHaptics.Preview(HazardVolume.HazardType.HEAT);

			Effects.Get("splash.ghost").Preview = () => WorldSounds.Preview(AudioType.Splash_GhostMatter);
			Effects.Get("splash.lava").Preview = () => WorldSounds.Preview(AudioType.Splash_Lava);
			Effects.Get("splash.water").Preview = () => WorldSounds.Preview(AudioType.Splash_Water_Player);

			Effects.Get("mem.base").Preview = () => MemoryHaptics.HoldLoop(false, AudioType.Flashback_Base_LP);
			Effects.Get("mem.overlay").Preview = () => MemoryHaptics.HoldLoop(true, AudioType.MemoryUplink_Overlay_LP);
			Effects.Get("mem.slam").Preview = () => MemoryHaptics.PlayOneShot(AudioType.MemoryUplink_Start);
			Effects.Get("mem.end").Preview = () => MemoryHaptics.PlayOneShot(AudioType.Flashback_End);

			Effects.Get("grav.crystal").Preview = () => WorldSounds.HoldLoop("grav.crystal", AudioType.NomaiGravCrystalAmbient_LP);
			Effects.Get("grav.flicker").Preview = () => WorldSounds.HoldLoop("grav.flicker", AudioType.NomaiGravCrystalFlickerAmbient_LP);
			Effects.Get("grav.beam").Preview = () => WorldSounds.HoldLoop("grav.beam", AudioType.NomaiTractorBeamAmbient_LP);
			Effects.Get("grav.beamswitch").Preview = () => WorldSounds.Preview(AudioType.NomaiTractorBeamActivate);
			Effects.Get("grav.cannon").Preview = () => WorldSounds.HoldLoop("grav.cannon", AudioType.NomaiGravityCannonAmbient_LP);
			Effects.Get("fire.crackle").Preview = () => WorldSounds.HoldLoop("fire.crackle", AudioType.TH_Campfire_LP);
			Effects.Get("fire.sleep").Preview = () => WorldSounds.HoldLoop("fire.sleep", AudioType.TH_Campfire_LP, WorldSounds.SleepPreviewName);
			Effects.Get("fire.ignite").Preview = () => WorldSounds.Preview(AudioType.TH_Campfire_Ignite);
			Effects.Get("pick.hit").Preview = () => WorldSounds.Preview(AudioType.TH_PickaxeImpact);
			Effects.Get("lift.shot").Preview = () => WorldSounds.Preview(AudioType.TH_LiftActivate);
			Effects.Get("lift.loop").Preview = () => WorldSounds.HoldLoop("lift.loop", AudioType.TH_Lift_LP);
			Effects.Get("geyser.shot").Preview = () => WorldSounds.Preview(AudioType.TH_GeyserStart);
			Effects.Get("geyser.loop").Preview = () => WorldSounds.HoldLoop("geyser.loop", AudioType.TH_Geyser_LP);
			Effects.Get("water.wheel").Preview = () => WorldSounds.HoldLoop("water.wheel", AudioType.TH_WaterWheel_LP);
			Effects.Get("water.fall").Preview = () => WorldSounds.HoldLoop("water.fall", AudioType.TH_Waterfall_LP);
			Effects.Get("water.under").Preview = () => WorldSounds.HoldLoop("water.under", AudioType.TH_UnderwaterAmbience_LP);
			Effects.Get("orb.drag").Preview = () => WorldSounds.HoldLoop("orb.drag", AudioType.NomaiOrbDragging_LP);
			Effects.Get("orb.roll").Preview = () => WorldSounds.HoldLoop("orb.roll", AudioType.NomaiOrbRolling_LP);
			Effects.Get("orb.grab").Preview = () => WorldSounds.Preview(AudioType.NomaiOrbStartDrag);
			Effects.Get("orb.slot").Preview = () => WorldSounds.Preview(AudioType.NomaiOrbSlotActivated);
			Effects.Get("hologram.shot").Preview = () => WorldSounds.Preview(AudioType.NomaiHologramActivate);
			Effects.Get("tool.equip").Preview = () => WorldSounds.Preview(AudioType.ToolScopeEquip);
			Effects.Get("tool.unequip").Preview = () => WorldSounds.Preview(AudioType.ToolScopeUnequip);
			Effects.Get("tool.click").Preview = () => WorldSounds.Preview(AudioType.ToolScopeSwitchFreq);
			Effects.Get("scope.identify").Preview = () => WorldSounds.Preview(AudioType.ToolScopeIdentifySignal);
			Effects.Get("flashlight.switch").Preview = () => WorldSounds.Preview(AudioType.ToolFlashlightOn);
			Effects.Get("flashlight.flicker").Preview = () => WorldSounds.Preview(AudioType.ToolFlashlightFlicker);
			Effects.Get("probe.launch").Preview = () => WorldSounds.Preview(AudioType.ToolProbeLaunch);
			Effects.Get("probe.launch.water").Preview = () => WorldSounds.Preview(AudioType.ToolProbeLaunchUnderwater);
			Effects.Get("probe.photo").Preview = () => WorldSounds.Preview(AudioType.ToolProbeTakePhoto);
			Effects.Get("probe.retrieve").Preview = () => WorldSounds.Preview(AudioType.ToolProbeRetrieve);
			Effects.Get("probe.attach").Preview = () => WorldSounds.Preview(AudioType.ToolProbeAttach);
			Effects.Get("mallow.equip").Preview = () => WorldSounds.Preview(AudioType.ToolMarshmallowEquip);
			Effects.Get("mallow.replace").Preview = () => WorldSounds.Preview(AudioType.ToolMarshmallowReplace);
			Effects.Get("mallow.puff").Preview = () => WorldSounds.Preview(AudioType.ToolMarshmallowBlowOut);
			Effects.Get("mallow.eat").Preview = () => WorldSounds.Preview(AudioType.ToolMarshmallowEat);
			Effects.Get("mallow.ignite").Preview = () => WorldSounds.Preview(AudioType.ToolMarshmallowIgnite);
			Effects.Get("repair.done").Preview = () => WorldSounds.Preview(AudioType.ToolRepairComplete);
			Effects.Get("scope.static").Preview = () => WorldSounds.HoldLoop("scope.static", AudioType.ToolScopeStatic);
			Effects.Get("scope.zoom").Preview = () => WorldSounds.HoldLoop("scope.zoom", AudioType.ToolScopeZoomAdjust);
			Effects.Get("probe.flight").Preview = () => WorldSounds.HoldLoop("probe.flight", AudioType.ToolProbeFlight_LP);
			Effects.Get("repair.loop").Preview = () => WorldSounds.HoldLoop("repair.loop", AudioType.ToolRepairing_LP);
			Effects.Get("tool.equip").PreviewPeriod = 1.4f;
			Effects.Get("tool.unequip").PreviewPeriod = 1.4f;
			Effects.Get("tool.click").PreviewPeriod = 1.4f;
			Effects.Get("scope.identify").PreviewPeriod = 1.4f;
			Effects.Get("flashlight.switch").PreviewPeriod = 1.4f;
			Effects.Get("flashlight.flicker").PreviewPeriod = 1.4f;
			Effects.Get("probe.launch").PreviewPeriod = 3.2f;
			Effects.Get("probe.launch.water").PreviewPeriod = 3.2f;
			Effects.Get("probe.photo").PreviewPeriod = 1.4f;
			Effects.Get("probe.retrieve").PreviewPeriod = 3.2f;
			Effects.Get("probe.attach").PreviewPeriod = 1.4f;
			Effects.Get("mallow.equip").PreviewPeriod = 1.4f;
			Effects.Get("mallow.replace").PreviewPeriod = 1.4f;
			Effects.Get("mallow.puff").PreviewPeriod = 1.4f;
			Effects.Get("mallow.eat").PreviewPeriod = 3.2f;
			Effects.Get("mallow.ignite").PreviewPeriod = 1.4f;
			Effects.Get("repair.done").PreviewPeriod = 3.2f;
			Effects.Get("item.pickup").Preview = () => WorldSounds.Preview(AudioType.ToolItemScrollPickUp);
			Effects.Get("item.drop").Preview = () => WorldSounds.Preview(AudioType.ToolItemScrollDrop);
			Effects.Get("stone.insert").Preview = () => WorldSounds.Preview(AudioType.ToolItemSharedStoneInsert);
			Effects.Get("stone.remove").Preview = () => WorldSounds.Preview(AudioType.ToolItemSharedStoneRemove);
			Effects.Get("core.insert").Preview = () => WorldSounds.Preview(AudioType.ToolItemWarpCoreInsert);
			Effects.Get("core.remove").Preview = () => WorldSounds.Preview(AudioType.ToolItemWarpCoreRemove);
			Effects.Get("item.pickup").PreviewPeriod = 3.6f;
			Effects.Get("item.drop").PreviewPeriod = 3.6f;
			Effects.Get("stone.insert").PreviewPeriod = 1.6f;
			Effects.Get("stone.remove").PreviewPeriod = 1.6f;
			Effects.Get("core.insert").PreviewPeriod = 3.6f;
			Effects.Get("core.remove").PreviewPeriod = 3.6f;
			Effects.Get("scroll.insert").Preview = () => WorldSounds.Preview(AudioType.ToolItemScrollInsert);
			Effects.Get("scroll.remove").Preview = () => WorldSounds.Preview(AudioType.ToolItemScrollRemove);
			Effects.Get("translator.text").Preview = () => WorldSounds.HoldLoop("translator.text", AudioType.ToolTranslateText_LP);
			Effects.Get("nomai.textreveal").Preview = () => WorldSounds.HoldLoop("nomai.textreveal", AudioType.NomaiTextReveal_LP);
			Effects.Get("pool.loop").Preview = () => WorldSounds.HoldLoop("pool.loop", AudioType.NomaiRemoteCameraAmbient_LP);
			Effects.Get("pool.shot").Preview = () => WorldSounds.Preview(AudioType.NomaiRemoteCameraEntry);
			Effects.Get("hole.loop").Preview = () => WorldSounds.HoldLoop("hole.loop", AudioType.Singularity_BlackHole_LP);
			Effects.Get("pedestal.slide").Preview = () => WorldSounds.HoldLoop("pedestal.slide", AudioType.NomaiPedestalSlide_LP);
			Effects.Get("nomai.computer").Preview = () => WorldSounds.HoldLoop("nomai.computer", AudioType.NomaiComputerAmbient);
			Effects.Get("nomai.recorder").Preview = () => WorldSounds.HoldLoop("nomai.recorder", AudioType.NomaiRecorderAmbient_LP);
			Effects.Get("obs.tornado").Preview = () => WorldSounds.HoldLoop("obs.tornado", AudioType.GD_Tornado_LP, "Audio_Observatory preview");
			Effects.Get("tornado.roar").Preview = () => WorldSounds.HoldLoop("tornado.roar", AudioType.GD_Tornado_LP);
			Effects.Get("tornado.ride").Preview = () => TornadoHaptics.Preview();
			Effects.Get("island.lift").Preview = () => WorldSounds.Preview(AudioType.GD_IslandLiftedByTornado);
			Effects.Get("island.splash").Preview = () => WorldSounds.Preview(AudioType.GD_IslandSplash);
			Effects.Get("island.waves").Preview = () => WorldSounds.HoldLoop("island.waves", AudioType.GD_WavesBeach_LP);
			Effects.Get("gd.current").Preview = () => WorldSounds.HoldLoop("gd.current", AudioType.GD_UnderwaterCurrent_LP);
			Effects.Get("gd.ambience").Preview = () => WorldSounds.HoldLoop("gd.ambience", AudioType.GD_OceanSurface_LP);
			Effects.Get("gd.barrier").Preview = () => WorldSounds.HoldLoop("gd.barrier", AudioType.GD_ElectricBarrier_LP);
			Effects.Get("shock.hit").Preview = () => ShockHaptics.Preview();
			Effects.Get("bh.meteor").Preview = () => WorldSounds.Preview(AudioType.BH_MeteorImpact);
			Effects.Get("bh.fragment").Preview = () => WorldSounds.Preview(AudioType.BH_BreakawayFragment);
			Effects.Get("bh.hole.approach").Preview = () => WorldSounds.HoldLoop("bh.hole.approach", AudioType.BlackHoleAmbience_LP);
			Effects.Get("bh.hole.transit").Preview = () => WorldSounds.Preview(AudioType.SingularityOnPlayerEnterExit);
			Effects.Get("bh.hole.emission").Preview = () => WorldSounds.Preview(AudioType.BH_BlackHoleEmission);
			Effects.Get("bh.platform").Preview = () => WorldSounds.Preview(AudioType.BH_BreakawayPlatform);
			Effects.Get("bh.forge").Preview = () => WorldSounds.HoldLoop("bh.forge", AudioType.BH_ForgeMoving_LP);
			Effects.Get("bh.ambience").Preview = () => WorldSounds.HoldLoop("bh.ambience", AudioType.BH_SubsurfaceAmbience_LP);
			Effects.Get("suit.suit").Preview = () => WorldSounds.Preview(AudioType.PlayerSuitWearSuit);
			Effects.Get("suit.helmet").Preview = () => WorldSounds.Preview(AudioType.PlayerSuitRemoveHelmet);
			Effects.Get("suit.refuel").Preview = () => WorldSounds.Preview(AudioType.ShipCabinUseRefueller);
			Effects.Get("suit.medkit").Preview = () => WorldSounds.Preview(AudioType.ShipCabinUseMedkit);
			Effects.Get("suit.breath").Preview = () => WorldSounds.HoldLoop("suit.breath", AudioType.PlayerBreathing_LP);
			Effects.Get("suit.breath.low").Preview = () => WorldSounds.HoldLoop("suit.breath.low", AudioType.PlayerBreathing_LowOxygen_LP);
			Effects.Get("qm.shuttle").Preview = () => WorldSounds.Preview(AudioType.NomaiShuttleImpact);
			Effects.Get("qm.lightning").Preview = () => WorldSounds.Preview(AudioType.EyeLightning);
			Effects.Get("qm.cairn").Preview = () => WorldSounds.Preview(AudioType.SolanumCairnAssembly);
			Effects.Get("qm.symbol").Preview = () => WorldSounds.Preview(AudioType.SolanumSymbolReveal);
			Effects.Get("qm.eye").Preview = () => TornadoHaptics.PreviewEye();
			Effects.Get("door.small").Preview = () => WorldSounds.HoldLoop("door.small", AudioType.NomaiDoorSlide_LP);
			Effects.Get("door.small.start").Preview = () => WorldSounds.Preview(AudioType.NomaiDoorStart);
			Effects.Get("door.small.stop").Preview = () => WorldSounds.Preview(AudioType.NomaiDoorStop);
			Effects.Get("door.big").Preview = () => WorldSounds.HoldLoop("door.big", AudioType.NomaiDoorSlideBig_LP);
			Effects.Get("door.big.start").Preview = () => WorldSounds.Preview(AudioType.NomaiDoorStartBig);
			Effects.Get("door.big.stop").Preview = () => WorldSounds.Preview(AudioType.NomaiDoorStopBig);
			Effects.Get("mech.big").Preview = () => WorldSounds.HoldLoop("mech.big", AudioType.NomaiAirlockSlide_LP);
			Effects.Get("dish.sun").Preview = () => WorldSounds.HoldLoop("dish.sun", AudioType.Sun_Ambience_LP, "SignalDish preview");
			Effects.Get("dish.giants").Preview = () => WorldSounds.HoldLoop("dish.giants", AudioType.GD_Tornado_LP, "SignalDish preview");
			Effects.Get("dish.hollow").Preview = () => WorldSounds.HoldLoop("dish.hollow", AudioType.BlackHoleAmbience_LP, "SignalDish preview");
			Effects.Get("grav.shot").Preview = () => WorldSounds.Preview(AudioType.NomaiGravityCannonActivate);

			foreach (var sound in UiSounds.All)
			{
				var captured = sound;
				var def = Effects.Get(sound.Id);
				def.Preview = () => { if (captured.ShipLog) UiHaptics.PlayShipLog(captured.Type); else UiHaptics.PlayMenu(captured.Type); };
			}
			Effects.Get("ui.live").Preview = () => UiHaptics.PlayMenu(AudioType.Menu_UpDown);
			Effects.Get("ui.textreveal").Preview = UiHaptics.HoldTextReveal;
			Effects.Get("ui.textreveal").PreviewStop = UiHaptics.StopTextReveal;

			var rumble = Effects.Get("hap.rumble");
			rumble.Preview = () => _rumbleUntil = Time.unscaledTime + 0.4f;

			ConfigureLooping();
		}

		/// <summary>
		/// In the menu Play repeats the effect until Stop is pressed. Held effects (triggers, colours, rumble) are
		/// refreshed every frame and released on stop, one-shot effects are repeated after their own length.
		/// </summary>
		private static void ConfigureLooping()
		{
			foreach (var def in Effects.All)
			{
				if (def.Id.StartsWith("jp.") || def.Id.StartsWith("ship.") || def.Id == "tornado.triggers")
				{
					def.PreviewStop = StopTriggers;
				}
				else if (def.Id.StartsWith("light."))
				{
					def.PreviewStop = PlanetLight.StopShow;
				}
			}
			Effects.Get("hap.rumble").PreviewStop = () => _rumbleUntil = 0f;
			foreach (var id in new[] { "hazard.ghost", "hazard.fire", "hazard.heat" })
				Effects.Get(id).PreviewStop = HazardHaptics.StopPreview;

			Effects.Get("hap.live").PreviewPeriod = 2.2f;
			foreach (var sound in UiSounds.All)
				Effects.Get(sound.Id).PreviewPeriod = sound.Seconds + 0.35f;
			Effects.Get("ui.live").PreviewPeriod = 0.5f;
			Effects.Get("grav.shot").PreviewPeriod = 9.0f;
			Effects.Get("lift.shot").PreviewPeriod = 1.7f;
			Effects.Get("door.small.start").PreviewPeriod = 1.4f;
			Effects.Get("door.small.stop").PreviewPeriod = 2.6f;
			Effects.Get("door.big.start").PreviewPeriod = 2.1f;
			Effects.Get("door.big.stop").PreviewPeriod = 3.1f;
			Effects.Get("qm.shuttle").PreviewPeriod = 8.2f;
			Effects.Get("qm.lightning").PreviewPeriod = 6.3f;
			Effects.Get("qm.cairn").PreviewPeriod = 3.5f;
			Effects.Get("qm.symbol").PreviewPeriod = 7.2f;
			Effects.Get("qm.eye").PreviewPeriod = 2.3f;
			Effects.Get("suit.suit").PreviewPeriod = 3.0f;
			Effects.Get("suit.helmet").PreviewPeriod = 2.4f;
			Effects.Get("suit.refuel").PreviewPeriod = 2.3f;
			Effects.Get("suit.medkit").PreviewPeriod = 1.7f;
			Effects.Get("bh.platform").PreviewPeriod = 3.8f;
			Effects.Get("bh.hole.transit").PreviewPeriod = 1.8f;
			Effects.Get("bh.hole.emission").PreviewPeriod = 2.3f;
			Effects.Get("bh.meteor").PreviewPeriod = 4.2f;
			Effects.Get("bh.fragment").PreviewPeriod = 9.0f;
			Effects.Get("shock.hit").PreviewPeriod = 2.2f;
			Effects.Get("island.lift").PreviewPeriod = 3.2f;
			Effects.Get("island.splash").PreviewPeriod = 12.5f;
			Effects.Get("tornado.ride").PreviewPeriod = 2.3f;
			Effects.Get("hologram.shot").PreviewPeriod = 7.8f;
			Effects.Get("pool.shot").PreviewPeriod = 5.0f;
			Effects.Get("scroll.insert").PreviewPeriod = 1.4f;
			Effects.Get("scroll.remove").PreviewPeriod = 1.4f;
			Effects.Get("orb.grab").PreviewPeriod = 1.4f;
			Effects.Get("orb.slot").PreviewPeriod = 1.2f;
			Effects.Get("fire.ignite").PreviewPeriod = 2.2f;
			Effects.Get("pick.hit").PreviewPeriod = 0.7f;
			Effects.Get("geyser.shot").PreviewPeriod = 4.6f;
			Effects.Get("grav.beamswitch").PreviewPeriod = 7.2f;
			foreach (var id in new[] { "grav.crystal", "grav.flicker", "grav.cannon", "grav.beam", "fire.crackle", "fire.sleep", "lift.loop", "geyser.loop", "water.wheel", "water.fall", "water.under", "orb.drag", "orb.roll", "mech.big", "dish.sun", "dish.giants", "dish.hollow", "obs.tornado", "tornado.roar", "island.waves", "gd.current", "gd.ambience", "gd.barrier", "bh.hole.approach", "bh.forge", "bh.ambience", "suit.breath", "suit.breath.low", "door.small", "door.big", "pool.loop", "scope.static", "scope.zoom", "probe.flight", "repair.loop", "translator.text", "nomai.textreveal", "hole.loop", "pedestal.slide", "nomai.computer", "nomai.recorder" })
				Effects.Get(id).PreviewStop = WorldSounds.StopLoop;
			Effects.Get("mem.slam").PreviewPeriod = 6.0f;
			Effects.Get("mem.end").PreviewPeriod = 4.0f;
			Effects.Get("mem.base").PreviewStop = () => MemoryHaptics.StopLoop(false);
			Effects.Get("mem.overlay").PreviewStop = () => MemoryHaptics.StopLoop(true);
			Effects.Get("splash.ghost").PreviewPeriod = 2.4f;
			Effects.Get("splash.lava").PreviewPeriod = 3.2f;
			Effects.Get("splash.water").PreviewPeriod = 2.0f;
			Effects.Get("step.base").PreviewPeriod = 4.0f;
			Effects.Get("step.surface").PreviewPeriod = 3.2f;
			Effects.Get("char.jump").PreviewPeriod = 0.7f;
			Effects.Get("char.landing").PreviewPeriod = 2.0f;
			Effects.Get("char.hit").PreviewPeriod = 2.2f;
			Effects.Get("char.damage").PreviewPeriod = 2.2f;
			// Lengths of the clips (tools/analyze_clip.py) plus a short pause.
			Effects.Get("death.sand").PreviewPeriod = 6.2f;
			Effects.Get("death.elevator").PreviewPeriod = 1.7f;
			Effects.Get("death.instant").PreviewPeriod = 5.5f;
			Effects.Get("death.energy").PreviewPeriod = 3.6f;
			Effects.Get("death.digestion").PreviewPeriod = 5.4f;
			Effects.Get("death.lava").PreviewPeriod = 3.2f;
			Effects.Get("death.bigbang").PreviewPeriod = 5.3f;
			Effects.Get("death.timeloop").PreviewPeriod = 4.5f;
			Effects.Get("death.helmet").PreviewPeriod = 1.2f;
			Effects.Get("death.groan").PreviewPeriod = 5.5f;
			Effects.Get("death.shipexplosion").PreviewPeriod = 4.6f;
		}

		/// <summary>Rumble values for the preview: both motors at full level.</summary>
		public static void OverrideRumble(ref float hi, ref float low)
		{
			if (!RumbleActive) return;
			hi = 1f;
			low = 1f;
		}

		/// <summary>A two second hum made like the live engine haptic, to tune its tones and loudness.</summary>
		private static void PlayHum(EffectDef live)
		{
			var haptics = DualSenseMod.Haptics;
			if (haptics == null) return;

			const int rate = 48000;
			var wave = new float[rate * 2];
			float level = live["bandTarget"];
			for (int i = 0; i < wave.Length; i++)
			{
				double t = i / (double)rate;
				float fade = Mathf.Clamp01(Mathf.Min((float)t / 0.1f, (float)(2.0 - t) / 0.3f));
				wave[i] = (float)(0.65 * System.Math.Sin(2 * System.Math.PI * live["rumbleLow"] * t)
					+ 0.35 * System.Math.Sin(2 * System.Math.PI * live["rumbleHigh"] * t)) * level * fade;
			}
			haptics.AddSource(new HapticOneShotSource(wave, rate, 1f));
		}
	}
}
