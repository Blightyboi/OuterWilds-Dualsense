using System;
using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// Sits next to a game AudioSource and turns what it plays into haptics.
	/// Looping sources (engines): the clip is read from the game's data, converted once into a haptic
	/// waveform and looped, with its loudness following the source's volume.
	/// Other sources (one-shots): live copy through OnAudioFilterRead, band-limited.
	/// The game's own audio is never changed.
	/// </summary>
	public class HapticTap : MonoBehaviour
	{
		private const float LowPassHz = 350f;
		private const float HighPassHz = 25f;

		// Live tap only: filter data is not scaled by the source volume, and the game audio is quiet.
		private const float TapBoost = 8f;

		// Clip mode: how strongly the source volume drives the looped haptic (square-root curve).
		private const float LoopDrive = 2f;
		private const float LoopFloor = 0.5f;
		private const float LoopMinVolume = 0.01f;

		private OWAudioSource _owSource;
		private AudioSource _source;
		private float _forcedPan = float.NaN;

		private HapticSource _active;            // whatever is currently registered with the output
		private HapticLoopSource _loop;          // clip mode
		private HapticStream _tapStream;         // live tap mode
		private AudioClip _loopClip;
		private bool _loopOn;

		// Set on the main thread, read on the audio thread.
		private volatile float _volume;
		private float _lpCoeff, _hpCoeff;

		// Filter state (audio thread only).
		private float _lp1, _lp2, _hpState;
		private float[] _block = new float[4096];

		private float _peakOut, _logTimer;

		/// <summary>Set once an extracted one-shot has played through this source, so the live copy stays quiet.</summary>
		private volatile bool _suppressLiveTap;

		/// <summary>Adds a tap to the source's GameObject (once). forcedPan: -1 left, +1 right, NaN = follow the source's pan.</summary>
		public static void Attach(OWAudioSource source, float forcedPan)
		{
			if (source == null || source.GetComponent<HapticTap>() != null) return;
			var tap = source.gameObject.AddComponent<HapticTap>();
			tap._owSource = source;
			tap._source = source.GetAudioSource();
			tap._forcedPan = forcedPan;
		}

		/// <summary>Plays an extracted one-shot haptic for a PlayOneShot on this source.</summary>
		public void PlayOneShot(float[] wave, int rate, float volume)
		{
			var haptics = DualSenseMod.Haptics;
			if (haptics == null || wave == null || _source == null) return;

			float pan = float.IsNaN(_forcedPan) ? _source.panStereo : _forcedPan;
			float angle = (Mathf.Clamp(pan, -1f, 1f) + 1f) * 0.25f * Mathf.PI;
			var shot = new HapticOneShotSource(wave, rate, Mathf.Clamp01(volume * 1.2f));
			shot.GainLeft = Mathf.Cos(angle);
			shot.GainRight = Mathf.Sin(angle);
			haptics.AddSource(shot);
			_suppressLiveTap = true;
			DualSenseMod.Log(string.Format("Haptic one-shot on '{0}' ({1} samples, volume {2:F2})", name, wave.Length, volume));
		}

		private void Start()
		{
			int rate = AudioSettings.outputSampleRate;
			_lpCoeff = 1f - Mathf.Exp(-2f * Mathf.PI * LowPassHz / rate);
			_hpCoeff = 1f - Mathf.Exp(-2f * Mathf.PI * HighPassHz / rate);
			if (DualSenseMod.Haptics == null) enabled = false;
		}

		private void OnDestroy()
		{
			Unregister();
		}

		private void Unregister()
		{
			var haptics = DualSenseMod.Haptics;
			if (haptics != null && _active != null) haptics.RemoveSource(_active);
			_active = null;
			_loop = null;
			_tapStream = null;
		}

		/// <summary>Picks clip mode for looping sources with a readable clip, live tap otherwise.</summary>
		private void ChooseMode()
		{
			var haptics = DualSenseMod.Haptics;
			if (haptics == null) return;

			AudioClip clip = _source.clip;
			if (_source.loop && clip != null)
			{
				if (_loop != null && clip == _loopClip) return;
				Unregister();
				_loopClip = clip;
				float[] wave = HapticClipBank.Get(clip);
				if (wave != null)
				{
					_loop = haptics.AddSource(new HapticLoopSource(wave, clip.frequency));
					_active = _loop;
					return;
				}
			}

			if (_tapStream == null)
			{
				Unregister();
				_tapStream = haptics.AddSource(new HapticStream(AudioSettings.outputSampleRate));
				_active = _tapStream;
			}
		}

		private void Update()
		{
			if (_source == null || DualSenseMod.Haptics == null) return;
			ChooseMode();
			if (_active == null) return;

			_volume = _source.mute ? 0f : _source.volume;

			float pan = float.IsNaN(_forcedPan) ? _source.panStereo : _forcedPan;
			float angle = (Mathf.Clamp(pan, -1f, 1f) + 1f) * 0.25f * Mathf.PI; // equal-power pan
			_active.GainLeft = Mathf.Cos(angle);
			_active.GainRight = Mathf.Sin(angle);

			if (_loop != null)
			{
				// Quiet engine sounds (volume 0.03-0.15) must still be felt, so the hum has a floor
				// while the source is audible and grows with its volume above that.
				float gain = 0f;
				if (_source.isPlaying && _volume > LoopMinVolume)
					gain = Mathf.Clamp01(LoopFloor + Mathf.Sqrt(_volume) * LoopDrive * 0.6f);
				bool on = gain > 0.01f;
				if (on != _loopOn)
				{
					_loopOn = on;
					DualSenseMod.Log(string.Format("Haptic loop '{0}' ({1}) {2}, gain {3:F2}", name, _loopClip.name, on ? "on" : "off", gain));
				}
				_loop.TargetGain = gain;
			}

			var mod = DualSenseMod.Instance;
			if (mod != null && mod.DebugLog && _source.isPlaying)
			{
				_logTimer += Time.unscaledDeltaTime;
				if (_logTimer > 2f)
				{
					_logTimer = 0f;
					DualSenseMod.Debug(string.Format("tap {0}: mode={1} volume={2:F2} pan={3:F2} gain={4:F2} tapPeak={5:F3}",
						name, _loop != null ? "clip" : "live", _volume, pan, _loop != null ? _loop.TargetGain : 0f, _peakOut));
					_peakOut = 0f;
				}
			}
		}

		private void OnAudioFilterRead(float[] data, int channels)
		{
			var stream = _tapStream;
			if (stream == null || channels < 1 || _suppressLiveTap) return;

			int frames = data.Length / channels;
			if (_block.Length < frames) _block = new float[frames];

			float volume = Mathf.Sqrt(_volume) * TapBoost;
			for (int i = 0; i < frames; i++)
			{
				float mono = 0f;
				int baseIndex = i * channels;
				for (int c = 0; c < channels; c++) mono += data[baseIndex + c];
				mono /= channels;

				_lp1 += _lpCoeff * (mono - _lp1);
				_lp2 += _lpCoeff * (_lp1 - _lp2);
				_hpState += _hpCoeff * (_lp2 - _hpState);
				float shaped = (_lp2 - _hpState) * volume;

				_block[i] = shaped;
				float b = Math.Abs(shaped); if (b > _peakOut) _peakOut = b;
			}
			stream.Write(_block, frames);
		}
	}
}
