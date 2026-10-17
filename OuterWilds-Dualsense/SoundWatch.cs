using System;
using System.Text;
using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// A finder for "which object plays this sound": when the setting watchClip is set (for example "Splash"),
	/// every time any audio source starts a clip whose name contains it, the clip and the source's object with its
	/// parents are written to the log. Off by default.
	/// </summary>
	internal static class SoundWatch
	{
		public static void Report(AudioSource source, AudioClip clip)
		{
			var mod = DualSenseMod.Instance;
			if (mod == null || string.IsNullOrEmpty(mod.WatchClip) || source == null || clip == null) return;
			if (clip.name.IndexOf(mod.WatchClip, StringComparison.OrdinalIgnoreCase) < 0) return;

			var path = new StringBuilder(source.name);
			for (Transform t = source.transform.parent; t != null; t = t.parent) path.Append(" < ").Append(t.name);
			DualSenseMod.Log(string.Format("Sound watch: '{0}' on {1} (volume {2:F2}, loop {3})", clip.name, path, source.volume, source.loop));
		}
	}
}
