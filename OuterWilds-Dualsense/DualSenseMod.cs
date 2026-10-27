using System;
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
		public bool UseExtractedClips;
		public string WatchClip = "";
		public bool DebugLog;
		public bool DebugMenuEnabled;
		/// <summary>0 = off, 1 = full (roll, pitch, yaw), 2 = roll only.</summary>
		public int GyroMode;

		private void Awake()
		{
			Instance = this;
			Device = new DualSenseDevice { Log = Log };
		}

		private void Start()
		{
			var harmony = new Harmony("Blightyboi.OuterWilds-Dualsense");
			harmony.PatchAll(typeof(DualSenseMod).Assembly);
			WorldSounds.Install(harmony);
			Application.quitting += ResetController;
			Effects.Load();
			EffectPreview.Wire();
			try { UiHaptics.TapMenu(Locator.GetMenuAudioController()); } catch (Exception) { } // the menu may already exist
			gameObject.AddComponent<DebugMenu>();
			Log("DualSense mod loaded.");
		}

		private void Update()
		{
			TouchpadInput.Tick();
			GyroInput.Tick();
			// Keep the output in step with the values being edited in the F1 menu.
			if (Haptics == null) return;
			Haptics.Maintain(); // reopens the audio output after the controller was unplugged
			// The pause menu silences the game's audio but not our haptics: fade them out with it, and back in on resume.
			// The F1 menu lets them through only while one of its Play buttons is on, so they can be tried on a paused game.
			bool silent = OWTime.IsPaused(OWTime.PauseType.Menu) && !(DebugMenu.IsOpen && DebugMenu.Previewing);
			float step = Time.unscaledDeltaTime / (silent ? 0.25f : 0.4f);
			Haptics.PauseGain = Mathf.MoveTowards(Haptics.PauseGain, silent ? 0f : 1f, step);
			TornadoHaptics.Tick();
			var live = Effects.Get("hap.live");
			var rumble = Effects.Get("hap.rumble");
			Haptics.MasterGain = HapticGain * live["gain"];
			Haptics.Rumble.LowHz = rumble["lowHz"];
			Haptics.Rumble.HighHz = rumble["highHz"];
			Haptics.Rumble.LowLevel = rumble["lowLevel"];
			Haptics.Rumble.HighLevel = rumble["highLevel"];
		}

		public override void Configure(IModConfig config)
		{
			Enabled = config.Enabled;
			AdaptiveTriggers = config.GetSettingsValue<bool>("adaptiveTriggers");
			TriggerStrength = Mathf.Clamp(config.GetSettingsValue<float>("triggerStrength"), 0f, 2f);
			Lightbar = config.GetSettingsValue<bool>("lightbar");
			AudioHaptics = config.GetSettingsValue<bool>("audioHaptics");
			UseExtractedClips = config.GetSettingsValue<bool>("useExtractedClips");
			WatchClip = config.GetSettingsValue<string>("watchClip") ?? "";
			HapticGain = Mathf.Clamp(config.GetSettingsValue<float>("hapticGain"), 0f, 4f);
			if (Haptics != null) Haptics.MasterGain = HapticGain;
			DebugLog = config.GetSettingsValue<bool>("debugLog");
			DebugMenuEnabled = config.GetSettingsValue<bool>("debugMenu");
			GyroMode = ParseGyroMode(config.GetSettingsValue<string>("gyroMode"));
			if (!Enabled) ResetController();
		}

		private static int ParseGyroMode(string value)
		{
			if (value == "Full") return 1;
			if (value == "Roll only") return 2;
			return 0;
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
