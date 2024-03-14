using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Thorlabs.MotionControl.DeviceManagerCLI;
using Thorlabs.MotionControl.GenericMotorCLI.Settings;
using Thorlabs.MotionControl.KCube.DCServoCLI;
using MicroEpsilon;
using System.Threading;
using System.IO;
using System.Diagnostics;

namespace ConsoleApp_test
{
    class Program
    {
        static ERR_CODE Error(string location, ref MEDAQLib sensor)
        {
            string errText = "";
            ERR_CODE err = sensor.GetError(ref errText);
            Console.WriteLine(location + " returned error: " + errText);
            Console.WriteLine("Main failed ...");
            //Console.ReadKey(true);
            return err;
        }

        static ERR_CODE Open(ref MEDAQLib sensor)
        {
            Console.WriteLine("Open sensor ...");

            sensor.SetParameterInt("IP_EnableLogging", 0);
            if (sensor.OpenSensorTCPIP("169.254.168.150") != ERR_CODE.ERR_NOERROR)
                return Error("OpenSensorTCPIP", ref sensor);

            return ERR_CODE.ERR_NOERROR;
        }

        static int sValsPerFrame = 0;
        static bool sVideoStreamActive = false;
        static string sVideoSignalName = "";
        static ERR_CODE GetParameters(ref MEDAQLib sensor)
        {
            Console.WriteLine("Get all parameters ...");

            if (sensor.SetIntExecSCmd("Get_AllParameters", "SP_Additional", 1) != ERR_CODE.ERR_NOERROR)
                return Error("SetIntExecSCmd (Get_AllParameters)", ref sensor);

            string serial = "";
            if (sensor.GetParameterString("SA_SerialNumber", ref serial) != ERR_CODE.ERR_NOERROR)
                return Error("GetParameterString (SA_SerialNumber)", ref sensor);
            Console.WriteLine("Sensor SerialNumber: {0}", serial);

            double range = 0;
            if (sensor.GetParameterDouble("SA_Range", ref range) != ERR_CODE.ERR_NOERROR)
                return Error("GetParameterDouble (SA_Range)", ref sensor);
            Console.WriteLine("Sensor range: {0} mm", range);

            return ERR_CODE.ERR_NOERROR;
        }
        static string StrWithIndex(string name, int index)
        {
            return name + index.ToString();
        }

