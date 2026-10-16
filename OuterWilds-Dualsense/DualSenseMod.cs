using HarmonyLib;
using OWML.Common;
using OWML.ModHelper;
using UnityEngine;

namespace OuterWildsDualSense
{
	public class DualSenseMod : ModBehaviour
	{
		public static DualSenseMod Instance { get; private set; }
		public static DualSenseDevice Device { get; private set; }
		public static HapticAudio Haptics { get; private set; }
		private static float _hapticRetry;

		public bool Enabled = true;
		public bool AdaptiveTriggers = true;
		public float TriggerStrength = 1f;
		public bool Lightbar = true;
		public bool AudioHaptics = true;
		public float HapticGain = 1f;
		public bool DebugLog;

		private void Awake()
		{
			Instance = this;
			Device = new DualSenseDevice { Log = Log };
		}

		private void Start()
		{
			new Harmony("Blightyboi.OuterWilds-Dualsense").PatchAll(typeof(DualSenseMod).Assembly);
			Application.quitting += ResetController;
			Log("DualSense mod loaded.");
		}

		public override void Configure(IModConfig config)
		{
			Enabled = config.Enabled;
			AdaptiveTriggers = config.GetSettingsValue<bool>("adaptiveTriggers");
			TriggerStrength = Mathf.Clamp(config.GetSettingsValue<float>("triggerStrength"), 0f, 2f);
			Lightbar = config.GetSettingsValue<bool>("lightbar");
			AudioHaptics = config.GetSettingsValue<bool>("audioHaptics");
			HapticGain = Mathf.Clamp(config.GetSettingsValue<float>("hapticGain"), 0f, 4f);
			if (Haptics != null) Haptics.MasterGain = HapticGain;
			DebugLog = config.GetSettingsValue<bool>("debugLog");
			if (!Enabled) ResetController();
		}

		private void OnDestroy()
		{
			ResetController();
			if (Device != null) Device.Dispose();
		}

		/// <summary>Opens the haptic audio output if needed. Retries at most every 3 seconds.</summary>
		public static bool EnsureHaptics()
		{
			if (Haptics != null) return true;
			if (Time.unscaledTime < _hapticRetry) return false;
			_hapticRetry = Time.unscaledTime + 3f;
			Haptics = HapticAudio.TryCreate(Log);
			if (Haptics != null) Haptics.MasterGain = Instance.HapticGain;
			return Haptics != null;
		}

		private static void ResetController()
		{
			if (Device != null && Device.IsOpen) Device.Reset();
			if (Haptics != null) { Haptics.Dispose(); Haptics = null; }
		}

		public static void Log(string message)
		{
			if (Instance != null) Instance.ModHelper.Console.WriteLine(message, MessageType.Info);
		}

		public static void Debug(string message)
		{
			if (Instance != null && Instance.DebugLog) Instance.ModHelper.Console.WriteLine(message, MessageType.Debug);
		}
	}
}
