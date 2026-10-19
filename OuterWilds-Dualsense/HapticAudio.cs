using System;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;

namespace OuterWildsDualSense
{
	/// <summary>
	/// A mono sample stream filled from Unity's audio thread and drained by the WASAPI thread.
	/// Single writer, single reader, no locks.
	/// </summary>
	public abstract class HapticSource
	{
		public int SourceRate = 48000;
		public volatile float GainLeft = 0.7071f;
		public volatile float GainRight = 0.7071f;

		/// <summary>Called from the output thread. step = sourceRate / outputRate.</summary>
		public abstract float Next(double step);
	}

	/// <summary>A looping, pre-processed haptic waveform whose loudness follows a target gain.</summary>
	public sealed class HapticLoopSource : HapticSource
	{
		private readonly float[] _data;
		private double _pos;
		private float _gain;
		public volatile float TargetGain;

		public HapticLoopSource(float[] data, int rate)
		{
			_data = data;
			SourceRate = rate;
		}

		public override float Next(double step)
		{
			_gain += (TargetGain - _gain) * 0.0005f; // ~40 ms smoothing at 48 kHz
			if (_gain < 0.0005f && TargetGain <= 0f) return 0f;

			int i0 = (int)_pos;
			float frac = (float)(_pos - i0);
			float a = _data[i0];
			float b = _data[(i0 + 1) % _data.Length];
			_pos += step;
			if (_pos >= _data.Length) _pos -= _data.Length;
			return (a + (b - a) * frac) * _gain;
		}
	}

	/// <summary>
	/// The game's own controller rumble, rendered as haptics: the big motor as a low rumble, the small
	/// motor as a higher buzz, on both actuators.
	/// </summary>
	public sealed class HapticRumbleSource : HapticSource
	{
		// Tunable through the F1 menu (effect "hap.rumble").
		public volatile float LowHz = 48f;
		public volatile float HighHz = 135f;
		public volatile float LowLevel = 0.9f;
		public volatile float HighLevel = 0.6f;

		private double _lowPhase, _highPhase;
		private float _low, _high;
		public volatile float TargetLow;
		public volatile float TargetHigh;

		public HapticRumbleSource()
		{
			GainLeft = 1f;
			GainRight = 1f;
		}

		public override float Next(double step)
		{
			_low += (TargetLow - _low) * 0.002f;   // ~10 ms smoothing at 48 kHz
			_high += (TargetHigh - _high) * 0.002f;
			if (_low < 0.0005f && _high < 0.0005f && TargetLow <= 0f && TargetHigh <= 0f) return 0f;

			_lowPhase += 2 * Math.PI * LowHz / 48000.0;
			_highPhase += 2 * Math.PI * HighHz / 48000.0;
			if (_lowPhase > 2 * Math.PI) _lowPhase -= 2 * Math.PI;
			if (_highPhase > 2 * Math.PI) _highPhase -= 2 * Math.PI;
			return (float)(Math.Sin(_lowPhase) * _low * LowLevel + Math.Sin(_highPhase) * _high * HighLevel);
		}
	}

	/// <summary>A pre-processed haptic waveform played once (ignition, thruster bursts) and then dropped.</summary>
	public sealed class HapticOneShotSource : HapticSource
	{
		private readonly float[] _data;
		private readonly float _gain;
		private double _pos;
		public volatile bool Finished;

		public HapticOneShotSource(float[] data, int rate, float gain)
		{
			_data = data;
			SourceRate = rate;
			_gain = gain;
		}

		public override float Next(double step)
		{
			int i0 = (int)_pos;
			if (i0 >= _data.Length - 1) { Finished = true; return 0f; }
			float frac = (float)(_pos - i0);
			float v = _data[i0] + (_data[i0 + 1] - _data[i0]) * frac;
			_pos += step;
			return v * _gain;
		}
	}

	public sealed class HapticStream : HapticSource
	{
		private const int RingSize = 1 << 15; // power of two
		private const double MaxLatencySamples = 6000;
		private const double TargetLatencySamples = 1536;

		private readonly float[] _ring = new float[RingSize];
		private long _writePos;
		private double _readPos;

		public HapticStream(int sourceRate)
		{
			SourceRate = sourceRate;
		}