        static ERR_CODE GetTransmittedDataInfo(ref MEDAQLib sensor)
        {
            int maxValsPerFrame = 0, maxOutputIndex = 0, valsPerFrame = -1;

            if (sensor.ExecSCmdGetInt("Get_TransmittedDataInfo", "IA_ValuesPerFrame", ref valsPerFrame) != ERR_CODE.ERR_NOERROR)
                return Error("Get_TransmittedDataInfo", ref sensor);
            sValsPerFrame = valsPerFrame;

            sensor.GetParameterInt("IA_MaxValuesPerFrame", ref maxValsPerFrame);
            sensor.GetParameterInt("IA_MaxOutputIndex", ref maxOutputIndex);
            Console.WriteLine("Sensor transmits {0} of {1} possible values, maximum output index is {2}", valsPerFrame, maxValsPerFrame, maxOutputIndex);

            for (int i = 0; i < valsPerFrame; i++)
            {
                int index = 0;
                double rawRangeMin = 0.0, rawRangeMax = 0.0, scaledRangeMin = 0.0, scaledRangeMax = 0.0;
                string rawName = "", scaledName = "", rawUnit = "", scaledUnit = "";
                sensor.GetParameterString(StrWithIndex("IA_Raw_Name", i + 1), ref rawName);
                sensor.GetParameterString(StrWithIndex("IA_Scaled_Name", i + 1), ref scaledName);
                sensor.GetParameterString(StrWithIndex("IA_Raw_Unit", i + 1), ref rawUnit);
                sensor.GetParameterString(StrWithIndex("IA_Scaled_Unit", i + 1), ref scaledUnit);
                sensor.GetParameterInt(StrWithIndex("IA_Index", i + 1), ref index);
                sensor.GetParameterDouble(StrWithIndex("IA_Raw_RangeMin", i + 1), ref rawRangeMin);
                sensor.GetParameterDouble(StrWithIndex("IA_Scaled_RangeMin", i + 1), ref scaledRangeMin);
                sensor.GetParameterDouble(StrWithIndex("IA_Raw_RangeMax", i + 1), ref rawRangeMax);
                sensor.GetParameterDouble(StrWithIndex("IA_Scaled_RangeMax", i + 1), ref scaledRangeMax);
                Console.WriteLine(" {0,2}: {1} [{2} .. {3} {4}], {5} [{6} .. {7} {8}]", index, rawName, rawRangeMin, rawRangeMax, rawUnit, scaledName, scaledRangeMin, scaledRangeMax, scaledUnit);
            }

            double samplerate = 0.0, datarate = 0.0;
            sensor.GetParameterDouble("IA_Samplerate", ref samplerate);
            sensor.GetParameterDouble("IA_Datarate", ref datarate);
            Console.WriteLine("Samplerate: {0} Hz, Datarate: {1} Hz", samplerate, datarate);

            int videoSignalsPerFrame = 0;
            sensor.GetParameterInt("IA_VideoSignalsPerFrame", ref videoSignalsPerFrame); // This parameter may not exist, so ignore errors
            if (videoSignalsPerFrame > 0)
            {
                sVideoStreamActive = true;

                Console.WriteLine("Sensor transmits {0} video signals", videoSignalsPerFrame);

                for (int i = 0; i < videoSignalsPerFrame; i++)
                {
                    string videoSignalName = "";
                    int videoSignalPixelCount = 0, videoSignalPixelByteSize = 0;
                    sensor.GetParameterString(StrWithIndex("IA_VideoSignalName", i + 1), ref videoSignalName);
                    sensor.GetParameterInt(StrWithIndex("IA_VideoSignalPixelCount", i + 1), ref videoSignalPixelCount);
                    sensor.GetParameterInt(StrWithIndex("IA_VideoSignalPixelByteSize", i + 1), ref videoSignalPixelByteSize);
                    Console.WriteLine("{0} ({1} pixel each {2} bytes) with length {3} bytes", videoSignalName, videoSignalPixelCount, videoSignalPixelByteSize, videoSignalPixelCount * videoSignalPixelByteSize);

                    if (i == 1)
                    {
                        sVideoSignalName = videoSignalName;
                    }
                }
            }

            return ERR_CODE.ERR_NOERROR;
        }

        static ERR_CODE SaveVideoSignal(ref MEDAQLib sensor, string filename)
        {
            int videoSignalsPerFrame = 0;
            sensor.GetParameterInt("IA_VideoSignalsPerFrame", ref videoSignalsPerFrame); // This parameter may not exist
            if (videoSignalsPerFrame > 0)
            {
                sVideoStreamActive = true;
                List<String> sVideoSignalList = new List<String>();
                Console.WriteLine("Sensor transmits {0} video signals", videoSignalsPerFrame);

                for (int i = 0; i < videoSignalsPerFrame; i++)
                {
                    string videoSignalName = "";
                    int videoSignalPixelCount = 0, videoSignalPixelByteSize = 0;
                    sensor.GetParameterString(StrWithIndex("IA_VideoSignalName", i + 1), ref videoSignalName);
                    sensor.GetParameterInt(StrWithIndex("IA_VideoSignalPixelCount", i + 1), ref videoSignalPixelCount);
                    sensor.GetParameterInt(StrWithIndex("IA_VideoSignalPixelByteSize", i + 1), ref videoSignalPixelByteSize);
                    Console.WriteLine("{0} ({1} pixel each {2} bytes) with length {3} bytes", videoSignalName, videoSignalPixelCount, videoSignalPixelByteSize, videoSignalPixelCount * videoSignalPixelByteSize);
                    sVideoSignalList.Add(videoSignalName);
                }
                Console.WriteLine(sVideoSignalList[0]);

                Thread.Sleep(100);

                if (sVideoStreamActive)
                {
                    sensor.SetParameterInt("SP_ReadMode", 2/*Automatic*/);
                    sensor.SetParameterInt("SP_WaitVideoTimeout", 500/*ms*/);
                    if (sensor.ExecSCmd("Get_VideoStreamSignal") != ERR_CODE.ERR_NOERROR)
                        return Error("Get_VideoStreamSignal", ref sensor);

                    byte[] bArray = new byte[512 * 2];
                    short[] videoRaw = new short[512];
                    List<short> VideoList = new List<short>();
                    for (int i = 0; i < 2; i++)
                    {
                        sensor.GetParameterBinary(sVideoSignalList[i], ref bArray);
                        Buffer.BlockCopy(bArray, 0, videoRaw, 0, bArray.Length);
                        VideoList.AddRange(videoRaw);
                    }

                    using (StreamWriter sw = File.CreateText(filename))
                    {
                        for (int i = 0; i < 512; i++)
                        {
                            sw.WriteLine(VideoList[i] + "," + VideoList[i + 512]);
                        }
                    }

                }
            }

            return ERR_CODE.ERR_NOERROR;
        }

