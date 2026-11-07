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
		public bool Lightbar = true;
		public bool AudioHaptics = true;
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
			// The same goes for playing on the keyboard or mouse: the controller lies on the desk then, so it falls silent until
			// the game sees a controller input again (OWInput.UsingGamepad, the last device that gave a command).
			// The sounds of the pause menu itself (HapticSource.IgnorePause) pass the pause but not the keyboard.
			bool previewing = DebugMenu.IsOpen && DebugMenu.Previewing;
			bool paused = OWTime.IsPaused(OWTime.PauseType.Menu) && !previewing;
			bool keyboard = !GamepadInUse() && !previewing;
			Haptics.PauseGain = Mathf.MoveTowards(Haptics.PauseGain, paused ? 0f : 1f, Time.unscaledDeltaTime / (paused ? 0.25f : 0.4f));
			Haptics.InputGain = Mathf.MoveTowards(Haptics.InputGain, keyboard ? 0f : 1f, Time.unscaledDeltaTime / (keyboard ? 0.25f : 0.4f));
			// While an F1 Play button is on, the rest of the game's haptics fall silent so the two do not mix.
			Haptics.GameGain = Mathf.MoveTowards(Haptics.GameGain, DebugMenu.IsOpen && DebugMenu.Previewing ? 0f : 1f, Time.unscaledDeltaTime / 0.15f);
			TornadoHaptics.Tick();
			WindowHaptics.Tick();
			var live = Effects.Get("hap.live");
			var rumble = Effects.Get("hap.rumble");
			Haptics.MasterGain = live["gain"];
			Haptics.Rumble.LowHz = rumble["lowHz"];
			Haptics.Rumble.HighHz = rumble["highHz"];
			Haptics.Rumble.LowLevel = rumble["lowLevel"];
			Haptics.Rumble.HighLevel = rumble["highLevel"];
		}

		/// <summary>True while the game's last input came from a controller (false after the player switched to keyboard or mouse).</summary>
		internal static bool GamepadInUse()
		{
			try { return OWInput.UsingGamepad(); }
			catch (Exception) { return true; } // the input manager does not exist yet (title screen loading): do not silence
		}

		public override void Configure(IModConfig config)
		{
			Enabled = config.Enabled;
			AdaptiveTriggers = config.GetSettingsValue<bool>("adaptiveTriggers");
			Lightbar = config.GetSettingsValue<bool>("lightbar");
			AudioHaptics = config.GetSettingsValue<bool>("audioHaptics");
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
			if (Haptics != null) Haptics.MasterGain = 1f;
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