		/// <summary>Called from the audio thread.</summary>
		public void Write(float[] block, int count)
		{
			long wp = Volatile.Read(ref _writePos);
			for (int i = 0; i < count; i++) _ring[(wp + i) & (RingSize - 1)] = block[i];
			Volatile.Write(ref _writePos, wp + count);
		}

		/// <summary>Called from the WASAPI thread. Returns 0 while starved. step = sourceRate / outputRate.</summary>
		public override float Next(double step)
		{
			long wp = Volatile.Read(ref _writePos);
			double available = wp - _readPos;
			if (available > MaxLatencySamples) _readPos = wp - TargetLatencySamples; // drifted: resync
			else if (available < 2) return 0f;

			long i0 = (long)_readPos;
			float frac = (float)(_readPos - i0);
			float a = _ring[i0 & (RingSize - 1)];
			float b = _ring[(i0 + 1) & (RingSize - 1)];
			_readPos += step;
			return a + (b - a) * frac;
		}
	}

	/// <summary>
	/// Plays the haptic streams on channels 3 and 4 of the DualSense audio endpoint, which drive the
	/// voice-coil actuators. Channels 1 and 2 (the speaker) stay silent.
	/// </summary>
	public sealed class HapticAudio : IDisposable
	{
		private readonly object _lock = new object();
		private volatile HapticSource[] _streams = new HapticSource[0];
		private WaveOutPlayer _out;

		public volatile float MasterGain = 1f;

		/// <summary>The game's controller rumble, rendered as haptics.</summary>
		public readonly HapticRumbleSource Rumble = new HapticRumbleSource();

		private const int Channels = 4;
		private const int Rate = 48000;

		private Action<string> _log = delegate { };
		private int _nextReopen;
		private bool _lostLogged;
		private int _device = -1;
		private int _nextPoll;

		private HapticAudio()
		{
			AddSource(Rumble);
		}

		/// <summary>Opens the waveOut player on the controller's audio device. Returns false if the device is not there.</summary>
		private bool Open()
		{
			int device = FindDevice();
			if (device < 0) return false;
			_out = new WaveOutPlayer(device, Rate, Channels, Read);
			_device = device;
			_log("Haptic audio started on waveOut device " + device + " (" + Channels + " ch, " + Rate + " Hz).");
			return true;
		}

		/// <summary>
		/// Call every frame. When the controller was unplugged (or reconnected) its waveOut handle goes dead: buffers stop
		/// completing and nothing is played any more, while the HID connection reopens by itself. Drops the dead player and
		/// opens a new one as soon as the audio device is back. The sources (taps) stay registered, so every haptic resumes.
		/// </summary>
		public void Maintain()
		{
			var player = _out;
			bool gone = false;
			if (player != null && unchecked(Environment.TickCount - _nextPoll) >= 0)
			{
				// The audio device may vanish or come back under another number without the old handle ever failing.
				_nextPoll = Environment.TickCount + 1000;
				gone = FindDevice() != _device;
			}
			if (player != null && (gone || player.IsDead))
			{
				_out = null;
				if (!_lostLogged) { _lostLogged = true; _log("Haptic audio output lost (controller disconnected?), waiting for the device."); }
				// Closing a handle of a removed device can block: keep that off the game thread.
				new Thread(() => { try { player.Dispose(); } catch (Exception) { } }) { IsBackground = true }.Start();
				_nextReopen = Environment.TickCount + 1000;
			}
			if (_out != null || unchecked(Environment.TickCount - _nextReopen) < 0) return;
			_nextReopen = Environment.TickCount + 2000;
			try
			{
				if (Open()) { _lostLogged = false; _log("Haptic audio output restored."); }
			}
			catch (Exception e)
			{
				_log("Haptic audio could not reopen yet: " + e.Message);
			}
		}