        // rotation:
        public static List<string> serialNumbers = new List<string>();
        public static void DetectDevices(int serialNr)
        {
            try
            {
                DeviceManagerCLI.BuildDeviceList();
            }
            catch (Exception)
            {
                Console.WriteLine("Device list failed to build!");
                return;
            }

            serialNumbers = DeviceManagerCLI.GetDeviceList(serialNr);
            try
            {
                Debug.Assert(serialNumbers.Count > 0);
                Console.WriteLine($"A controller with serial number: {serialNumbers[0]} has been detected.");

            }
            catch (Exception)
            {
                Console.WriteLine("No connected devices, please connect one.");
                throw;
            }
        }

        public static void InitializeIt(KCubeDCServo controller, List<string> serialNumbers)
        {
            controller.Connect(serialNumbers[0]);
            controller.WaitForSettingsInitialized(9_000); // (ms)
            MotorConfiguration motorConfiguration = controller.LoadMotorConfiguration(controller.DeviceID,
                DeviceConfiguration.DeviceSettingsUseOptionType.UseFileSettings);
            Console.WriteLine("Device has been initialized.");
        }

        public static void EnableIt(KCubeDCServo controller)
        {
            controller.StartPolling(250); // (ms)
            Thread.Sleep(1_000); // (ms)
            controller.EnableDevice();
            Thread.Sleep(1_000); // (ms)
            Console.WriteLine("Device has been correctly enabled.");
        }

        public static void SetParams(KCubeDCServo controller)
        {
            controller.SetRotationModes(RotationSettings.RotationModes.RotationalRange, RotationSettings.RotationDirections.Quickest);
            //controller.SetJogStepSize(1);
            controller.SetVelocityParams(50, 100);
        }

        public static void ShowInfo(KCubeDCServo controller)
        {
            DeviceInfo deviceInfo = controller.GetDeviceInfo();
            Console.WriteLine($"Controller {deviceInfo.SerialNumber} = {deviceInfo.Name}");
        }

        public static void HomeIt(KCubeDCServo controller)
        {
            Console.WriteLine("Actuator is homing...");
            controller.Home(60_000);
            Console.WriteLine("Actuator homed.");
            Thread.Sleep(1_000);
        }

        public static void GoToAngle(KCubeDCServo controller)
        {
            Console.Write("Please enter angle value (deg): ");
            string u_input = Console.ReadLine();
            while (u_input != "quit" && u_input != "exit")
            {
                decimal theta1;
                bool success = decimal.TryParse(u_input, out theta1);
                if (success)
                {
                    Console.WriteLine("Actuator is moving...");
                    controller.MoveTo(theta1, 60_000); // (deg, ms)
                    Console.WriteLine($"Actuator moved to {theta1} deg.");
                    Thread.Sleep(1_000);
                }
                else
                {
                    Console.WriteLine("Invalid input!");
                }
                Console.Write("Please enter angle value (deg): ");
                u_input = Console.ReadLine();
            }
        }

