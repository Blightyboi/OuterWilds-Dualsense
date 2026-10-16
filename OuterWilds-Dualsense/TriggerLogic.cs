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
		private const float LowFuelFraction = 0.2f;

		private static PlayerResources _playerResources;
		private static JetpackThrusterModel _jetpack;
		private static ShipThrusterModel _shipThrusters;

		public static void Compute(float strength, out TriggerEffect left, out TriggerEffect right)
		{
			left = TriggerEffect.Off;
			right = TriggerEffect.Off;

			if (PlayerState.IsDead() || PlayerState.InMapView() || PlayerState.UsingShipComputer()) return;

			if (PlayerState.IsInsideShip() || PlayerState.AtFlightConsole())
			{
				if (PlayerState.AtFlightConsole()) ShipEffects(strength, ref left, ref right);
			}
			else if (PlayerState.IsWearingSuit())
			{
				JetpackEffects(strength, ref left, ref right);
			}
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

			float accelY = _jetpack.GetLocalAcceleration().y;
			bool thrustingUp = accelY > 0.1f;

			if (_jetpack.IsBoosterFiring())
				right = TriggerEffect.Vibration(2, 30, Scale(4, strength));
			else if (fuel < LowFuelFraction && thrustingUp)
				right = TriggerEffect.Vibration(1, 10, Scale(3, strength)); // low fuel warning
			else
				right = TriggerEffect.ResistanceSlope(Scale(2, strength), Scale(5, strength));

			left = TriggerEffect.Resistance(0, Scale(2, strength));
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
				return TriggerEffect.Vibration(1, Mathf.RoundToInt(Mathf.Lerp(12f, 24f, thrust)), Scale(Mathf.RoundToInt(Mathf.Lerp(2f, 5f, thrust)), strength));
			return TriggerEffect.Resistance(1, Scale(3, strength));
		}

		private static int Scale(int value, float strength)
		{
			return Mathf.Clamp(Mathf.RoundToInt(value * strength), 1, 8);
		}
	}
}
