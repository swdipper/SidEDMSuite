﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Lifetime;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Forms;
using System.Text;
using System.Diagnostics;
using System.Reflection;
using Microsoft.CSharp;

using DAQ;
using DAQ.Environment;
using DAQ.HAL;
using DAQ.Analog;
using Data;
using Data.Scans;


using IMAQ;

using System.Runtime.InteropServices;
using System.CodeDom;
using System.CodeDom.Compiler;

using NationalInstruments;
using NationalInstruments.DAQmx;
using NationalInstruments.UI;
using NationalInstruments.UI.WindowsForms;
using System.Runtime.Serialization.Formatters.Binary;

namespace MOTMaster
{
    /// <summary>
    /// Here's MOTMaster's controller.
    /// 
    /// Gets a MOTMasterScript (a script contaning a series of commands like "addEdge" for both digital and analog)
    /// from user (either remotely or via UI), compiles it, builds a pattern and sends it
    /// to hardware.
    /// </summary>
    public class Controller : MarshalByRefObject
    {

        #region Class members

        private static string
            motMasterPath = (string)Environs.FileSystem.Paths["MOTMasterEXEPath"] + "//MotMaster.exe";
        private static string
            daqPath = (string)Environs.FileSystem.Paths["daqDLLPath"];
        private static string
            scriptListPath = (string)Environs.FileSystem.Paths["scriptListPath"];
        private static string
            motMasterDataPath = (string)Environs.FileSystem.Paths["MOTMasterDataPath"];
        private static string
            cameraAttributesPath = (string)Environs.FileSystem.Paths["CameraAttributesPath"];
        private static string
            hardwareClassPath = (string)Environs.FileSystem.Paths["HardwareClassPath"];
        private const int
            pgClockFrequency = 10000;
        private const int
            apgClockFrequency = 10000;

        ControllerWindow controllerWindow;

        DAQMxPatternGenerator pg;
        DAQMxAnalogPatternGenerator apg;

        CameraControllable camera;
        TranslationStageControllable tstage;
        ExperimentReportable experimentReporter;

        MMDataIOHelper ioHelper;

        #endregion

        #region Initialisation

        // without this method, any remote connections to this object will time out after
        // five minutes of inactivity.
        // It just overrides the lifetime lease system completely.
        public override Object InitializeLifetimeService()
        {
            return null;
        }

        public void StartApplication()
        {

            controllerWindow = new ControllerWindow();
            controllerWindow.controller = this;

            pg = new DAQMxPatternGenerator((string)Environs.Hardware.Boards["multiDAQ"]);
            apg = new DAQMxAnalogPatternGenerator();

            camera = (CameraControllable)Activator.GetObject(typeof(CameraControllable),
                "tcp://localhost:1172/controller.rem");

            tstage = (TranslationStageControllable)Activator.GetObject(typeof(CameraControllable),
                "tcp://localhost:1172/controller.rem");

            experimentReporter = (ExperimentReportable)Activator.GetObject(typeof(ExperimentReportable),
                "tcp://localhost:1172/controller.rem");

            
            ioHelper = new MMDataIOHelper(motMasterDataPath, 
                    (string)Environs.Hardware.GetInfo("Element"));

            ScriptLookupAndDisplay();

            Application.Run(controllerWindow);

        }

        #endregion

        #region Hardware control methods


        private void run(MOTMasterSequence sequence)
        {
            apg.OutputPatternAndWait(sequence.AnalogPattern.Pattern);
            pg.OutputPattern(sequence.DigitalPattern.Pattern);
        }

        private void initializeHardware(MOTMasterSequence sequence)
        {
            pg.Configure(pgClockFrequency, false, true, true, sequence.DigitalPattern.Pattern.Length, true, false);
            apg.Configure(sequence.AnalogPattern, apgClockFrequency);
        }

        private void releaseHardwareAndClearDigitalPattern(MOTMasterSequence sequence)
        {
            sequence.DigitalPattern.Clear(); //No clearing required for analog (I think).
            pg.StopPattern();
            apg.StopPattern();
        }

        #endregion

        #region Housekeeping on UI

