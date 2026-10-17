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

		public static void Triggers(TriggerEffect left, TriggerEffect right, float seconds = 3f)
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

			var live = Effects.Get("hap.live");
			live.Preview = () => PlayHum(live);

			var rumble = Effects.Get("hap.rumble");
			rumble.Preview = () => _rumbleUntil = Time.unscaledTime + 1.2f;
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
