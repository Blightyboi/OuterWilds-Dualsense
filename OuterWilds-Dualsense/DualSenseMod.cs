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

		public bool Enabled = true;
		public bool AdaptiveTriggers = true;
		public float TriggerStrength = 1f;
		public bool Lightbar = true;
		public bool DebugLog;

		private void Awake()
		{
			Instance = this;
			Device = new DualSenseDevice();
		}

		private void Start()
		{
			new Harmony("Blightyboi.OuterWilds-Dualsense").PatchAll(typeof(DualSenseMod).Assembly);
			Application.quitting += ResetController;
			Log("DualSense mod loaded.");
		}

		public override void Configure(IModConfig config)
		{
			Enabled = config.GetSettingsValue<bool>("enabled");
			AdaptiveTriggers = config.GetSettingsValue<bool>("adaptiveTriggers");
			TriggerStrength = Mathf.Clamp(config.GetSettingsValue<float>("triggerStrength"), 0f, 2f);
			Lightbar = config.GetSettingsValue<bool>("lightbar");
			DebugLog = config.GetSettingsValue<bool>("debugLog");
			if (!Enabled) ResetController();
		}

		private void OnDestroy()
		{
			ResetController();
			if (Device != null) Device.Dispose();
		}

		private static void ResetController()
		{
			if (Device != null && Device.IsOpen) Device.Reset();
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