		/// <summary>
		/// Opens the DualSense audio output through winmm (waveOut). WASAPI and NAudio need COM interop,
		/// which Unity's Mono runtime cannot do. Returns null if the controller's audio device is not there (yet).
		/// </summary>
		public static HapticAudio TryCreate(Action<string> log)
		{
			try
			{
				var haptics = new HapticAudio();
				haptics._log = log;
				if (!haptics.Open()) { log("DualSense audio device not found."); return null; }
				return haptics;
			}
			catch (Exception e)
			{
				log("Haptic audio failed to start: " + e);
			}
			return null;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct WAVEOUTCAPS
		{
			public ushort wMid;
			public ushort wPid;
			public uint vDriverVersion;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
			public uint dwFormats;
			public ushort wChannels;
			public ushort wReserved1;
			public uint dwSupport;
		}

		[DllImport("winmm.dll")]
		private static extern int waveOutGetNumDevs();

		[DllImport("winmm.dll", CharSet = CharSet.Unicode)]
		private static extern int waveOutGetDevCaps(IntPtr deviceId, ref WAVEOUTCAPS caps, int size);

		private static int FindDevice()
		{
			int count = waveOutGetNumDevs();
			for (int i = 0; i < count; i++)
			{
				var caps = new WAVEOUTCAPS();
				if (waveOutGetDevCaps((IntPtr)i, ref caps, Marshal.SizeOf(typeof(WAVEOUTCAPS))) != 0) continue;
				// Names are cut to 31 characters, e.g. "Speakers (DualSense Wireless Con".
				if (caps.szPname != null && caps.szPname.IndexOf("DualSense", StringComparison.OrdinalIgnoreCase) >= 0) return i;
			}
			return -1;
		}

		public T AddSource<T>(T source) where T : HapticSource
		{
			lock (_lock)
			{
				var list = new HapticSource[_streams.Length + 1];
				Array.Copy(_streams, list, _streams.Length);
				list[list.Length - 1] = source;
				_streams = list;
			}
			return source;
		}

		public void RemoveSource(HapticSource source)
		{
			lock (_lock)
			{
				var list = new List<HapticSource>(_streams);
				list.Remove(source);
				_streams = list.ToArray();
			}
		}

		/// <summary>Pull callback: always fills the whole buffer (silence if nothing plays).</summary>
		public int Read(byte[] buffer, int offset, int count)
		{
			int frameBytes = 2 * Channels;
			int frames = count / frameBytes;
			Array.Clear(buffer, offset, frames * frameBytes);

			var streams = _streams;
			float master = MasterGain;
			for (int f = 0; f < frames; f++)
			{
				float left = 0f, right = 0f;
				for (int s = 0; s < streams.Length; s++)
				{
					var st = streams[s];
					float v = st.Next((double)st.SourceRate / Rate);
					left += v * st.GainLeft;
					right += v * st.GainRight;
				}
				int o = offset + f * frameBytes + 4; // channels 3 and 4
				WriteSample(buffer, o, left * master);
				WriteSample(buffer, o + 2, right * master);
			}
			for (int s = 0; s < streams.Length; s++)
			{
				var shot = streams[s] as HapticOneShotSource;
				if (shot != null && shot.Finished) RemoveSource(shot);
			}
			return frames * frameBytes;
		}

		private static void WriteSample(byte[] buffer, int index, float value)
		{
			short pcm = (short)(Math.Tanh(value) * 32767.0);
			buffer[index] = (byte)(pcm & 0xFF);
			buffer[index + 1] = (byte)((pcm >> 8) & 0xFF);
		}

		public void Dispose()
		{
			try
			{
				if (_out != null) { _out.Dispose(); }
			}
			catch (Exception) { }
			_out = null;
		}
	}

	/// <summary>Minimal event-driven waveOut player: 16-bit PCM, fed by a callback running on its own thread.</summary>
	internal sealed class WaveOutPlayer : IDisposable
	{
		private const int BufferCount = 4;
		private const int BufferMs = 20;
		private const uint CALLBACK_EVENT = 0x00050000;
		private const uint WHDR_DONE = 0x1;

		[StructLayout(LayoutKind.Sequential, Pack = 2)]
		private struct WAVEFORMATEXTENSIBLE
		{
			public ushort wFormatTag, nChannels;
			public uint nSamplesPerSec, nAvgBytesPerSec;
			public ushort nBlockAlign, wBitsPerSample, cbSize, wValidBitsPerSample;
			public uint dwChannelMask;
			public Guid SubFormat;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct WAVEHDR
		{
			public IntPtr lpData;
			public uint dwBufferLength, dwBytesRecorded;
			public IntPtr dwUser;
			public uint dwFlags, dwLoops;
			public IntPtr lpNext, reserved;
		}

