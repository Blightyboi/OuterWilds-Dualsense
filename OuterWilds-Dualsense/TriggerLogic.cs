using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// Our own adaptive trigger effects for the jetpack and the ship. They are the base layer:
	/// anything the game itself asks for (ship ignition, turbulence) is applied on top.
	/// R2 is "thrust up", L2 is "thrust down" on a gamepad.
	/// </summary>
	internal static class TriggerLogic
	{
		private static PlayerResources _playerResources;
		private static JetpackThrusterModel _jetpack;
		private static ShipThrusterModel _shipThrusters;

		public static void Compute(float strength, out TriggerEffect left, out TriggerEffect right)
		{
			left = TriggerEffect.Off;
			right = TriggerEffect.Off;

			if (PlayerState.IsDead() || PlayerState.InMapView() || PlayerState.UsingShipComputer()) return;

			// At the flight console the triggers fly the ship. Anywhere else with the suit on (also inside the
			// cabin) the jetpack is what R2 and L2 control.
			if (PlayerState.AtFlightConsole())
				ShipEffects(strength, ref left, ref right);
			else if (PlayerState.IsWearingSuit())
				JetpackEffects(strength, ref left, ref right);
		}

		private static void JetpackEffects(float strength, ref TriggerEffect left, ref TriggerEffect right)
		{
			if (_playerResources == null || _jetpack == null)
			{
				var player = Locator.GetPlayerTransform();
				if (player == null) return;
				_playerResources = player.GetComponent<PlayerResources>();
				_jetpack = player.GetComponent<JetpackThrusterModel>();
				if (_playerResources == null || _jetpack == null) return;
			}

			bool usable = _playerResources.IsJetpackUsable();
			float fuel = _playerResources.GetFuelFraction();
			if (!usable) return; // out of fuel and oxygen: the triggers go limp

			bool thrustingUp = _jetpack.GetLocalAcceleration().y > 0.1f;

			// Boost: slow strong pulses that push the finger back out. Normal flight: the spring that stiffens with
			// travel. Low fuel makes the trigger buzz as a warning.
			var boost = Effects.Get("jp.boost");
			var lowFuel = Effects.Get("jp.lowfuel");
			var rest = Effects.Get("jp.rest");
			var down = Effects.Get("jp.down");

			if (_jetpack.IsBoosterFiring())
				right = TriggerEffect.Vibration(boost.Int("zone"), boost.Int("freq"), Scale(boost.Int("amp"), strength));
			else if (fuel * 100f < lowFuel["threshold"] && thrustingUp)
				right = TriggerEffect.Vibration(lowFuel.Int("zone"), lowFuel.Int("freq"), Scale(lowFuel.Int("amp"), strength));
			else
				right = TriggerEffect.ResistanceSlope(Scale(rest.Int("start"), strength), Scale(rest.Int("end"), strength));

			left = TriggerEffect.Resistance(down.Int("zone"), Scale(down.Int("strength"), strength));
		}

		// The ship has no real fuel system (the tank only drains when it leaks), so the
		// triggers depend only on how hard the vertical thrusters are working.
		private static void ShipEffects(float strength, ref TriggerEffect left, ref TriggerEffect right)
		{
			if (_shipThrusters == null)
			{
				var ship = Locator.GetShipTransform();
				if (ship == null) return;
				_shipThrusters = ship.GetComponent<ShipThrusterModel>();
				if (_shipThrusters == null) return;
			}

			float maxThrust = _shipThrusters.GetMaxTranslationalThrust();
			float up = Mathf.Clamp01(_shipThrusters.GetLocalAcceleration().y / maxThrust);
			float down = Mathf.Clamp01(-_shipThrusters.GetLocalAcceleration().y / maxThrust);

			right = ShipTrigger(up, strength);
			left = ShipTrigger(down, strength);
		}

		/// <summary>Resting resistance; while thrusting, vibration that grows with thrust.</summary>
		private static TriggerEffect ShipTrigger(float thrust, float strength)
		{
			if (thrust > 0.05f)
			{
				var fx = Effects.Get("ship.thrust");
				int freq = Mathf.RoundToInt(Mathf.Lerp(fx["freqMin"], fx["freqMax"], thrust));
				int amp = Mathf.RoundToInt(Mathf.Lerp(fx["ampMin"], fx["ampMax"], thrust));
				return TriggerEffect.Vibration(fx.Int("zone"), freq, Scale(amp, strength));
			}
			var rest = Effects.Get("ship.rest");
			return TriggerEffect.Resistance(rest.Int("zone"), Scale(rest.Int("strength"), strength));
		}

		private static int Scale(int value, float strength)
		{
			return Mathf.Clamp(Mathf.RoundToInt(value * strength), 1, 8);
		}
	}
}
