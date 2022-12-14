using OpenBveApi.Runtime;
using OpenBveApi.Colors;
using System;
using System.Threading.Tasks;

namespace Plugin {
    /// <summary>The interface to be implemented by the plugin.</summary>
    public partial class Plugin : IRuntime {
        internal static int[] Panel = null;
        
        internal static bool DoorOpened;
        internal static bool DoorOpened2 = true;
        internal static Util.LRVType LRVGeneration;
        internal static int LastBrakeNotch = 5;
        internal static bool LockTreadBrake = false;
        internal static VehicleSpecs specs;
        internal static string Language = "en-us";
        internal static SpeedMode CurrentSpeedMode = SpeedMode.Normal;
        internal int SpeedLimit = 60;
        private Version Version = new Version("2.5.0");
        private int CurrentRoute = 0;
        private double currentSpeed;
        private bool Crashed;
        private bool iSPSDoorLock;
        private bool DoorBrake;
        private bool UpdateChecked;
        private bool Ready = false;
        private CameraManager CameraManager = new CameraManager();
        private PAManager PAManager = new PAManager();
        private IndicatorLight DirectionLight = IndicatorLight.None;

        /// <summary>Is called when the plugin is loaded.</summary>
        public bool Load(LoadProperties prop) {
            /* Get LRV Generation with GetLRVGen method/function in Misc.cs */
            LRVGeneration = Util.GetLRVGen(prop.TrainFolder);
            /* Initialize MessageManager, so we can print message on the top-left screen later. */
            MessageManager.Initialise(prop.AddMessage);
            /* Initialize an empty array with 256 elements, used for Panel Indicator. */
            Panel = new int[256];
            /* Initialize Sound, used to play sound later on. */
            SoundManager.Initialise(prop.PlaySound, prop.PlayCarSound, 256);
            prop.Panel = Panel;
            prop.FailureReason = "LRV plugin failed to load, some functions will be unavailable.";
            prop.AISupport = AISupport.Basic;

            return Config.LoadConfig(prop, Util.LRVType.P4);
        }

        public void Unload() {
        }

        /// <summary>Is called after loading to inform the plugin about the specifications of the train.</summary>
        public void SetVehicleSpecs(VehicleSpecs specs) {
            Plugin.specs = specs;
            SetPanel(PanelIndices.FirstCarNumber, Util.CarNumPanel(Config.carNum1));
            SetPanel(PanelIndices.SecondCarNumber, Util.CarNumPanel(Config.carNum2));
        }

        /// <summary>Is called when the plugin should initialize, reinitialize or jumping stations.</summary>
        public void Initialize(InitializationModes mode) {
            PAManager.Load();
            ResetLRV(ResetType.JumpStation);
        }