        /// - MOTMaster looks in a folder ("scriptListPath") for all classes. 
        ///  Then displays the list in a combo box.
        /// 
        /// - These classes contain an implementation of a "MOTMasterScript". This contains the information 
        /// about the patterns.
        public void ScriptLookupAndDisplay()
        {
            string[] s = scriptLookup();
            displayScripts(s);
        }
        private string[] scriptLookup()
        {
            string[] scriptList = Directory.GetFiles(scriptListPath, "*.cs");
            return scriptList;
        }
        private void displayScripts(string[] s)
        {
            controllerWindow.FillScriptComboBox(s);
        }

        #endregion

        #region RUN RUN RUN (public & remotable stuff)

        /// <summary>
        /// This is the guts of MOTMaster.
        /// 
        /// - MOTMaster initializes the hardware, faffs a little to prepare the patterns in the 
        /// builders (e.g. calls "BuildPattern"), and sends the pattern to Hardware.
        /// 
        /// -Note that the analog stuff needs a trigger to start!!!! Make sure one of your digital lines is reserved 
        /// for triggering the analog pattern.
        /// 
        /// - Once the experiment is finished, MM releases the hardware.
        /// 
        /// - MOTMaster also saves the data to a .zip. This includes: the original MOTMasterScript (.cs), a text file
        /// with the parameters in it (IF DIFFERENT FROM THE VALUES IN .cs, THE PARAMETERS IN THE TEXT FILE ARE THE
        /// CORRECT VALUES!), another text file with the camera attributes, yet another file (entitled hardware report)
        ///  which contains the values set by the Hardware controller at the start of the run, and a .png file(s) containing the final image(s).
        /// 
        /// -There are 2 ways of using "Run". Run(null) uses the parameters given in the script (.cs file).
        ///  Run(Dictionary<>) compiles the .cs file but then replaces values in the dictionary. This is to allow
        ///  the user to inject values after compilation but before sending to hardware. By doing this,
        ///  the user can scan parameters using a python script, for example.
        ///  If you call Run(), MOTMaster immediately checks to see if you're running a fresh script 
        ///  or whether you're re-running an old one. In the former case Run(null) is called. In the latter,
        ///  MOTMaster will fetch the dictionary used in the old experiment and use it as the
        ///  argument for Run(Dictionary<>).        ///  
        /// 
        
        /// </summary>
        private bool saveEnable = true;
        public void SaveToggle(System.Boolean value)
        {
            saveEnable = value;
            controllerWindow.SetSaveCheckBox(value);
        }
        private int batchNumber = 0;
        public void SetBatchNumber(Int32 number)
        {
            batchNumber = number;
            controllerWindow.WriteToSaveBatchTextBox(number);  
        }
        private string scriptPath = "";
        public void SetScriptPath(String path)
        {
            scriptPath = path;
            controllerWindow.WriteToScriptPath(path);
        }
        private bool replicaRun = false;
        public void SetReplicaRunBool(System.Boolean value)
        {
            replicaRun = value;
        }
        private string dictionaryPath = "";
        public void SetDictionaryPath(String path)
        {
            dictionaryPath = path;
        }


