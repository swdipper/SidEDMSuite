﻿using System;
using System.Collections;

using NationalInstruments.DAQmx;

using DAQ.Pattern;
using System.Runtime.Remoting;

namespace DAQ.HAL
{
    /// <summary>
    /// This is the specific hardware that the edm machine has. This class conforms
    /// to the Hardware interface.
    /// </summary>
    public class RainbowHardware : DAQ.HAL.Hardware
    {

        public RainbowHardware()
        {

            // add the boards
            Boards.Add("daq", "/PXI1Slot4");
            Boards.Add("TCLBoard", "/PXI1Slot6");
            string daqBoard = (string)Boards["daq"];
            string TCLBoard = (string)Boards["TCLBoard"];

            // add things to the info
            // the analog triggers
            Info.Add("analogTrigger0", (string)Boards["daq"] + "/PFI1");
            Info.Add("analogTrigger1", (string)Boards["daq"] + "/PFI2");
            Info.Add("sourceToDetect", 0.4);
            Info.Add("moleculeMass", 100.0);
            Info.Add("phaseLockControlMethod", "synth");
            Info.Add("PGClockLine", daqBoard + "/PFI4");
            Info.Add("PatternGeneratorBoard", daqBoard);
            Info.Add("PGType", "integrated");
            Info.Add("PGClockCounter", "/ctr0");

            //TCL Lockable lasers
            Info.Add("TCLLockableLasers", new string[] { "laser","laser2","laser4"});
            Info.Add("TCLPhotodiodes", new string[] { "cavityRampMonitor", "master", "p1", "p2","p4"});// THE FIRST TWO MUST BE CAVITY AND MASTER PHOTODIODE!!!!
            Info.Add("TCL_Slave_Voltage_Limit_Upper", 2.0); //volts: Laser control
            Info.Add("TCL_Slave_Voltage_Limit_Lower", -2.0); //volts: Laser control
            Info.Add("TCL_Default_Gain", -0.01);
            Info.Add("TCL_Default_VoltageToLaser", 0.0);
            Info.Add("TCL_MAX_INPUT_VOLTAGE", 10.0);
            // Some matching up for TCL
            Info.Add("laser", "p1");
            Info.Add("laser2", "p2");
            Info.Add("laser4", "p4");
            Info.Add("TCLTrigger", TCLBoard + "/PFI0");

            // YAG laser
            yag = new BrilliantLaser("ASRL3::INSTR");

            // add the GPIB instruments
 
            // map the digital channels
            AddDigitalOutputChannel("valve", daqBoard, 0, 0);
            AddDigitalOutputChannel("flash", daqBoard, 0, 1);
            AddDigitalOutputChannel("q", daqBoard, 0, 2);
            AddDigitalOutputChannel("detector", daqBoard, 0, 3);
            AddDigitalOutputChannel("detectorprime", daqBoard, 0, 4); // this trigger is for switch scanning
            AddDigitalOutputChannel("aom", daqBoard, 0, 5); // this trigger is for switch scanning

            // map the analog input channels
            AddAnalogInputChannel("pmt", daqBoard + "/ai1", AITerminalConfiguration.Nrse);
            AddAnalogInputChannel("norm", daqBoard + "/ai0", AITerminalConfiguration.Nrse);
            AddAnalogInputChannel("iodine", daqBoard + "/ai2", AITerminalConfiguration.Nrse);
            AddAnalogInputChannel("cavity", daqBoard + "/ai3", AITerminalConfiguration.Nrse);

            AddAnalogInputChannel("master", TCLBoard + "/ai1", AITerminalConfiguration.Rse);
            AddAnalogInputChannel("p1", TCLBoard + "/ai3", AITerminalConfiguration.Rse);
            AddAnalogInputChannel("cavityRampMonitor", TCLBoard + "/ai2", AITerminalConfiguration.Rse);
            AddAnalogInputChannel("p4", TCLBoard + "/ai0", AITerminalConfiguration.Rse);
            AddAnalogInputChannel("p2", TCLBoard + "/ai5", AITerminalConfiguration.Rse);

   
            // map the analog output channels
            AddAnalogOutputChannel("laser", TCLBoard + "/ao0");
            AddAnalogOutputChannel("laser4", TCLBoard + "/ao1");
            AddAnalogOutputChannel("laser2", daqBoard + "/ao1");
            AddAnalogOutputChannel("laser3", daqBoard + "/ao2");
            AddAnalogOutputChannel("rampfb", daqBoard + "/ao0");
         
            //Transfer Cavity Lock
            //AddAnalogOutputChannel("cavity", daqBoard + "/ao1");
            //Info.Add("analogTrigger2", (string)Boards["daq"] + "/PFI0");
            //Info.Add("analogTrigger3", (string)Boards["daq"] + "/PFI3");
            //AddDigitalOutputChannel("scanTrigger", daqBoard, 0, 6);
            //AddAnalogInputChannel("slavepd", daqBoard + "/ai4", AITerminalConfiguration.Nrse);
            //AddAnalogInputChannel("masterpd", daqBoard + "/ai5", AITerminalConfiguration.Nrse);


             // map the counter channels
            //AddCounterChannel("phaseLockOscillator", daqBoard + "/ctr7");
            //AddCounterChannel("phaseLockReference", daqBoard + "/pfi10");
            //AddCounterChannel("northLeakage", counterBoard + "/ctr0");
            //AddCounterChannel("southLeakage", counterBoard + "/ctr1");

        }

        public override void ConnectApplications()
        {
            // ask the remoting system for access to TCL2012
            Type t = Type.GetType("TransferCavityLock2012.Controller, TransferCavityLock");
            RemotingConfiguration.RegisterWellKnownClientType(t, "tcp://localhost:1190/controller.rem");
        }

    }
}