		[DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr h, IntPtr device, ref WAVEFORMATEXTENSIBLE f, IntPtr callback, IntPtr instance, uint flags);
		[DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
		[DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
		[DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
		[DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr h);
		[DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr h);

		private readonly Func<byte[], int, int, int> _fill;
		private readonly AutoResetEvent _event = new AutoResetEvent(false);
		private readonly IntPtr[] _headers = new IntPtr[BufferCount];
		private readonly IntPtr[] _data = new IntPtr[BufferCount];
		private readonly byte[] _managed;
		private readonly int _bufferBytes;
		private readonly Thread _thread;
		private IntPtr _handle;
		private volatile bool _stop;
		private volatile bool _failed;
		private int _lastDone = Environment.TickCount;

		/// <summary>True when the device stopped taking audio: a write failed or no buffer finished for 1.5 seconds.</summary>
		public bool IsDead
		{
			get { return _failed || unchecked(Environment.TickCount - _lastDone) > 1500; }
		}

		public WaveOutPlayer(int device, int rate, int channels, Func<byte[], int, int, int> fill)
		{
			_fill = fill;
			int blockAlign = channels * 2;
			_bufferBytes = rate * BufferMs / 1000 * blockAlign;
			_managed = new byte[_bufferBytes];

			var format = new WAVEFORMATEXTENSIBLE
			{
				wFormatTag = 0xFFFE, nChannels = (ushort)channels, nSamplesPerSec = (uint)rate,
				nAvgBytesPerSec = (uint)(rate * blockAlign), nBlockAlign = (ushort)blockAlign,
				wBitsPerSample = 16, cbSize = 22, wValidBitsPerSample = 16,
				dwChannelMask = 0x33, // quad: the controller's 4-channel endpoint
				SubFormat = new Guid("00000001-0000-0010-8000-00aa00389b71") // PCM
			};
			int result = waveOutOpen(out _handle, (IntPtr)device, ref format, _event.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT);
			if (result != 0) throw new InvalidOperationException("waveOutOpen failed: " + result);

			int hdrSize = Marshal.SizeOf(typeof(WAVEHDR));
			for (int i = 0; i < BufferCount; i++)
			{
				_data[i] = Marshal.AllocHGlobal(_bufferBytes);
				_headers[i] = Marshal.AllocHGlobal(hdrSize);
				var hdr = new WAVEHDR { lpData = _data[i], dwBufferLength = (uint)_bufferBytes };
				Marshal.StructureToPtr(hdr, _headers[i], false);
				waveOutPrepareHeader(_handle, _headers[i], hdrSize);
				Queue(i);
			}

			_thread = new Thread(Run) { IsBackground = true, Name = "DualSense haptics" };
			_thread.Start();
		}

		private void Queue(int i)
		{
			Array.Clear(_managed, 0, _managed.Length);
			_fill(_managed, 0, _bufferBytes);
			Marshal.Copy(_managed, 0, _data[i], _bufferBytes);
			if (waveOutWrite(_handle, _headers[i], Marshal.SizeOf(typeof(WAVEHDR))) != 0) _failed = true;
		}

		private void Run()
		{
			while (!_stop)
			{
				_event.WaitOne(100);
				for (int i = 0; i < BufferCount && !_stop; i++)
				{
					var hdr = (WAVEHDR)Marshal.PtrToStructure(_headers[i], typeof(WAVEHDR));
					if ((hdr.dwFlags & WHDR_DONE) != 0) { _lastDone = Environment.TickCount; Queue(i); }
				}
			}
		}

		public void Dispose()
		{
			if (_stop) return;
			_stop = true;
			_event.Set();
			if (_thread != null) _thread.Join(500);
			waveOutReset(_handle);
			int hdrSize = Marshal.SizeOf(typeof(WAVEHDR));
			for (int i = 0; i < BufferCount; i++)
			{
				waveOutUnprepareHeader(_handle, _headers[i], hdrSize);
				Marshal.FreeHGlobal(_headers[i]);
				Marshal.FreeHGlobal(_data[i]);
			}
			waveOutClose(_handle);
		}
	}
}