        public void Run()
        {
            if (replicaRun)
            {
                Run(ioHelper.LoadDictionary(dictionaryPath));
            }
            else
            {
                Run(null);
            }
        }
        public void Run(Dictionary<String, Object> dict)
        {
            Stopwatch watch = new Stopwatch();
            MOTMasterScript script = prepareScript(scriptPath, dict);
            if (script != null)
            {
                MOTMasterSequence sequence = getSequenceFromScript(script);
               
                try
                {
                    prepareCameraControl();
                    Console.WriteLine("prepared Camera Control");
                    armTranslationStageForTimedMotion(script);

                    GrabImage((int)script.Parameters["NumberOfFrames"]);
                    Console.WriteLine("'grabbed image'");
                    buildPattern(sequence, (int)script.Parameters["PatternLength"]);
                    Console.WriteLine("built pattern");
                    waitUntilCameraIsReadyForAcquisition();

                    watch.Start();
                    Console.WriteLine("watch started");
                    runPattern(sequence);
                    Console.WriteLine("sequence finished");
                    watch.Stop();
                    Console.WriteLine("watch stopped");
                    //MessageBox.Show(watch.ElapsedMilliseconds.ToString());
                    if (saveEnable)
                    {
                        Console.WriteLine("waiting for camera acquisition to finish");

                        waitUntilCameraAquisitionIsDone();
                        Console.WriteLine("waited for camera acquisition to finish");
                        try
                        {
                            checkDataArrived();
                        }
                        catch (DataNotArrivedFromHardwareControllerException)
                        {
                            Console.WriteLine("Data not arrived from hardware controller");
                            return;
                        }
                        int[] ROIforImageProcessing = { (int)script.Parameters["ROIforImageProcessingx1"], (int)script.Parameters["ROIforImageProcessingx2"], (int)script.Parameters["ROIforImageProcessingy1"], (int)script.Parameters["ROIforImageProcessingy2"] };
                        processImages(ROIforImageProcessing);
                        Console.WriteLine("processed images");
                        Dictionary<String, Object> report = GetExperimentReport();
                        Console.WriteLine("got experiment report");
                        if (absImage == null) save(script, scriptPath, imageData, report);
                        else save(script, scriptPath, imageData, absImage, columnSum, rowSum, report);
                        Console.WriteLine("saved images");

                    }
                    else
                    {
                        Console.WriteLine("waiting for camera acquisition to finish");

                        waitUntilCameraAquisitionIsDone();
                        Console.WriteLine("waited for camera acquisition to finish");
                    }
                    
                    finishCameraControl();
                    Console.WriteLine("finished camera control");
                    disarmAndReturnTranslationStage();
                }
                catch (System.Net.Sockets.SocketException e)
                {
                    MessageBox.Show("CameraControllable not found. \n Is there a hardware controller running? \n \n" + e.Message, "Remoting Error");
                }
            }
            else
            {
                MessageBox.Show("Unable to load pattern. \n Check that the script file exists and that it compiled successfully");
            }
            
        }
        
        #endregion

        #region private stuff


        private void save(MOTMasterScript script, string pathToPattern, ushort[,] imageData, Dictionary<String, Object> report)
        {
            ioHelper.StoreRun(motMasterDataPath, controllerWindow.GetSaveBatchNumber(), pathToPattern, hardwareClassPath,
                script.Parameters, report, cameraAttributesPath, imageData);
        }
        private void save(MOTMasterScript script, string pathToPattern, ushort[][,] imageData, Dictionary<String, Object> report)
        {
            ioHelper.StoreRun(motMasterDataPath, controllerWindow.GetSaveBatchNumber(), pathToPattern, hardwareClassPath,
                script.Parameters, report, cameraAttributesPath, imageData);
        }
        private void save(MOTMasterScript script, string pathToPattern, ushort[][,] imageData, double[,] dividedImage, double[] columnSum,double[] rowSum, Dictionary<String, Object> report)
        {
            ioHelper.StoreRun(motMasterDataPath, controllerWindow.GetSaveBatchNumber(), pathToPattern, hardwareClassPath,
                script.Parameters, report, cameraAttributesPath, imageData, absImage, columnSum, rowSum);
        }
        private void runPattern(MOTMasterSequence sequence)
        {
            initializeHardware(sequence);
            run(sequence);
            releaseHardwareAndClearDigitalPattern(sequence);

        }

        private MOTMasterScript prepareScript(string pathToPattern, Dictionary<String, Object> dict)
        {
            MOTMasterScript script;
            CompilerResults results = compileFromFile(pathToPattern);
            if (results != null)
            {
                script = loadScriptFromDLL(results);
                if (dict != null)
                {
                    script.EditDictionary(dict);
                    
                }
                return script;
            }
            return null;
        }

        private void buildPattern(MOTMasterSequence sequence, int patternLength)
        {
            sequence.DigitalPattern.BuildPattern(patternLength);
            sequence.AnalogPattern.BuildPattern();
        }
       
        #endregion

        #region Compiler & Loading DLLs

        /// <summary>
        ///   /// - Once the user has selected a particular implementation of MOTMasterScript, 
        /// MOTMaster will compile it. Note: the dll is currently stored in a temp folder somewhere. 
        /// Its pathToPattern can be found in the CompilerResults.PathToAssembly). 
        /// This newly formed dll contain methods named GetDigitalPattern and GetAnalogPattern. 
        /// 
        /// - These are called by the script's "GetSequence". GetSequence always returns a 
        /// "MOTMasterSequence", which comprises a PatternBuilder32 and an AnalogPatternBuilder.
        /// </summary>