        /// <summary>This is called every frame. If you have 60fps, then this method is called 60 times in 1 second</summary>
        public void Elapse(ElapseData data) {
            /* Get system language, used for displaying train settings dialog later. */
            Ready = true;
            Language = data.CurrentLanguageCode;
            currentSpeed = data.Vehicle.Speed.KilometersPerHour;
            Panel[PanelIndices.DestinationLED] = CurrentRoute;

            CameraManager.Update(data);
            StationManager.Update(data);
            ReporterLED.Update(data);
            PAManager.Loop();

            /* Check for update on the first frame */
            if (!UpdateChecked) {
                if (!Config.ignoreUpdate) {
                    Task.Run(() => Update.checkUpdate(Language, Version));
                }
                UpdateChecked = true;
            }

            /* Lock the door above 2 km/h */
            if (currentSpeed > 2 && Config.doorlockEnabled) {
                data.DoorInterlockState = DoorInterlockStates.Locked;
            } else {
                data.DoorInterlockState = DoorInterlockStates.Unlocked;
            }

            if (currentSpeed > SpeedLimit - 5) {
                Panel[PanelIndices.iSPSOverSpeed] = 1;
            } else {
                Panel[PanelIndices.iSPSOverSpeed] = 0;
            }

            if ((DoorBrake && Config.doorApplyBrake) || (iSPSDoorLock && Config.iSPSEnabled)) {
                data.Handles.PowerNotch = 0;
                data.Handles.BrakeNotch = specs.B67Notch;
            }

            if (DirectionLight == IndicatorLight.Left) {
                SetPanel(PanelIndices.Indicator, 1);
            } else if (DirectionLight == IndicatorLight.Right) {
                SetPanel(PanelIndices.Indicator, 2);
            } else if (DirectionLight == IndicatorLight.Both) {
                SetPanel(PanelIndices.Indicator, 3);
            } else {
                SetPanel(PanelIndices.Indicator, 0);
            }

            if (Config.tutorialMode) {
                if (Language.StartsWith("zh")) {
                    Panel[PanelIndices.TutorialModeChin] = 1;
                } else {
                    Panel[PanelIndices.TutorialModeEng] = 1;
                }
            } else {
                Panel[PanelIndices.TutorialModeChin] = 0;
                Panel[PanelIndices.TutorialModeEng] = 0;
            }

            /* Clamp the power notch to P1 on slow mode. */
            if (CurrentSpeedMode == SpeedMode.Slow && data.Handles.PowerNotch > 1) {
                data.Handles.PowerNotch = 1;
            } else if (CurrentSpeedMode != SpeedMode.Fast && data.Handles.PowerNotch == specs.PowerNotches) {
                /* We reserved the last notch for the fast (aka "Elephant") mode. If the current speed mode is not fast and current power notch is the last notch: Clamp it to last notch - 1 */
                data.Handles.PowerNotch = specs.PowerNotches - 1;
            }

            if (data.PrecedingVehicle != null) {
                if (Config.crashEnabled && Crashed == false && data.PrecedingVehicle.Distance < 0.1 && data.PrecedingVehicle.Distance > -4) {
                    /* Crash Sounds */
                    SoundManager.Play(SoundIndices.Crash, 1.0, 1.0, false);

                    if (Math.Abs(data.PrecedingVehicle.Speed.KilometersPerHour - currentSpeed) > 10) {
                        Panel[213] = 1;
                        Panel[PanelIndices.HeadLight] = 1;

                        if (Math.Abs(data.PrecedingVehicle.Speed.KilometersPerHour - currentSpeed) > 17) {
                            Panel[PanelIndices.SpeedometerLight] = 1;
                            DirectionLight = IndicatorLight.None;
                            Panel[PanelIndices.Indicator] = 0;
                        }
                    }
                    Crashed = true;
                }
            }

            if (StationManager.approachingStation && currentSpeed < 0.1 && DoorOpened2 == false) {
                Panel[202] = 1;
                /* If the reverser is Forward */
                if (data.Handles.Reverser == 1) {
                    iSPSDoorLock = true;
                } else {
                    if(Config.allowReversingInStations) iSPSDoorLock = false;
                }
            }

            if (currentSpeed > 10 && Panel[202] == 1) {
                Panel[202] = 0;
            }

            /* Turn signal sound in cab */
            if (DirectionLight != IndicatorLight.None) {
                if (CameraManager.isInCab()) {
                    SoundManager.Play(SoundIndices.CabDirIndicator, 1.0, 1.0, true);
                } else {
                    SoundManager.Stop(SoundIndices.CabDirIndicator);
                }
            } else {
                SoundManager.Stop(SoundIndices.CabDirIndicator);
            }

            SetPanel(PanelIndices.TrainStatus, Config.trainStatus);
        }

        public void SetReverser(int reverser) {
        }

        public void SetPower(int notch) {
            if(notch % 2 == 0 && CameraManager.isInCab() && Ready) {
                SoundManager.Play(SoundIndices.drvHandleClick, 1.0, 1.0, false);
            }
        }

        public void SetBrake(int notch) {
            if (LastBrakeNotch == 0 && notch > 0 && currentSpeed > 15) {
                SoundManager.PlayAllCar(SoundIndices.StartBrake, 1.0, 1.0, false);
            }

            if (notch % 2 == 0 && CameraManager.isInCab() && Ready) {
                SoundManager.Play(SoundIndices.drvHandleClick, 1.0, 1.0, false);
            }

            LastBrakeNotch = notch;
        }

