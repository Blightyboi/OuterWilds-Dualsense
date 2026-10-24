using HarmonyLib;
using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// The electric shock itself, from the game's event and not from the sound (the jellyfish shock clips have no low end).
	/// ElectricityVolume.ApplyShock runs when a detector that is not insulated is hit: on entering the volume and again every
	/// second while it stays. The player is also hurt there (the damage buzz already exists), and is kicked back at 10 m/s;
	/// the ship is not hurt but loses its electrics and its controls lock. So the player gets the blow of the kick, the ship
	/// (with the player inside) gets the blow and the electrical buzz.
	/// </summary>
	[HarmonyPatch(typeof(ElectricityVolume), "ApplyShock")]
	internal static class ShockHaptics
	{
		[HarmonyPostfix]
		private static void After(HazardDetector detector)
		{
			try
			{
				if (detector == null || !DualSenseMod.EnsureHaptics()) return;
				bool player = detector.CompareTag("PlayerDetector");
				bool ship = !player && detector.CompareTag("ShipDetector") && PlayerState.IsInsideShip();
				if (!player && !ship) return;
				Play(ship);
			}
			catch (System.Exception e)
			{
				DualSenseMod.Debug("Shock haptic failed: " + e.Message);
			}
		}

		public static void Play(bool shipBuzz)
		{
			var fx = Effects.Get("shock.hit");
			HapticSynth.Play(HapticSynth.Thump(fx["freq"], fx["ms"] / 1000f, fx["strength"], true));
			if (shipBuzz)
				HapticSynth.Play(HapticSynth.Buzz(fx["buzzFreq"], fx["buzzMs"] / 1000f, fx["buzzStrength"], fx["buzzChop"], 0.05f));
		}

		/// <summary>F1 menu: the blow alone, then the blow with the ship's buzz.</summary>
		public static void Preview()
		{
			Play(false);
			var fx = Effects.Get("shock.hit");
			HapticSynth.Play(HapticSynth.Thump(fx["freq"], fx["ms"] / 1000f, fx["strength"], true, 1.2f));
			HapticSynth.Play(HapticSynth.Buzz(fx["buzzFreq"], fx["buzzMs"] / 1000f, fx["buzzStrength"], fx["buzzChop"], 1.25f));
		}
	}
}
