using System;
using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// Sits next to a game AudioSource and turns what it plays into haptics. The game's audio is never changed.
	///
	/// Live mode (default): the samples the source plays are copied through OnAudioFilterRead and turned into
	/// haptics on the fly. Needs no extra files.
	/// Extracted mode (config "useExtractedClips"): looping sources play a haptic waveform prepared by
	/// tools/extract_haptics.py instead; sources without such a file fall back to live mode.
	/// </summary>
	public class HapticTap : MonoBehaviour
	{
		private const float MinVolume = 0.01f;
		private const float Gate = 0.003f;          // below this broadband level the sound counts as silence
		private const float PeakDecayPerSecond = 0.97f;

		// Tunable through the F1 menu (effect "hap.live"), refreshed every frame on the main thread.
		private float _gainFloor = 0.15f;
		private float _startThrust = 0.05f;        // below this thrust (0-1) there is no haptic
		private float _curve = 1f;
		private float _referenceVolume = 0.25f;     // volume counted as full thrust
		private float _bandTarget = 0.45f;          // level of the normalised low-frequency part

		/// <summary>
		/// Real thrust (0-1) of the thruster that makes this sound, read from the game's thruster model. When set, the
		/// haptic strength follows it instead of the source volume, so it starts and stops with the actual thrust.
		/// </summary>
		private Func<float> _thrust;

		/// <summary>Optional: a strength multiplier asked for every frame (for example per kind of hazard).</summary>
		public Func<float> ScaleProvider;

		/// <summary>
		/// Dynamic mode, for one-shot sounds whose loudness over time is the point (deaths): no loudness
		/// normalising, so a quiet creak stays quiet and a crunch stays loud.
		/// </summary>
		private bool _dynamic;
		private float _dynBandGain = 3f;
		private float _dynRumbleGain = 2.5f;

		/// <summary>Per-sound strength multiplier for dynamic mode, set when the game starts a sound.</summary>
		public volatile float OneShotGain = 1f;

		/// <summary>Dynamic mode, optional: asks for the strength every frame instead of using OneShotGain.</summary>
		public Func<float> GainOverride;

		/// <summary>
		/// Dynamic mode for interface sounds (clicks and chimes with nothing in the low band): higher tones for the
		/// envelope-following tick and a faster reaction, set in the "ui.live" effect.
		/// </summary>
		public bool UiProfile;

		/// <summary>Interface sounds: this sound's own tick tone (0 = the shared setting); the second tone follows at 1.58 times.</summary>
		public volatile float ToneHz;

		/// <summary>Interface sounds: this sound's own tick decay speed in Hz (0 = the shared setting).</summary>
		public volatile float ReleaseHz;

		/// <summary>Dynamic mode: scale the strength by the source's current volume (loops the game fades in and out).</summary>
		public bool FollowVolume;

		/// <summary>Dynamic mode: only sounds that set OneShotGain are felt (the source also plays unrelated sounds).</summary>
		public bool Strict;

		private OWAudioSource _owSource;
		private AudioSource _source;
		private float _forcedPan = float.NaN;

		private HapticSource _active;            // whatever is currently registered with the output
		private HapticLoopSource _loop;          // extracted mode
		private HapticStream _tapStream;         // live mode
		private AudioClip _loopClip;
		private bool _loopOn;

		// Set on the main thread, read on the audio thread.
		private volatile float _volume;
		private volatile float _liveGain;
		private float _peakVolume = 0.25f;
		private volatile bool _suppressLiveTap;  // an extracted one-shot played on this source
		private float _lpCoeff, _hpCoeff, _broadCoeff, _releaseCoeff, _bandCoeff, _refDecay, _phaseStep1, _phaseStep2;

		// Live filter state (audio thread only).
		private float _lp1, _lp2, _hpState, _broadEnv, _bandEnv, _refEnv;
		private double _phase1, _phase2;
		private float[] _block = new float[4096];

		private float _peakOut, _logTimer;

		/// <summary>Adds a tap to the source's GameObject (once). forcedPan: -1 left, +1 right, NaN = follow the source's pan.</summary>
		public static void Attach(OWAudioSource source, float forcedPan, Func<float> thrust = null, bool dynamic = false)
		{
			if (source == null || source.GetComponent<HapticTap>() != null) return;
			var tap = source.gameObject.AddComponent<HapticTap>();
			tap._owSource = source;
			tap._source = source.GetAudioSource();
			tap._forcedPan = forcedPan;
			tap._thrust = thrust;
			tap._dynamic = dynamic;
		}

		/// <summary>
		/// Dynamic tap on any Unity AudioSource (for sounds the game plays without an OWAudioSource). Reuses the tap if
		/// the source already has one. Returns null if the haptic output is not available.
		/// </summary>
		public static HapticTap AttachTo(AudioSource source, float pan)
		{
			if (source == null || DualSenseMod.Haptics == null) return null;
			var tap = source.GetComponent<HapticTap>();
			if (tap == null)
			{
				tap = source.gameObject.AddComponent<HapticTap>();
				tap._source = source;
				tap._dynamic = true;
			}
			tap._forcedPan = pan;
			return tap;
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
			ApplySettings();
			if (DualSenseMod.Haptics == null) enabled = false;
		}

		/// <summary>Copies the current menu values into the fields the audio thread uses.</summary>
		private void ApplySettings()
		{
			var live = Effects.Get("hap.live");
			int rate = AudioSettings.outputSampleRate;
			_gainFloor = live["gainFloor"];
			_startThrust = live["startThrust"];
			_curve = live["curve"];
			var dyn = Effects.Get("death.live");
			_dynBandGain = dyn["bandGain"];
			_dynRumbleGain = dyn["rumbleGain"];
			_referenceVolume = live["refVolume"];
			_bandTarget = live["bandTarget"];
			_lpCoeff = 1f - Mathf.Exp(-2f * Mathf.PI * live["lowPass"] / rate);
			_hpCoeff = 1f - Mathf.Exp(-2f * Mathf.PI * live["highPass"] / rate);
			_broadCoeff = 1f - Mathf.Exp(-2f * Mathf.PI * 20f / rate);
			_releaseCoeff = _broadCoeff;
			_bandCoeff = 1f - Mathf.Exp(-2f * Mathf.PI * 10f / rate);
			_refDecay = Mathf.Exp(-1f / (5f * rate));
			_phaseStep1 = 2f * Mathf.PI * live["rumbleLow"] / rate;
			_phaseStep2 = 2f * Mathf.PI * live["rumbleHigh"] / rate;

			// The interface profile overrides the general values above, so it comes last.
			if (UiProfile)
			{
				var ui = Effects.Get("ui.live");
				_dynBandGain = ui["bandGain"];
				_dynRumbleGain = ui["rumbleGain"];
				float low = ToneHz > 0f ? ToneHz : ui["toneLow"];
				float high = ToneHz > 0f ? ToneHz * 1.58f : ui["toneHigh"];
				float release = ReleaseHz > 0f ? ReleaseHz : ui["releaseHz"];
				_phaseStep1 = 2f * Mathf.PI * low / rate;
				_phaseStep2 = 2f * Mathf.PI * high / rate;
				_broadCoeff = 1f - Mathf.Exp(-2f * Mathf.PI * ui["reactionHz"] / rate);
				_releaseCoeff = 1f - Mathf.Exp(-2f * Mathf.PI * release / rate);
			}
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

		/// <summary>Extracted mode for looping sources with a prepared file, live mode otherwise.</summary>
		private void ChooseMode()
		{
			var haptics = DualSenseMod.Haptics;
			var mod = DualSenseMod.Instance;
			if (haptics == null || mod == null) return;

			AudioClip clip = _source.clip;
			if (mod.UseExtractedClips && _source.loop && clip != null)
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
			ApplySettings();
			ChooseMode();
			if (_active == null) return;

			_volume = _source.mute ? 0f : _source.volume;
			float gain = 0f;
			if (_source.isPlaying)
			{
				if (_dynamic)
				{
					gain = (GainOverride != null ? GainOverride() : OneShotGain) * (FollowVolume ? _volume : 1f);
				}
				else if (_thrust != null)
				{
					// Real thrust from the game's thruster model: starts and stops with the thrust itself.
					float t = Mathf.Clamp01(_thrust());
					if (t > _startThrust)
					{
						float onset = _startThrust > 0.0001f ? Mathf.Clamp01((t - _startThrust) / _startThrust) : 1f;
						float ratio = Mathf.Clamp01((t - _startThrust) / Mathf.Max(1f - _startThrust, 0.01f));
						gain = onset * Mathf.Lerp(_gainFloor, 1f, Mathf.Pow(ratio, _curve));
					}
				}
				else if (_volume > MinVolume)
				{
					// Sounds without a thruster behind them (ignition, rotation): follow the source volume.
					_peakVolume = Mathf.Max(_referenceVolume, Mathf.Max(_volume, _peakVolume * Mathf.Pow(PeakDecayPerSecond, Time.unscaledDeltaTime)));
					gain = Mathf.Lerp(_gainFloor, 1f, Mathf.Clamp01(_volume / _peakVolume));
				}
			}
			if (ScaleProvider != null) gain *= ScaleProvider();
			_liveGain = gain;

			float pan = float.IsNaN(_forcedPan) ? _source.panStereo : _forcedPan;
			float angle = (Mathf.Clamp(pan, -1f, 1f) + 1f) * 0.25f * Mathf.PI; // equal-power pan
			_active.GainLeft = Mathf.Cos(angle);
			_active.GainRight = Mathf.Sin(angle);

			if (_loop != null)
			{
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
					DualSenseMod.Debug(string.Format("tap {0}: mode={1} volume={2:F2} pan={3:F2} gain={4:F2} outPeak={5:F3}",
						name, _loop != null ? "clip" : "live", _volume, pan, gain, _peakOut));
					_peakOut = 0f;
				}
			}
		}

		/// <summary>
		/// Live conversion. Two parts are blended by how much of the sound lives in the actuators' band:
		/// the 25-350 Hz part itself, normalised to a constant level, and for hissy sounds a synthetic low rumble
		/// that follows the sound's overall loudness. The source volume is applied through _liveGain because the
		/// filter data is not scaled by it.
		/// </summary>
		private void OnAudioFilterRead(float[] data, int channels)
		{
			var stream = _tapStream;
			if (stream == null || channels < 1 || _suppressLiveTap) return;

			int frames = data.Length / channels;
			if (_block.Length < frames) _block = new float[frames];

			float gain = _liveGain;
			for (int i = 0; i < frames; i++)
			{
				float mono = 0f;
				int baseIndex = i * channels;
				for (int c = 0; c < channels; c++) mono += data[baseIndex + c];
				mono /= channels;

				_lp1 += _lpCoeff * (mono - _lp1);
				_lp2 += _lpCoeff * (_lp1 - _lp2);
				_hpState += _hpCoeff * (_lp2 - _hpState);
				float band = _lp2 - _hpState;

				_broadEnv += (Math.Abs(mono) > _broadEnv ? _broadCoeff : _releaseCoeff) * (Math.Abs(mono) - _broadEnv);
				_bandEnv += _bandCoeff * (Math.Abs(band) - _bandEnv);
				_refEnv = Math.Max(_broadEnv, _refEnv * _refDecay);

				float output = 0f;
				if (_broadEnv > Gate)
				{
					// Share of the sound's level that sits in the band: ~0.4 for engine rumble, ~0.05 for hiss.
					float ratio = _bandEnv / (_broadEnv + 1e-6f);
					float w = Math.Min(1f, Math.Max(0f, (ratio - 0.08f) / 0.17f));

					if (_dynamic)
					{
						// Keep the sound's own loudness: the band as it is, plus a rumble whose level follows the
						// overall level for the hissy, creaky parts that have little in the band itself.
						_phase1 += _phaseStep1; _phase2 += _phaseStep2;
						if (_phase1 > 6.2831853) _phase1 -= 6.2831853;
						if (_phase2 > 6.2831853) _phase2 -= 6.2831853;
						float tone = (float)(0.65 * Math.Sin(_phase1) + 0.35 * Math.Sin(_phase2));
						float dyn = band * _dynBandGain * w + tone * Math.Min(1f, _broadEnv * _dynRumbleGain) * (1f - w);
						_block[i] = dyn * gain;
						float da = Math.Abs(_block[i]); if (da > _peakOut) _peakOut = da;
						continue;
					}

					float bandPart = band / Math.Max(_bandEnv, 0.0015f) * _bandTarget;
					if (bandPart > 1f) bandPart = 1f; else if (bandPart < -1f) bandPart = -1f;

					_phase1 += _phaseStep1; _phase2 += _phaseStep2;
					if (_phase1 > 6.2831853) _phase1 -= 6.2831853;
					if (_phase2 > 6.2831853) _phase2 -= 6.2831853;
					float carrier = (float)(0.65 * Math.Sin(_phase1) + 0.35 * Math.Sin(_phase2));
					float rumblePart = carrier * _bandTarget * Math.Min(1f, _broadEnv / Math.Max(_refEnv, Gate));

					output = (w * bandPart + (1f - w) * rumblePart) * gain;
				}

				_block[i] = output;
				float a = Math.Abs(output); if (a > _peakOut) _peakOut = a;
			}
			stream.Write(_block, frames);
		}
	}
}
