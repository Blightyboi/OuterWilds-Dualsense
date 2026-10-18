namespace OuterWildsDualSense
{
	/// <summary>One interface sound of the game, with what was measured from its clip (tools/analyze_clip.py).</summary>
	internal struct UiSound
	{
		public AudioType Type;
		public AudioType Alias;    // a second sound type that plays the same clip and shares these settings (AudioType.None if none)
		public string Clip;
		public float Seconds;
		public float Volume;       // how loud the game plays it (the volume of its entry in the sound table)
		public bool ShipLog;       // played by the ship log, otherwise by the menu
		public float DefaultGain;

		public string Id { get { return "ui.snd." + Type; } }
	}

	/// <summary>
	/// Every menu and ship log sound that gets its own entry in the F1 menu. Almost all of them are clicks and chimes
	/// with nothing in the low band, so each becomes a short tick that follows the sound; strength, tone and length
	/// of the tick are set per sound.
	/// </summary>
	internal static class UiSounds
	{
		public static readonly UiSound[] All =
		{
			// Menu (title screen and pause menu): played by MenuAudioController.
			new UiSound { Type = AudioType.Menu_UpDown, Clip = "UI_Navigate_03", Seconds = 0.05f, Volume = 0.05f, DefaultGain = 15f },
			new UiSound { Type = AudioType.Menu_LeftRight, Clip = "UI_Tab_v2_02", Seconds = 0.25f, Volume = 0.3f, DefaultGain = 3f },
			new UiSound { Type = AudioType.Menu_SliderIncrement, Clip = "UI_Tab_v2_05", Seconds = 0.40f, Volume = 0.2f, DefaultGain = 3f },
			new UiSound { Type = AudioType.Menu_ChangeTab, Clip = "Marshmallow_Replace_01", Seconds = 0.29f, Volume = 0.2f, DefaultGain = 3f },
			new UiSound { Type = AudioType.Menu_Pause, Alias = AudioType.Menu_Unpause, Clip = "UI_Pause_v2_08", Seconds = 0.64f, Volume = 0.5f, DefaultGain = 2.5f },
			new UiSound { Type = AudioType.Menu_RebindKey, Clip = "Ignite_Marshmallow_01", Seconds = 0.82f, Volume = 0.5f, DefaultGain = 2.5f },
			new UiSound { Type = AudioType.Menu_ResetDefaults, Clip = "mallowpuff2", Seconds = 1.36f, Volume = 0.5f, DefaultGain = 2.5f },
			new UiSound { Type = AudioType.Menu_KonamiCode, Clip = "Ghost Shines Light", Seconds = 3.02f, Volume = 0.5f, DefaultGain = 2.5f },
			new UiSound { Type = AudioType.NonDiaUINegativeSFX, Clip = "Negative1", Seconds = 0.5f, Volume = 0.2f, DefaultGain = 3f },

			// Ship log: played by the ship log controller and its modes.
			new UiSound { Type = AudioType.ShipLogBootUp, Clip = "OW_SP_ActivateComputer_OneShot", Seconds = 2.00f, Volume = 1f, ShipLog = true, DefaultGain = 2.5f },
			new UiSound { Type = AudioType.ShipLogEnterDetectiveMode, Clip = "shiplog_switchmode_back", Seconds = 1.38f, Volume = 1f, ShipLog = true, DefaultGain = 2.5f },
			new UiSound { Type = AudioType.ShipLogEnterMapMode, Clip = "shiplog_switchmode_forward", Seconds = 1.38f, Volume = 1f, ShipLog = true, DefaultGain = 2.5f },
			new UiSound { Type = AudioType.ShipLogSelectPlanet, Clip = "shiplog_selectplanet2", Seconds = 1.38f, Volume = 1f, ShipLog = true, DefaultGain = 2.5f },
			new UiSound { Type = AudioType.ShipLogDeselectPlanet, Clip = "shiplog_deselectplanet", Seconds = 1.38f, Volume = 1f, ShipLog = true, DefaultGain = 2.5f },
			new UiSound { Type = AudioType.ShipLogMoveBetweenPlanets, Clip = "shiplog_movebetweenplanets", Seconds = 1.38f, Volume = 0.5f, ShipLog = true, DefaultGain = 3f },
			new UiSound { Type = AudioType.ShipLogMoveBetweenEntries, Clip = "shiplog_highlight", Seconds = 1.38f, Volume = 0.3f, ShipLog = true, DefaultGain = 3f },
			new UiSound { Type = AudioType.ShipLogHighlightEntry, Clip = "shiplog_highlight2", Seconds = 0.18f, Volume = 1f, ShipLog = true, DefaultGain = 3f },
			new UiSound { Type = AudioType.ShipLogSelectEntry, Clip = "shiplog_misc1", Seconds = 1.38f, Volume = 1f, ShipLog = true, DefaultGain = 3f },
			new UiSound { Type = AudioType.ShipLogDeselectEntry, Clip = "shiplog_misc4", Seconds = 0.25f, Volume = 1f, ShipLog = true, DefaultGain = 3f },
			new UiSound { Type = AudioType.ShipLogRevealEntry, Clip = "shiplog_newentry3_softer", Seconds = 1.38f, Volume = 0.5f, ShipLog = true, DefaultGain = 2.5f },
			new UiSound { Type = AudioType.ShipLogMarkLocation, Clip = "OW_PR_LockOn", Seconds = 0.84f, Volume = 0.5f, ShipLog = true, DefaultGain = 2.5f },
			new UiSound { Type = AudioType.ShipLogUnmarkLocation, Clip = "OW_PR_LockOff", Seconds = 1.00f, Volume = 0.5f, ShipLog = true, DefaultGain = 2.5f },
		};

		public static bool TryGet(AudioType type, out UiSound sound)
		{
			foreach (var s in All)
				if (s.Type == type || (s.Alias != AudioType.None && s.Alias == type)) { sound = s; return true; }
			sound = default(UiSound);
			return false;
		}
	}
}
