using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OuterWildsDualSense
{
	/// <summary>One DualSense trigger effect: a mode byte plus up to ten parameter bytes.</summary>
	public struct TriggerEffect
	{
		public byte Mode;
		public byte[] Params; // always 10 bytes

		public static readonly TriggerEffect Off = Make(0x05);

		private static TriggerEffect Make(byte mode)
		{
			return new TriggerEffect { Mode = mode, Params = new byte[10] };
		}

		/// <summary>Zone mask for zones start..9 (zone 0 is the top of the trigger travel).</summary>
		private static ushort ZoneMask(int startZone)
		{
			ushort mask = 0;
			for (int z = Math.Max(0, startZone); z < 10; z++) mask |= (ushort)(1 << z);
			return mask;
		}

		/// <summary>Packs one 3-bit strength (0..7) per zone into 32 bits.</summary>
		private static uint PackStrengths(Func<int, int> strengthForZone, ushort mask)
		{
			uint packed = 0;
			for (int z = 0; z < 10; z++)
			{
				if ((mask & (1 << z)) == 0) continue;
				int s = Math.Max(1, Math.Min(8, strengthForZone(z))) - 1;
				packed |= (uint)s << (3 * z);
			}
			return packed;
		}

		private static void WriteMaskAndStrengths(byte[] p, ushort mask, uint packed)
		{
			p[0] = (byte)(mask & 0xFF);
			p[1] = (byte)(mask >> 8);
			p[2] = (byte)(packed & 0xFF);
			p[3] = (byte)((packed >> 8) & 0xFF);
			p[4] = (byte)((packed >> 16) & 0xFF);
			p[5] = (byte)((packed >> 24) & 0xFF);
		}

		/// <summary>Constant resistance from startZone to the end of the travel. Strength is 1..8.</summary>
		public static TriggerEffect Resistance(int startZone, int strength)
		{
			if (strength <= 0) return Off;
			var e = Make(0x21);
			ushort mask = ZoneMask(startZone);
			WriteMaskAndStrengths(e.Params, mask, PackStrengths(z => strength, mask));
			return e;
		}

		/// <summary>Resistance that ramps linearly from strengthStart (zone 0) to strengthEnd (zone 9).</summary>
		public static TriggerEffect ResistanceSlope(int strengthStart, int strengthEnd)
		{
			if (strengthStart <= 0 && strengthEnd <= 0) return Off;
			var e = Make(0x21);
			ushort mask = ZoneMask(0);
			WriteMaskAndStrengths(e.Params, mask,
				PackStrengths(z => (int)Math.Round(strengthStart + (strengthEnd - strengthStart) * (z / 9f)), mask));
			return e;
		}

		/// <summary>Vibration in the trigger. Frequency in Hz, amplitude 1..8.</summary>
		public static TriggerEffect Vibration(int startZone, int frequencyHz, int amplitude)
		{
			if (amplitude <= 0 || frequencyHz <= 0) return Off;
			var e = Make(0x26);
			ushort mask = ZoneMask(startZone);
			WriteMaskAndStrengths(e.Params, mask, PackStrengths(z => amplitude, mask));
			e.Params[8] = (byte)Math.Min(255, frequencyHz);
			return e;
		}

		/// <summary>
		/// Machine effect: the trigger alternates between two amplitudes (1..8) at frequency Hz, which pushes the
		/// finger back in pulses. period sets how long each phase lasts.
		/// </summary>
		public static TriggerEffect Machine(int startZone, int endZone, int amplitudeA, int amplitudeB, int frequencyHz, int period)
		{
			var e = Make(0x27);
			int zones = (1 << Math.Max(0, Math.Min(9, startZone))) | (1 << Math.Max(0, Math.Min(9, endZone)));
			e.Params[0] = (byte)(zones & 0xFF);
			e.Params[1] = (byte)((zones >> 8) & 0xFF);
			int a = Math.Max(1, Math.Min(8, amplitudeA)) - 1;
			int b = Math.Max(1, Math.Min(8, amplitudeB)) - 1;
			e.Params[2] = (byte)(a | (b << 3));
			e.Params[3] = (byte)Math.Min(255, frequencyHz);
			e.Params[4] = (byte)Math.Min(255, period);
			return e;
		}

		public bool SameAs(TriggerEffect other)
		{
			if (Mode != other.Mode) return false;
			for (int i = 0; i < 10; i++)
				if ((Params == null ? 0 : Params[i]) != (other.Params == null ? 0 : other.Params[i])) return false;
			return true;
		}
	}

	/// <summary>
	/// Talks to a USB-connected DualSense directly over HID output report 0x02.
	/// Bluetooth (report 0x31 + CRC32) is not implemented yet.
	/// </summary>
	public sealed class DualSenseDevice : IDisposable
	{
		private const int ReportSize = 48;

		private SafeFileHandle _handle;
		private string _path;
		private TriggerEffect _left = TriggerEffect.Off;
		private TriggerEffect _right = TriggerEffect.Off;
		private byte _r, _g, _b;
		private bool _lightbarEnabled;
		private bool _dirty = true;
		private float _retryTimer;
		private float _resendTimer;
		private bool _loggedFirstWrite;
		private bool _loggedMissing;
		private bool _lightbarSetupPending;
		private byte _motorRight, _motorLeft;
		private bool _rumbleWasOn;

		/// <summary>Receives diagnostic messages (set by the mod).</summary>
		public Action<string> Log = delegate { };

		public bool IsOpen { get { return _handle != null && !_handle.IsInvalid && !_handle.IsClosed; } }

		public void SetTriggers(TriggerEffect left, TriggerEffect right)
		{
			if (!left.SameAs(_left)) { _left = left; _dirty = true; }
			if (!right.SameAs(_right)) { _right = right; _dirty = true; }
		}

		/// <summary>Sets the two rumble motors (0..1). hi = small right motor, low = big left motor.</summary>
		public void SetMotors(float hi, float low)
		{
			byte r = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(hi * 255f)));
			byte l = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(low * 255f)));
			if (r != _motorRight || l != _motorLeft) { _motorRight = r; _motorLeft = l; _dirty = true; }
		}

		public void SetLightbar(byte r, byte g, byte b)
		{
			if (_lightbarEnabled && _r == r && _g == g && _b == b) return;
			if (!_lightbarEnabled) _lightbarSetupPending = true;
			_lightbarEnabled = true; _r = r; _g = g; _b = b; _dirty = true;
		}

		/// <summary>Sends the report if anything changed. Call once per frame; reconnects on its own.</summary>
		public void Flush(float unscaledDeltaTime)
		{
			if (!IsOpen)
			{
				_retryTimer -= unscaledDeltaTime;
				if (_retryTimer > 0f) return;
				_retryTimer = 2f;
				if (!TryOpen()) return;
				_dirty = true;
			}
			// Other software (Rewired, Steam) also writes output reports, so resend periodically.
			_resendTimer -= unscaledDeltaTime;
			if (_resendTimer <= 0f) { _dirty = true; _resendTimer = 0.25f; }
			if (!_dirty) return;

			var d = new byte[ReportSize];
			d[0] = 0x02;
			// Valid flags: right + left trigger effect. The rumble bits (compat rumble, haptics select) would
			// weaken the audio haptics, so they are only set while a motor runs and for one report after it stops.
			bool rumbling = _motorRight != 0 || _motorLeft != 0;
			d[1] = (byte)(0x0C | (rumbling || _rumbleWasOn ? 0x03 : 0x00));
			_rumbleWasOn = rumbling;
			d[2] = (byte)(_lightbarEnabled ? 0x04 : 0x00);
			d[3] = _motorRight;
			d[4] = _motorLeft;
			d[11] = _right.Mode;
			Buffer.BlockCopy(_right.Params ?? new byte[10], 0, d, 12, 10);
			d[22] = _left.Mode;
			Buffer.BlockCopy(_left.Params ?? new byte[10], 0, d, 23, 10);
			if (_lightbarEnabled)
			{
				// The setup command restarts the lightbar fade animation (visible as blinking),
				// so only send it with the first report after the colour changed.
				if (_lightbarSetupPending)
				{
					d[39] = 0x02;
					d[42] = 0x02;
					_lightbarSetupPending = false;
				}
				d[45] = _r; d[46] = _g; d[47] = _b;
			}

			int written;
			if (!NativeMethods.WriteFile(_handle, d, d.Length, out written, IntPtr.Zero))
			{
				// Controller unplugged or handle lost: drop it and retry later.
				Log("HID write failed, Win32 error " + Marshal.GetLastWin32Error());
				Close();
				return;
			}
			_dirty = false;
			if (!_loggedFirstWrite)
			{
				_loggedFirstWrite = true;
				Log("First HID write ok (" + written + " bytes).");
			}
		}

		private bool TryOpen()
		{
			_path = NativeMethods.FindDualSensePath();
			if (_path == null)
			{
				if (!_loggedMissing) { _loggedMissing = true; Log("No DualSense found over USB."); }
				return false;
			}
			// Share read/write so Rewired and Steam can keep using the device.
			var h = NativeMethods.CreateFile(_path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
			if (h.IsInvalid) return false;
			_handle = h;
			Log("Opened DualSense: " + _path);
			return true;
		}

		private void Close()
		{
			if (_handle != null) _handle.Close();
			_handle = null;
		}

		/// <summary>Resets triggers and lightbar so the controller is left in a clean state.</summary>
		public void Reset()
		{
			_left = TriggerEffect.Off;
			_right = TriggerEffect.Off;
			_lightbarEnabled = false;
			_motorRight = 0;
			_motorLeft = 0;
			_dirty = true;
			Flush(10f);
		}

		public void Dispose()
		{
			Close();
		}
	}

	internal static class NativeMethods
	{
		[StructLayout(LayoutKind.Sequential)]
		private struct SP_DEVICE_INTERFACE_DATA
		{
			public int cbSize;
			public Guid InterfaceClassGuid;
			public int Flags;
			public IntPtr Reserved;
		}

		[DllImport("hid.dll")]
		private static extern void HidD_GetHidGuid(out Guid guid);

		[DllImport("setupapi.dll", SetLastError = true)]
		private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, int flags);

		[DllImport("setupapi.dll", SetLastError = true)]
		private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, int index, ref SP_DEVICE_INTERFACE_DATA data);

		[DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
		private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr info);

		[DllImport("setupapi.dll")]
		private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
		public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

		[DllImport("kernel32.dll", SetLastError = true)]
		public static extern bool WriteFile(SafeFileHandle handle, byte[] buffer, int count, out int written, IntPtr overlapped);

		/// <summary>Finds the HID interface of a Sony DualSense (VID 054C, PID 0CE6 or Edge 0DF2).</summary>
		public static string FindDualSensePath()
		{
			Guid guid;
			HidD_GetHidGuid(out guid);
			IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
			try
			{
				for (int i = 0; ; i++)
				{
					var data = new SP_DEVICE_INTERFACE_DATA();
					data.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
					if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) break;

					int required;
					SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
					IntPtr buffer = Marshal.AllocHGlobal(required);
					try
					{
						Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6); // cbSize of the detail struct
						if (!SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, required, out required, IntPtr.Zero)) continue;
						string path = Marshal.PtrToStringAuto(new IntPtr(buffer.ToInt64() + 4));
						string lower = path == null ? "" : path.ToLowerInvariant();
						if (lower.Contains("vid_054c&pid_0ce6") || lower.Contains("vid_054c&pid_0df2")) return path;
					}
					finally
					{
						Marshal.FreeHGlobal(buffer);
					}
				}
			}
			finally
			{
				SetupDiDestroyDeviceInfoList(set);
			}
			return null;
		}
	}
}