        private CompilerResults compileFromFile(string scriptPath)
        {
            CompilerParameters options = new CompilerParameters();

            options.ReferencedAssemblies.Add(motMasterPath);
            options.ReferencedAssemblies.Add(daqPath);

            TempFileCollection tempFiles = new TempFileCollection();
            tempFiles.KeepFiles = true;
            CompilerResults results = new CompilerResults(tempFiles);
            options.GenerateExecutable = false;                         //Creates .dll instead of .exe.
            CodeDomProvider codeProvider = new CSharpCodeProvider();
            options.TempFiles = tempFiles;
            try
            {
                results = codeProvider.CompileAssemblyFromFile(options, scriptPath);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                return null;
            }
            //controllerWindow.WriteToScriptPath(results.PathToAssembly);
            return results;
        }

        private MOTMasterScript loadScriptFromDLL(CompilerResults results)
        {
            object loadedInstance = new object();
            try
            {
                Assembly patternAssembly = Assembly.LoadFrom(results.PathToAssembly);
                foreach (Type type in patternAssembly.GetTypes())
                {
                    if (type.IsClass == true)
                    {
                        loadedInstance = Activator.CreateInstance(type);
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                return null;
            }
            return (MOTMasterScript)loadedInstance;
        }

        private MOTMasterSequence getSequenceFromScript(MOTMasterScript script)
        {
            MOTMasterSequence sequence = script.GetSequence();
            return sequence;
        }

        #endregion

        #region CameraControl

        /// <summary>
        /// - Camera control is run through the hardware controller. All MOTMaster knows 
        /// about it a function called "GrabImage(string cameraSettings)". If the camera attributes are 
        /// set so that it needs a trigger, MOTMaster will have to deliver that too.
        /// It'll expect a byte[,] or byte[][,] (if there are several images) as a return value.
        /// 
        /// -At the moment MOTMaster won't run without a camera nor with 
        /// more than one. In the long term, we might 
        /// want to fix this.
        /// </summary>
        /// 
        int nof;
        public void GrabImage(int numberOfFrames)
        {
            nof = numberOfFrames;
            Thread LLEThread = new Thread(new ThreadStart(grabImage));
            LLEThread.Start();

        }
        
        bool imagesRecieved = false;
        /*private byte[,] imageData;
        private void grabImage()
        {
            imagesRecieved = false;
            imageData = (byte[,])camera.GrabSingleImage(cameraAttributesPath);
            imagesRecieved = true;
        }*/
        private ushort[][,] imageData;
        private double[,] absImage;
        private double[] rowSum;
        private double[] columnSum;
        private void grabImage()
        {
            imagesRecieved = false;
            imageData = camera.GrabMultipleImages(cameraAttributesPath, nof);
            imagesRecieved = true;
        }
        private void processImages(int[] ROI)
        {
            int width = 1+ROI[3]-ROI[2];
            int height = 1+ ROI[1]-ROI[0];
                        
            int beamPixel; int cloudPixel; double absPixel; 
            
            absImage = new double[width,height];
            columnSum = new double[width];
            rowSum = new double[height];
            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j< height; j++)
                {
                    cloudPixel = (imageData[0][0 + i,0+ j] - imageData[2][0+i ,0+  j]);
                    beamPixel = (imageData[1][0+i,0+ j] - imageData[2][0+i, 0+j]);
                    if (beamPixel <= 0 || cloudPixel <=0) absPixel = 0.0;
                    else absPixel = -Math.Log( cloudPixel*1.0/ beamPixel);

                    absImage[i, j] = absPixel;
                    rowSum[j] = rowSum[j]+ absPixel;
                    columnSum[i] = columnSum[i]+ absPixel;
                }

            }
        }

