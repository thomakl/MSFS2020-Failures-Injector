﻿using Microsoft.FlightSimulator.SimConnect;
using Serilog;
using SimConModels.SimVar;

namespace SimConModels
{
    public class SimVarLists
    {
        private static readonly SimVarLists instance = new();

        private List<SimVarModel>? SimVarDataList;
        private List<SimVarModel>? SimVarFailableList;
        private List<SimVarModel>? SimVarFailuresList;

        private int flyTime = 0;

        private SimVarLists()
        {

        }

        public void LoadDataList()
        {
            Log.Logger.Information("Loading data list");
            SimVarDataList = SQLSimVar.LoadDataList();
            FillSimVarEnums(SimVarDataList);
            Log.Logger.Information("Data list loaded");
            SimCon.GetSimCon().RegisterList(SimVarDataList);
        }

        public void LoadFailableList(int presetID)
        {
            SimVarFailableList = SQLSimVar.LoadFailableSimVarsList(presetID, true);
            FillSimVarEnums(SimVarFailableList);
        }

        public void RandomizeFailures()
        {
            Log.Logger.Information("Randomizing failures");
            SimVarFailuresList = new();
            Random rnd = new();
            LoadFailableList(SQLOptions.LoadOptionValueInt("PresetID"));
            flyTime = 0;

            PresetModel preset = SQLPresets.LoadPreset(SQLOptions.LoadOptionValueInt("PresetID"));

            foreach (var sv in SimVarFailableList!)
            {
                if (sv.IsFailable && rnd.Next(1000) < sv.FailPercent)
                {
                    if (sv.IsComplete)
                        sv.FailureValue = 1;

                    if (sv.IsLeak)
                        sv.FailureValue = 0.000001 + (rnd.Next(0, 80) / 10000000);

                    bool cont;
                    do
                    {
                        cont = false;
                        sv.WhenFail = (WHEN_FAIL)rnd.Next(0, 4);
                        switch (sv.WhenFail)
                        {
                            case WHEN_FAIL.INSTANT:
                                {
                                    if (preset.InstantEnabled == 1)
                                        cont = true;
                                    break;
                                }
                            case WHEN_FAIL.ALT:
                                {
                                    if (preset.AltEnabled == 1)
                                        cont = true;
                                    break;
                                }
                            case WHEN_FAIL.TIME:
                                {
                                    if (preset.TimeEnabled == 1)
                                        cont = true;
                                    break;
                                }
                            case WHEN_FAIL.SPEED:
                                {
                                    if (preset.SpeedEnabled == 1)
                                        cont = true;
                                    break;
                                }
                            default:
                                {
                                    cont = true;
                                    break;
                                }
                        }
                    } while (!cont);

                    switch (sv.WhenFail)
                    {
                        case WHEN_FAIL.ALT:
                            {
                                sv.FailureAlt = rnd.Next(preset.AltMin, preset.AltMax);
                                break;
                            }
                        case WHEN_FAIL.TIME:
                            {
                                sv.FailureTime = rnd.Next(preset.TimeMin, preset.TimeMax);
                                break;
                            }
                        case WHEN_FAIL.SPEED:
                            {
                                sv.FailureSpeed = rnd.Next(preset.SpeedMin, preset.SpeedMax);
                                break;
                            }
                            /*case WHEN_FAIL.INSTANT:
                                {
                                    sv.FailureTime = 10;
                                    sv.WhenFail = WHEN_FAIL.TIME;
                                    break;
                                }*/
                    }

                    SimVarFailuresList.Add(sv);
                    Log.Logger.Information("Adding to failures: " + sv.SimVarName);
                }
            }

            SimCon.GetSimCon().RegisterList(SimVarFailuresList);
            Log.Logger.Information("Failures randomized");
        }

        public void SetFailures()
        {
            foreach (var sv in SimVarFailuresList!)
            {
                if (!sv.Failed)
                {
                    switch (sv.WhenFail)
                    {
                        case WHEN_FAIL.INSTANT:
                            {
                                if (flyTime > 0)
                                    SetFail(sv);
                                break;
                            }
                        case WHEN_FAIL.ALT:
                            {
                                if (SimVarDataList!.First(x => x.SimVariable == "PLANE ALTITUDE").Value >= sv.FailureAlt)
                                    SetFail(sv);
                                break;
                            }
                        case WHEN_FAIL.TIME:
                            {
                                if (flyTime >= sv.FailureTime)
                                    SetFail(sv);
                                break;
                            }
                        case WHEN_FAIL.SPEED:
                            {
                                if (SimVarDataList!.First(x => x.SimVariable == "GROUND VELOCITY").Value >= sv.FailureSpeed)
                                    SetFail(sv);
                                break;
                            }
                    }
                }
            }
        }

        private static void SetFail(SimVarModel simVarModel)
        {
            try
            {
                if (simVarModel.IsStuck)
                {
                    if (!simVarModel.Started)
                    {
                        simVarModel.FailureValue = simVarModel.Value;
                        simVarModel.Started = true;
                    }
                    simVarModel.Value = simVarModel.FailureValue;
                }
                else if (simVarModel.IsLeak)
                {
                    simVarModel.Value -= simVarModel.FailureValue;
                }
                else if (simVarModel.IsComplete)
                {
                    if (simVarModel.IsEvent)
                    {
                        SimCon.GetSimCon().GetSimConnect().MapClientEventToSimEvent(simVarModel.eEvent, simVarModel.SimVariable);
                        SimCon.GetSimCon().GetSimConnect().
                            TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, simVarModel.eEvent, 0, GROUP.ID_PRIORITY_STANDARD, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
                    }
                    else
                        simVarModel.Value = simVarModel.FailureValue;

                    simVarModel.Failed = true;
                }


                if (!simVarModel.IsEvent)
                    SimCon.GetSimCon().GetSimConnect().SetDataOnSimObject(simVarModel.eDef, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, simVarModel.Value);
            }
            catch (Exception ex)
            {
                Log.Logger.Error("SetFail");
                Log.Logger.Error(ex, ex.StackTrace);
            }
        }

        public void AddFlyTime()
        {
            flyTime++;
        }

        private static void FillSimVarEnums(List<SimVarModel> list)
        {
            foreach (SimVarModel simVarModel in list)
            {
                simVarModel.FillEnums();
            }
        }

        public static SimVarLists GetSimVarLists() => instance;

        public List<SimVarModel> GetDataList() => SimVarDataList!;

        public List<SimVarModel> GetFailableList() => SimVarFailableList!;

        public List<SimVarModel> GetFailuresList() => SimVarFailuresList!;
    }
}