        /// <summary>Is called when a virtual key is pressed.</summary>
        public void KeyDown(VirtualKeys key) {
            VirtualKeys virtualKey = key;

            switch (virtualKey) {
                /* GearDown = Ctrl + G */
                case VirtualKeys.GearDown:
                    ConfigForm.LaunchForm();
                    break;
                case VirtualKeys.A1:
                    ResetLRV(0);
                    break;
                case VirtualKeys.A2:
                    if (CameraManager.isInCab()) SoundManager.Play(SoundIndices.Click, 1.0, 1.0, false);
                    if ((int)CurrentSpeedMode == 2) CurrentSpeedMode = SpeedMode.Normal;
                    else CurrentSpeedMode++; Panel[PanelIndices.SpeedModeSwitch] = (int)CurrentSpeedMode;
                    break;
                case VirtualKeys.B1:
                    Panel[PanelIndices.TreadBrake] = (Panel[PanelIndices.TreadBrake] + 1) % 2;
                    break;
                case VirtualKeys.D:
                    ToggleDirLight(IndicatorLight.Left);
                    break;
                case VirtualKeys.E:
                    ToggleDirLight(IndicatorLight.Right);
                    break;
                case VirtualKeys.F:
                    CurrentRoute++;
                    break;
                case VirtualKeys.G:
                    Panel[PanelIndices.Digit1]++;
                    break;
                case VirtualKeys.H:
                    Panel[PanelIndices.Digit2]++;
                    break;
                case VirtualKeys.I:
                    Panel[PanelIndices.Digit3]++;
                    break;
                case VirtualKeys.L:
                    Panel[PanelIndices.SpeedometerLight] ^= 1;
                    SoundManager.PlayCabClickSound(CameraManager.GetMode());
                    break;
                case VirtualKeys.S:
                    Panel[PanelIndices.CabDoor] ^= 1;
                    break;
                case VirtualKeys.K:
                    Panel[PanelIndices.HeadLight] ^= 1;
                    SoundManager.PlayCabClickSound(CameraManager.GetMode());
                    break;
                case VirtualKeys.J:
                    Panel[PanelIndices.LightToggle] ^= 1;
                    SoundManager.PlayCabClickSound(CameraManager.GetMode());
                    break;
                case VirtualKeys.WiperSpeedUp:
                    if (Panel[PanelIndices.WiperMode] + 1 > 4) {
                        Panel[PanelIndices.WiperMode] = 0;
                    } else {
                        Panel[PanelIndices.WiperMode]++;
                    }
                    SoundManager.PlayCabClickSound(CameraManager.GetMode());
                    break;
                case VirtualKeys.WiperSpeedDown:
                    if (Panel[PanelIndices.WiperMode] - 1 < 0) {
                        Panel[PanelIndices.WiperMode] = 5;
                    } else {
                        Panel[PanelIndices.WiperMode]--;
                    }
                    SoundManager.PlayCabClickSound(CameraManager.GetMode());
                    break;
                case VirtualKeys.LeftDoors:
                    PAManager.KeyDown();
                    if (DoorOpened && Config.mtrBeeping) {
                        if (SoundManager.IsPlaying(SoundIndices.MTRBeep)) {
                            SoundManager.Stop(SoundIndices.MTRBeep);
                        } else {
                            SoundManager.PlayCar(SoundIndices.MTRBeep, 2.0, 1.0, false, 0);
                            if (specs.Cars == 2) SoundManager.PlayCar(SoundIndices.MTRBeep, 2.0, 1.0, false, 1);
                        }
                    }
                    break;
                case VirtualKeys.MainBreaker:
                    ToggleDirLight(IndicatorLight.Both);
                    break;
            }
        }

        /// <summary>Is called when a virtual key is released.</summary>
        public void KeyUp(VirtualKeys key) {
        }

        public void HornBlow(HornTypes type) {
        }