        public class DataNotArrivedFromHardwareControllerException : Exception { };
        private bool waitUntilCameraAquisitionIsDone()
        {
            int numberofwaits = 0; 
            while (!imagesRecieved)
            { 
                Thread.Sleep(10);
                numberofwaits += 1;
                if(numberofwaits % 300 ==0)
                { Console.WriteLine(numberofwaits); }
            }
            Console.WriteLine(numberofwaits);
            return true;
        }
        private bool waitUntilCameraIsReadyForAcquisition()
        {
            while (!camera.IsReadyForAcquisition())
            { 
            Thread.Sleep(10);
            
            }
            return true;
        }
        private void prepareCameraControl()
        {
            camera.PrepareRemoteCameraControl();
        }
        private void finishCameraControl()
        {
            camera.FinishRemoteCameraControl();
        }

        private void checkDataArrived()
        {
            if (imageData == null)
            {
                MessageBox.Show("No data. Something's Wrong.");
                throw new DataNotArrivedFromHardwareControllerException();
            }
        }
        
        #endregion

        #region Getting an Experiment Report
        /// <summary>
        /// This is the mechanism for saving experimental parameters which MM doesn't control, but that the hardware controller can monitor
        /// (e.g. oven temperature, vacuum chamber pressure etc).
        /// </summary>

        public Dictionary<String, Object> GetExperimentReport()
        {
            return experimentReporter.GetExperimentReport();
        }

        
        #endregion

        #region Translation stage
        private void armTranslationStageForTimedMotion(MOTMasterScript script)
        {
            tstage.TSConnect();
            Thread.Sleep(50);
            tstage.TSInitialize((double)script.Parameters["TSAcceleration"], (double)script.Parameters["TSDeceleration"],
                (double)script.Parameters["TSDistance"], (double)script.Parameters["TSVelocity"]);
            Thread.Sleep(50);
            //tstage.TSOn();
            Thread.Sleep(50);
            tstage.TSAutoTriggerDisable();
            Thread.Sleep(50);
            //tstage.TSGo();
            //Thread.Sleep(50);
            //tstage.TSAutoTriggerDisable();
            //Thread.Sleep(50);
            //tstage.TSDoAll((double)script.Parameters["TSAcceleration"], (double)script.Parameters["TSDeceleration"],
                //(double)script.Parameters["TSDistanceF"], (double)script.Parameters["TSDistanceB"], (double)script.Parameters["TSVelocity"]);
            tstage.TSLoadExperimentProfile((double)script.Parameters["TSAcceleration"], (double)script.Parameters["TSDeceleration"],
                (double)script.Parameters["TSDistanceF"], (double)script.Parameters["TSDistanceB"], (double)script.Parameters["TSVelocity"]);


        }
        private void disarmAndReturnTranslationStage()
        {
            tstage.TSAutoTriggerEnable();
            //Thread.Sleep(50);
            //tstage.TSReturn(); // This is the hard coded return of the translation stage at the end of running a MM script
            Thread.Sleep(50);
            tstage.TSDisconnect();
        }
        #endregion

        #region Re-Running a script (intended for reloading old scripts)

        /// <summary>
        /// This section is meant to be for the situation when you want to re-run exactly the same pattern
        /// you ran sometime in the past.
        /// armReplicaRun prompts you for a zip file which contains the run you want to replicate. It unzipps the
        /// file into a folder of the same name, picks out the dictionary and the script.
        /// These then get loaded in the usual way through Run().
        /// disposeReplicaRun does some clean up after the experiment is finished.
        /// </summary>

        public void RunReplica()
        {
            armReplicaRun();
            Run();
            disposeReplicaRun();
        }

        private void armReplicaRun()
        {
            string zipPath = ioHelper.SelectSavedScriptPathDialog();
            string outputFolderPath = Path.GetDirectoryName(zipPath) + "\\" +
                Path.GetFileNameWithoutExtension(zipPath) + "\\";

            ioHelper.UnzipFolder(zipPath);
            SetScriptPath(outputFolderPath +
                Path.GetFileNameWithoutExtension(zipPath) + "_script.cs");

            SetDictionaryPath(outputFolderPath +
                Path.GetFileNameWithoutExtension(zipPath) + "_parameters.txt");

            SetReplicaRunBool(true);

        }

        private void disposeReplicaRun()
        {
            SetReplicaRunBool(false);
            ioHelper.DisposeReplicaScript(Path.GetDirectoryName(scriptPath));
        }
        #endregion
    }
}

