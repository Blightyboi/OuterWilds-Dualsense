using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OuterWildsDualSense
{
	/// <summary>
	/// Pulls the sound files the game plays out of their AudioClips, turns them into haptic waveforms
	/// (mono, 25-350 Hz, normalised loudness) and caches the result. Processed clips are also written
	/// as WAV files next to the mod so they can be inspected.
	/// </summary>
	internal static class HapticClipBank
	{
		private const float LowPassHz = 350f;
		private const float HighPassHz = 25f;
		private const float TargetRms = 0.5f;

		private static readonly Dictionary<AudioClip, float[]> Cache = new Dictionary<AudioClip, float[]>();

		/// <summary>
		/// Returns the haptic waveform for a clip: first the file prepared by tools/extract_haptics.py,
		/// then the live samples if the game allows reading them, otherwise null (live audio tap).
		/// </summary>
		public static float[] Get(AudioClip clip)
		{
			if (clip == null) return null;
			float[] cached;
			if (Cache.TryGetValue(clip, out cached)) return cached;

			float[] result = null;
			try
			{
				result = LoadExtracted(clip.name) ?? Process(clip);
			}
			catch (Exception e)
			{
				DualSenseMod.Log("Could not process clip '" + clip.name + "': " + e.Message);
			}
			Cache[clip] = result;
			return result;
		}

		/// <summary>Reads haptic-clips/{name}.wav (mono, 16-bit PCM) written by the extraction script.</summary>
		private static float[] LoadExtracted(string name)
		{
			var mod = DualSenseMod.Instance;
			if (mod == null) return null;
			string path = Path.Combine(Path.Combine(mod.ModHelper.Manifest.ModFolderPath, "haptic-clips"), name + ".wav");
			if (!File.Exists(path)) return null;

			byte[] bytes = File.ReadAllBytes(path);
			// Find the "data" chunk after the 12-byte RIFF header.
			int pos = 12;
			while (pos + 8 <= bytes.Length)
			{
				string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
				int size = BitConverter.ToInt32(bytes, pos + 4);
				if (id == "data")
				{
					int count = Math.Min(size, bytes.Length - pos - 8) / 2;
					var samples = new float[count];
					for (int i = 0; i < count; i++) samples[i] = BitConverter.ToInt16(bytes, pos + 8 + i * 2) / 32768f;
					DualSenseMod.Log("Loaded extracted haptic clip '" + name + "' (" + count + " samples).");
					return samples;
				}
				pos += 8 + size + (size & 1);
			}
			return null;
		}

		private static float[] Process(AudioClip clip)
		{
			int channels = clip.channels;
			int frames = clip.samples;
			DualSenseMod.Debug(string.Format("Clip '{0}': {1} frames, {2} ch, {3} Hz, loadType={4}, loadState={5}",
				clip.name, frames, channels, clip.frequency, clip.loadType, clip.loadState));

			// Reading a compressed clip makes Unity log an error, so only decompressed clips are tried.
			if (clip.loadType != AudioClipLoadType.DecompressOnLoad)
			{
				DualSenseMod.Debug("Clip '" + clip.name + "' is compressed and has no extracted file, using the live audio tap.");
				return null;
			}
			if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
			var raw = new float[frames * channels];
			if (frames <= 1 || !clip.GetData(raw, 0))
			{
				DualSenseMod.Log("Clip '" + clip.name + "' is not readable, falling back to live audio tap.");
				return null;
			}

			// Mono downmix.
			var mono = new float[frames];
			for (int i = 0; i < frames; i++)
			{
				float sum = 0f;
				for (int c = 0; c < channels; c++) sum += raw[i * channels + c];
				mono[i] = sum / channels;
			}

			// Band-pass twice over the loop so the filter state at the seam matches the start of the clip.
			float lp = 1f - Mathf.Exp(-2f * Mathf.PI * LowPassHz / clip.frequency);
			float hp = 1f - Mathf.Exp(-2f * Mathf.PI * HighPassHz / clip.frequency);
			float l1 = 0f, l2 = 0f, h = 0f;
			var shaped = new float[frames];
			for (int pass = 0; pass < 2; pass++)
			{
				for (int i = 0; i < frames; i++)
				{
					l1 += lp * (mono[i] - l1);
					l2 += lp * (l1 - l2);
					h += hp * (l2 - h);
					shaped[i] = l2 - h;
				}
			}

			// Normalise loudness: the game's engine loops are quiet and the actuators need a strong signal.
			double energy = 0;
			for (int i = 0; i < frames; i++) energy += shaped[i] * shaped[i];
			float rms = (float)Math.Sqrt(energy / frames);
			if (rms < 0.01f)
			{
				DualSenseMod.Log("Clip '" + clip.name + "' has no energy in the haptic band.");
				return null;
			}
			float scale = TargetRms / rms;
			for (int i = 0; i < frames; i++) shaped[i] = Mathf.Clamp(shaped[i] * scale, -1f, 1f);

			DualSenseMod.Log(string.Format("Clip '{0}' -> haptic waveform, rms {1:F4} scaled x{2:F1}.", clip.name, rms, scale));
			DumpWav(clip.name, shaped, clip.frequency);
			return shaped;
		}

		private static void DumpWav(string name, float[] samples, int rate)
		{
			try
			{
				var mod = DualSenseMod.Instance;
				if (mod == null) return;
				string dir = Path.Combine(mod.ModHelper.Manifest.ModFolderPath, "haptic-clips");
				Directory.CreateDirectory(dir);
				string safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
				using (var w = new BinaryWriter(File.Create(Path.Combine(dir, safe + ".wav"))))
				{
					int dataBytes = samples.Length * 2;
					w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + dataBytes);
					w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); w.Write(16);
					w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
					w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(dataBytes);
					foreach (float f in samples) w.Write((short)(Mathf.Clamp(f, -1f, 1f) * 32767f));
				}
			}
			catch (Exception e)
			{
				DualSenseMod.Debug("Could not write haptic wav: " + e.Message);
			}
		}
	}
}
