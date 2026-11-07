using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// Lightbar colour by where the player is. The place is the game's own notion of it: the sector the player is
	/// in (Sector.Name). In menus and in open space the default colour (orange) is shown. Colours blend when moving
	/// from one place to another.
	/// </summary>
	internal static class PlanetLight
	{
		private struct Place
		{
			public Sector.Name Sector;
			public string Id;
		}

		// Most specific first: sectors are nested (a moon lies inside a bigger sector), the first match wins.
		private static readonly Place[] Places =
		{
			new Place { Sector = Sector.Name.EyeOfTheUniverse, Id = "light.eye" },
			new Place { Sector = Sector.Name.DreamWorld, Id = "light.dream" },          // the simulation inside the Stranger (DLC)
			new Place { Sector = Sector.Name.InvisiblePlanet, Id = "light.ringworld" }, // the Stranger itself (DLC)
			new Place { Sector = Sector.Name.VesselDimension, Id = "light.bramble" },   // the Vessel has no colour of its own: it lies in Dark Bramble
			new Place { Sector = Sector.Name.Vessel, Id = "light.bramble" },
			new Place { Sector = Sector.Name.BrambleDimension, Id = "light.bramble" },
			new Place { Sector = Sector.Name.DarkBramble, Id = "light.bramble" },
			new Place { Sector = Sector.Name.QuantumMoon, Id = "light.quantum" },
			new Place { Sector = Sector.Name.VolcanicMoon, Id = "light.lantern" },
			new Place { Sector = Sector.Name.TimberMoon, Id = "light.attlerock" },
			new Place { Sector = Sector.Name.BrittleHollow, Id = "light.hollow" },
			new Place { Sector = Sector.Name.GiantsDeep, Id = "light.deep" },
			new Place { Sector = Sector.Name.TimberHearth, Id = "light.timber" },
			new Place { Sector = Sector.Name.HourglassTwin_A, Id = "light.twinA" },
			new Place { Sector = Sector.Name.HourglassTwin_B, Id = "light.twinB" },
			new Place { Sector = Sector.Name.Comet, Id = "light.comet" },
			new Place { Sector = Sector.Name.WhiteHole, Id = "light.whitehole" },
			new Place { Sector = Sector.Name.OrbitalProbeCannon, Id = "light.cannon" },
			new Place { Sector = Sector.Name.SunStation, Id = "light.sunstation" },
		};

		private static float _r = 255f, _g = 110f, _b = 20f;
		private static float _nextCheck;

		// While the pointer of the Quantum Moon's locator (the Nomai tower on the moon) moves to the new place, the lightbar jumps
		// between the colours of the moon's places in a random order, with no blending, exactly as long as the pointer moves,
		// and then shows the colour of the new place at once.
		private static readonly string[] RunIds = { "light.twinB", "light.timber", "light.hollow", "light.deep", "light.bramble", "light.eye" };
		private static float _pointerAt = -100f, _testUntil, _stepUntil, _snapUntil;
		private static string _runId;
		private static bool _wasRunning;
		private static string _currentId = "light.color";
		private static string _previewId;
		private static float _previewUntil;

		/// <summary>Shows a place's colour for a few seconds (the Play button of the F1 menu).</summary>
		public static void Show(string id, float seconds = 0.4f)
		{
			_previewId = id;
			_previewUntil = Time.unscaledTime + seconds;
		}

		/// <summary>The locator's pointer is moving (called every frame it does).</summary>
		public static void PointerMoving()
		{
			_pointerAt = Time.unscaledTime;
		}

		/// <summary>F1 menu: a run of colours as long as a short pointer movement.</summary>
		public static void TestRun()
		{
			_testUntil = Time.unscaledTime + 1.8f;
		}

		/// <summary>The next colour of the run: random, never the one just shown.</summary>
		private static string NextRunId()
		{
			string id;
			do { id = RunIds[Random.Range(0, RunIds.Length)]; } while (id == _runId && RunIds.Length > 1);
			return id;
		}

		public static void StopShow()
		{
			_previewUntil = 0f;
		}

		/// <summary>Which colour setting applies at the moment.</summary>
		private static string FindPlaceId()
		{
			var detector = Locator.GetPlayerSectorDetector();
			if (detector == null) return "light.color";
			foreach (var place in Places)
				if (detector.IsWithinSector(place.Sector) && !LeftGravity(place.Sector)) return place.Id == "light.quantum" ? QuantumPlaceId() : place.Id;
			return "light.color";
		}

		// The sector of a planet reaches far beyond it (1500 m and more), so the colour stayed long after the character had left the
		// planet. The game's own sign that the character is no longer in a planet's gravity is the force alignment: the character
		// is turned to the planet while its field reaches them and floats freely (PlayerState.InZeroG) after that.
		private static readonly System.Collections.Generic.Dictionary<Sector.Name, AstroObject.Name> Bodies = new System.Collections.Generic.Dictionary<Sector.Name, AstroObject.Name>
		{
			{ Sector.Name.TimberHearth, AstroObject.Name.TimberHearth },
			{ Sector.Name.BrittleHollow, AstroObject.Name.BrittleHollow },
			{ Sector.Name.GiantsDeep, AstroObject.Name.GiantsDeep },
			{ Sector.Name.TimberMoon, AstroObject.Name.TimberMoon },
			{ Sector.Name.VolcanicMoon, AstroObject.Name.VolcanicMoon },
			{ Sector.Name.HourglassTwin_A, AstroObject.Name.TowerTwin },
			{ Sector.Name.HourglassTwin_B, AstroObject.Name.CaveTwin },
			{ Sector.Name.Comet, AstroObject.Name.Comet },
		};
		// Giant's Deep has an atmosphere: its cloud layer (CloudLayer_Fluid, a sphere of 950 m in the scene, a little above the 900 m
		// alignment radius) is where the planet ends for the colour, with the storms inside it, and there is no zero gravity above the
		// water itself. The other bodies have no atmosphere of that kind and keep the radius of their gravity volume.
		private static readonly System.Collections.Generic.Dictionary<Sector.Name, float> AtmosphereRadius = new System.Collections.Generic.Dictionary<Sector.Name, float>
		{
			{ Sector.Name.GiantsDeep, 950f },
		};
		private static readonly System.Collections.Generic.Dictionary<Sector.Name, float> SurfaceRadius = new System.Collections.Generic.Dictionary<Sector.Name, float>();

		/// <summary>
		/// True when the character is in the sector of this planet but the planet has let go of them. On foot or on the jetpack
		/// that is the game's own sign: they float in zero gravity (PlayerState.InZeroG) above the surface; zero-gravity places inside
		/// the planet (caves) and the ground itself (a margin of 40 m) do not count. In the ship the player's alignment is not
		/// updated, so the same boundary is taken from the planet itself: the alignment radius of its gravity volume, the distance
		/// from the centre at which the game stops turning bodies to the planet (375 m at Timber Hearth).
		/// </summary>
		private static bool LeftGravity(Sector.Name sector)
		{
			AstroObject.Name body;
			if (!Bodies.TryGetValue(sector, out body)) return false;
			try
			{
				var astro = Locator.GetAstroObject(body);
				var player = Locator.GetPlayerBody();
				var gravity = astro != null ? astro.GetGravityVolume() : null;
				if (gravity == null || player == null) return false;
				float atmosphere;
				if (!AtmosphereRadius.TryGetValue(sector, out atmosphere)) atmosphere = 0f;
				if (PlayerState.IsInsideShip() || PlayerState.IsAttached())
					return (player.GetWorldCenterOfMass() - gravity.GetCenterPosition()).magnitude > Mathf.Max(gravity.GetAlignmentRadius(), atmosphere);
				if (!PlayerState.InZeroG()) return false;
				float surface;
				if (!SurfaceRadius.TryGetValue(sector, out surface))
				{
					surface = HarmonyLib.Traverse.Create(gravity).Field("_upperSurfaceRadius").GetValue<float>();
					SurfaceRadius[sector] = surface;
				}
				return (player.GetWorldCenterOfMass() - gravity.GetCenterPosition()).magnitude > Mathf.Max(surface + 40f, atmosphere);
			}
			catch (System.Exception)
			{
				return false;
			}
		}

		/// <summary>
		/// The colour setting of a state of the Quantum Moon: the planet it orbits then (0 Ember Twin, 1 Timber Hearth,
		/// 2 Brittle Hollow, 3 Giant's Deep, 4 Dark Bramble, as the QuantumOrbit components of the scene say) or the Eye of the
		/// Universe (5). The symbols of the locator in the moon's tower are in the same order. Null for an unknown state.
		/// </summary>
		private static string PlaceIdForState(int state)
		{
			switch (state)
			{
				case 0: return "light.twinB";
				case 1: return "light.timber";
				case 2: return "light.hollow";
				case 3: return "light.deep";
				case 4: return "light.bramble";
				case 5: return "light.eye";
			}
			return null;
		}

		/// <summary>The Quantum Moon takes the colour of the place it is at now; its own colour stays for an unknown state.</summary>
		private static string QuantumPlaceId()
		{
			try
			{
				var moon = Locator.GetQuantumMoon();
				if (moon != null) return PlaceIdForState(moon.GetStateIndex()) ?? "light.quantum";
			}
			catch (System.Exception)
			{
				// The moon is not there yet: its own colour.
			}
			return "light.quantum";
		}

		/// <summary>Moves the lightbar colour toward the colour of the current place and writes it to the device.</summary>
		public static void Update(DualSenseDevice device, float deltaTime)
		{
			if (Time.unscaledTime >= _nextCheck)
			{
				_nextCheck = Time.unscaledTime + 0.1f;
				_currentId = FindPlaceId();
			}

			float now = Time.unscaledTime;
			bool flashing = now - _pointerAt < 0.15f || now < _testUntil;
			if (flashing)
			{
				if (now >= _stepUntil)
				{
					_runId = NextRunId();
					_stepUntil = now + Mathf.Max(Effects.Get("lightflash.quantum")["step"], 0.03f);
				}
			}
			else if (_wasRunning)
			{
				_snapUntil = now + 0.3f; // the run is over: the new place at once, not a blend from the last colour of the run
			}
			_wasRunning = flashing;
			string id = flashing ? _runId : now < _previewUntil ? _previewId : _currentId;
			var target = Effects.Get(id);
			var fade = Effects.Get("light.settings");
			float speed = fade["fadeSeconds"] > 0.01f ? deltaTime / fade["fadeSeconds"] : 1f;
			if (flashing || now < _snapUntil) speed = 1f; // jump to every colour of the run, a blend would smear them
			_r = Mathf.MoveTowards(_r, target["r"], speed * 255f);
			_g = Mathf.MoveTowards(_g, target["g"], speed * 255f);
			_b = Mathf.MoveTowards(_b, target["b"], speed * 255f);

			float k = fade["brightness"];
			device.SetLightbar(ToByte(_r * k), ToByte(_g * k), ToByte(_b * k));
		}

		private static byte ToByte(float v)
		{
			return (byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 255);
		}
	}
}