        /// <summary>Is called when the state of the doors changes.</summary>
        public void DoorChange(DoorStates oldState, DoorStates newState) {
            /* Door is opened */
            if (oldState == DoorStates.None & newState != DoorStates.None) {
                DoorOpened = true;
                DoorOpened2 = true;
                DoorBrake = true;
                /* Door is closed */
            } else if (oldState != DoorStates.None & newState == DoorStates.None) {
                DoorOpened = false;
                Panel[204] = 0;
                StationManager.approachingStation = false;
                iSPSDoorLock = false;
                DoorBrake = false;
            }
        }
        public void SetSignal(SignalData[] signal) {
        }

        /// <summary>Is called when the train passes a beacon.</summary>
        /// <param name="beacon">The beacon data.</param>
        public void SetBeacon(BeaconData beacon) {
            switch (beacon.Type) {
                case BeaconIndices.SpeedLimit:
                    if (beacon.Optional > 0) SpeedLimit = beacon.Optional;
                    break;
                case BeaconIndices.IndicatorLeft:
                    if (StationManager.AIEnabled)
                        if (beacon.Optional == 1) {
                            if (DirectionLight != IndicatorLight.Left) KeyDown(VirtualKeys.D);
                        } else {
                            if (DirectionLight != IndicatorLight.None) KeyDown(VirtualKeys.D);
                        }
                    break;
                case BeaconIndices.IndicatorRight:
	                if (StationManager.AIEnabled) {
		                if (beacon.Optional == 1 && DirectionLight != IndicatorLight.Right) {
			                KeyDown(VirtualKeys.E);
		                } else if (DirectionLight != IndicatorLight.None) {
			                KeyDown(VirtualKeys.E);
		                }
	                }
	                break;
            }
        }

        public static void SetPanel(int index, int val) {
            Panel[index] = val;
        }

        public void PerformAI(AIData data) {
            StationManager.ResetAITimer();
        }

        internal void ToggleDirLight(IndicatorLight direction) {
            if (direction == IndicatorLight.Left) {
                if (DirectionLight == direction) {
                    DirectionLight = IndicatorLight.None;
                } else {
                    DirectionLight = IndicatorLight.Left;
                }
            } else if (direction == IndicatorLight.Right) {
                if (DirectionLight == direction) {
                    DirectionLight = IndicatorLight.None;
                } else {
                    DirectionLight = IndicatorLight.Right;
                }
            } else if (direction == IndicatorLight.Both) {
                if (DirectionLight == direction) {
                    DirectionLight = IndicatorLight.None;
                    Panel[PanelIndices.DirBoth] = 0;
                } else {
                    /* Can't be used in conjunction */
                    if (DirectionLight == IndicatorLight.Left || DirectionLight == IndicatorLight.Right) {
                        MessageManager.PrintMessage(Messages.getTranslation("gameMsg.turnOffTurnSignal"), MessageColor.Orange, 5.0);
                        return;
                    } else {
	                    Panel[PanelIndices.DirBoth] = 1;
	                    DirectionLight = IndicatorLight.Both;
                    }
                }

            }
            /* If our current camera mode is in cab(F1), play the click sound as it should only be heard in cab. */
            if (CameraManager.isInCab()) {
                SoundManager.Play(SoundIndices.Click, 1.0, 1.0, false);
            }
        }

        internal void ResetLRV(ResetType mode) {
            if (mode == ResetType.JumpStation) {
                DoorOpened2 = true;
                DoorBrake = true;
                iSPSDoorLock = false;
                if (Crashed) {
                    Crashed = false;
                    Panel[PanelIndices.SpeedometerLight] = 0;
                    Panel[PanelIndices.HeadLight] = 0;
                    Panel[213] = 0;
                }
            }
            StationManager.AIEnabled = false;
            StationManager.approachingStation = false;
            iSPSDoorLock = false;
            DoorBrake = false;
            Panel[202] = 0;
            Panel[203] = 0;
        }

        internal static void ChangeCarNumber(int car, int states) {
            if (car == 1) {
                Panel[PanelIndices.FirstCarNumber] = states;
            } else {
                Panel[PanelIndices.SecondCarNumber] = states;
            }
        }
    }

    enum ResetType {
        JumpStation,
        ManualReset,
    }

    enum IndicatorLight {
        Left,
        Right,
        Both,
        None
    }

    enum SpeedMode {
        Normal,
        Fast,
        Slow
    }
}