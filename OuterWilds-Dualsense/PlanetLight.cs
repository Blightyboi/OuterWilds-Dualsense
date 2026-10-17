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
		private static string _currentId = "light.color";
		private static string _previewId;
		private static float _previewUntil;

		/// <summary>Shows a place's colour for a few seconds (the Play button of the F1 menu).</summary>
		public static void Show(string id, float seconds = 0.4f)
		{
			_previewId = id;
			_previewUntil = Time.unscaledTime + seconds;
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
				if (detector.IsWithinSector(place.Sector)) return place.Id;
			return "light.color";
		}

		/// <summary>Moves the lightbar colour toward the colour of the current place and writes it to the device.</summary>
		public static void Update(DualSenseDevice device, float deltaTime)
		{
			if (Time.unscaledTime >= _nextCheck)
			{
				_nextCheck = Time.unscaledTime + 0.25f;
				_currentId = FindPlaceId();
			}

			var target = Effects.Get(Time.unscaledTime < _previewUntil ? _previewId : _currentId);
			var fade = Effects.Get("light.settings");
			float speed = fade["fadeSeconds"] > 0.01f ? deltaTime / fade["fadeSeconds"] : 1f;
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
