using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>Short synthetic haptic waveforms for game events (steps, jumps, impacts, damage).</summary>
	internal static class HapticSynth
	{
		public const int Rate = 48000;

		/// <summary>A short decaying thump (with a second harmonic for body, and an optional tick for hard surfaces).</summary>
		public static float[] Thump(float freq, float duration, float amplitude, bool tick = false, float delaySeconds = 0f)
		{
			int lead = Mathf.RoundToInt(delaySeconds * Rate);
			int length = Mathf.Max(32, Mathf.RoundToInt(duration * Rate));
			var wave = new float[lead + length];
			float tau = duration / 3.5f;
			for (int i = 0; i < length; i++)
			{
				float t = i / (float)Rate;
				float env = (1f - Mathf.Exp(-t / 0.003f)) * Mathf.Exp(-t / tau);
				float v = Mathf.Sin(2f * Mathf.PI * freq * t) + 0.35f * Mathf.Sin(4f * Mathf.PI * freq * t);
				if (tick) v += 0.5f * Mathf.Sin(2f * Mathf.PI * freq * 3f * t) * Mathf.Exp(-t / 0.012f);
				wave[lead + i] = v * env * amplitude;
			}
			return wave;
		}

		/// <summary>A buzz: sustained tone with a fast attack and a linear release, optionally chopped at tremoloHz.</summary>
		public static float[] Buzz(float freq, float duration, float amplitude, float tremoloHz = 0f, float delaySeconds = 0f)
		{
			int lead = Mathf.RoundToInt(delaySeconds * Rate);
			int length = Mathf.Max(32, Mathf.RoundToInt(duration * Rate));
			var wave = new float[lead + length];
			for (int i = 0; i < length; i++)
			{
				float t = i / (float)Rate;
				float env = Mathf.Min(t / 0.005f, 1f) * (1f - t / duration);
				float chop = tremoloHz > 0f ? 0.5f + 0.5f * Mathf.Sign(Mathf.Sin(2f * Mathf.PI * tremoloHz * t)) : 1f;
				wave[lead + i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * chop * amplitude;
			}
			return wave;
		}

		/// <summary>
		/// A low rumble with a shaped loudness. shape &gt; 0 swells up (loudness = progress^shape), shape &lt; 0 dies away
		/// ((1 - progress)^|shape|). chopHz &gt; 0 makes it throb softly.
		/// </summary>
		public static float[] Rumble(float freq, float duration, float amplitude, float shape, float chopHz = 0f, float delaySeconds = 0f)
		{
			int lead = Mathf.RoundToInt(delaySeconds * Rate);
			int length = Mathf.Max(32, Mathf.RoundToInt(duration * Rate));
			var wave = new float[lead + length];
			for (int i = 0; i < length; i++)
			{
				float t = i / (float)Rate;
				float progress = t / duration;
				float env = shape >= 0f ? Mathf.Pow(progress, shape) : Mathf.Pow(1f - progress, -shape);
				env *= Mathf.Min(t / 0.01f, 1f);
				float chop = chopHz > 0f ? 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * chopHz * t) : 1f;
				float tone = Mathf.Sin(2f * Mathf.PI * freq * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * freq * 1.5f * t);
				wave[lead + i] = tone * env * chop * amplitude;
			}
			return wave;
		}

		/// <summary>Adds waveforms together (the result is as long as the longest one).</summary>
		public static float[] Mix(params float[][] waves)
		{
			int length = 0;
			foreach (var w in waves) length = Mathf.Max(length, w.Length);
			var mix = new float[length];
			foreach (var w in waves)
				for (int i = 0; i < w.Length; i++) mix[i] += w[i];
			return mix;
		}

		/// <summary>Plays a waveform on both actuators; pan -1 is the left one, +1 the right one.</summary>
		public static void Play(float[] wave, float pan = 0f)
		{
			var haptics = DualSenseMod.Haptics;
			var mod = DualSenseMod.Instance;
			if (haptics == null || mod == null || !mod.Enabled || !mod.AudioHaptics) return;

			float angle = (Mathf.Clamp(pan, -1f, 1f) + 1f) * 0.25f * Mathf.PI; // equal-power pan
			var shot = new HapticOneShotSource(wave, Rate, 1f);
			shot.GainLeft = Mathf.Cos(angle);
			shot.GainRight = Mathf.Sin(angle);
			haptics.AddSource(shot);
		}
	}
}