namespace OuterWildsDualSense
{
	/// <summary>
	/// The locator of the Quantum Moon (the Nomai tower on the moon, a pointer on an arc of six symbols) moves its pointer to the
	/// symbol of the new place at 90 degrees a second. While it moves, the lightbar runs through the colours of the places.
	/// </summary>
	[HarmonyLib.HarmonyPatch(typeof(QuantumMoonCompass), "Update")]
	internal static class QuantumLocatorPointerPatch
	{
		// Last pointer angle per compass (the game has two of them: the moon's locator and the round one of the Brittle Hollow tower).
		private static readonly System.Collections.Generic.Dictionary<int, float> Last = new System.Collections.Generic.Dictionary<int, float>();

		private static void Postfix(QuantumMoonCompass __instance)
		{
			try
			{
				var traverse = HarmonyLib.Traverse.Create(__instance);
				float degrees = traverse.Field("_degrees").GetValue<float>();
				float previous;
				bool moved = Last.TryGetValue(__instance.GetInstanceID(), out previous) && Mathf.Abs(degrees - previous) > 1e-4f;
				Last[__instance.GetInstanceID()] = degrees;
				if (!moved) return;

				// Only the locator on the moon itself, and only for a player who is inside its tower (the shrine): the pointer
				// also moves when the moon changes its place behind the player's back, and that is not for the lightbar.
				if (traverse.Field("_loop").GetValue<bool>()) return; // the round compass of the Brittle Hollow tower is another one
				var moon = Locator.GetQuantumMoon();
				if (moon == null || !moon.IsPlayerInsideShrine()) return;

				PlanetLight.PointerMoving();
			}
			catch (System.Exception e)
			{
				DualSenseMod.Debug("Quantum locator colour failed: " + e.Message);
			}
		}
	}
}