        public static decimal[] LinearRange(double start, double stop, double step = 1.0)
        {
            Debug.Assert(step != 0, "ERROR: step must not be zero. No Division by zero allowed!");
            int n_elem = (int)((stop - start) / step + 1);
            decimal[] result = new decimal[n_elem];
            decimal elem = System.Convert.ToDecimal(start);
            for (int i = 0; i < n_elem; i++)
            {
                elem = System.Convert.ToDecimal(i * step);
                result[i] = elem;
            }
            return result;
        }





        static void Main(string[] args)
        {
            //Console.WriteLine("easy!");
            //SimulationManager.Instance.InitializeSimulations();
            //string serialNr = "27";
            //DeviceManagerCLI.BuildDeviceList();
            //List<string> serialNumbers = new List<string> { serialNr, serialNr };
            //KCubeDCServo controllerx = KCubeDCServo.CreateKCubeDCServo(serialNr);
            
            // test rotation stage:
            DetectDevices(27);
            KCubeDCServo controller = KCubeDCServo.CreateKCubeDCServo(serialNumbers[0]);

            MEDAQLib sensor = new MEDAQLib("IFC2422");
            Thread.Sleep(4_300);
            Open(ref sensor);
            GetParameters(ref sensor);
            sensor.SetIntExecSCmd("Update_DataOutInterface", "SP_OutputEthernet", 1);
            sensor.SetIntExecSCmd("Update_Output_ETH", "SP_OutputVideoRaw_Ch1_ETH", 1);
            GetTransmittedDataInfo(ref sensor);
            //int angle = 0;

            if (controller != null)
            {
                InitializeIt(controller, serialNumbers);
                EnableIt(controller);
                SetParams(controller);
                ShowInfo(controller);

                Console.WriteLine("Actuator is moving...");
                HomeIt(controller);
                decimal[] thetaSet = LinearRange(0.0, 359.0, 5);
                int Nrepetions = 1;
                for (int i = 0; i < Nrepetions; i += 1)
                {
                    foreach (decimal theta in thetaSet)
                    {
                        controller.MoveTo(theta, 9_000);
                        GetTransmittedDataInfo(ref sensor);
                        string padTheta = theta.ToString();
                        padTheta = padTheta.PadLeft(3, '0');
                        SaveVideoSignal(ref sensor, @"C:\Users\ezznwp\Downloads\test_csv\240215_test" + $"_{padTheta}.csv");
                        Console.WriteLine($"Saved spectrum at angle: {theta} deg...");
                        Thread.Sleep(500);
                    }
                }

                HomeIt(controller);
                Console.WriteLine("Actuator finished movements.");
                Thread.Sleep(2_000);


                // finish operations:
                controller.StopPolling();
                controller.DisableDevice();
                sensor.SetIntExecSCmd("Update_Output_ETH", "SP_OutputVideoRaw_Ch1_ETH", 0);
            }
            else
            {
                Console.WriteLine("No device was found!");
                return;
            }
            controller.Disconnect(false);



            // test optical sensor:
            //Console.WriteLine("Start Main...");
            //MEDAQLib sensor = new MEDAQLib("IFC2422");
            //Thread.Sleep(4_300);
            //Open(ref sensor);
            //GetParameters(ref sensor);
            //sensor.SetIntExecSCmd("Update_DataOutInterface", "SP_OutputEthernet", 1);
            //sensor.SetIntExecSCmd("Update_Output_ETH", "SP_OutputVideoRaw_Ch1_ETH", 1);
            //GetTransmittedDataInfo(ref sensor);
            //int angle = 0;
            //while (angle <= 5)
            //{
            //    GetTransmittedDataInfo(ref sensor);
            //    SaveVideoSignal(ref sensor, @"C:\Users\ezznwp\Downloads\test_csv\240119_test" + $"_{angle}.csv");
            //    Console.WriteLine($"saved spectrum at angle {angle} deg...");
            //    Thread.Sleep(1_000);
            //    angle += 1;
            //}

            //sensor.SetIntExecSCmd("Update_Output_ETH", "SP_OutputVideoRaw_Ch1_ETH", 0);

            //Console.WriteLine("Main successfully finished ...");
            //Console.ReadKey();
        }
    }
}
