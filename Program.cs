using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

#nullable enable

// Note: The project works with .Net 9.0 and is set to use C# 13.0. For some reason the code wouldn't work with .NET 4.8 -- it can't find the stream deck device for some reason.

namespace StreamDeckBrightnessControl
{
    class Program
    {
        static void Main(string[] args)
        {
            int? argBrightness = null;
            bool isDebugMode = false;

            // Check for an integer argument between 0 and 100
            if (args.Length > 0)
            {
                foreach (string arg in args)
                {
                    if (int.TryParse(arg, out int parsedBrightness) && parsedBrightness >= 0 && parsedBrightness <= 100)
                    {
                        argBrightness = parsedBrightness;
                    }
                    else if (arg.Equals("-debug", StringComparison.OrdinalIgnoreCase))
                    {
                        // Handle background mode
                        isDebugMode = true;
                    }
                }
            }

            // Handle background mode. If no args are given, do not run in background mode. If args are given, run in background mode unless debug mode is specified.
            bool runInBackground;
            if (isDebugMode)
                runInBackground = false;    // Dont use background mode if debug is specified no matter what
            else if (argBrightness != null)
                runInBackground = true;     // Run in background mode if brightness is specified with argument without debug mode
            else
                runInBackground = false;    // Default to not running in background mode

            bool isBackgroundMode = ConsoleHandler.HandleBackgroundMode(runInBackground);
            // ------------------------------------------------

            // Show header info to console
            Console.WriteLine("Stream Deck Brightness Control");
            Console.WriteLine("Arguments:");
            Console.WriteLine("  <brightness>   Set brightness (0-100) of the first found Stream Deck device.");
            Console.WriteLine("  -debug         Run in debug mode (no background mode).");
            Console.WriteLine("\n");

            //Console.WriteLine("Finding Stream Deck devices using SetupAPI...");
            List<StreamDeckHandler.StreamDeckInfo> foundDevices = StreamDeckHandler.DeviceFinder.FindStreamDecks();

            if (foundDevices.Count == 0)
            {
                Console.WriteLine("No Stream Deck devices found.");
            }
            else if (argBrightness != null)
            {
                byte brightnessByte = (byte)argBrightness.Value;
                if (brightnessByte <= 100)
                {
                    StreamDeckHandler.StreamDeckInfo targetDevice = foundDevices[0];

                    if (isDebugMode)
                    {
                        Console.WriteLine($"Found {foundDevices.Count} Stream Deck device(s):");
                        Console.WriteLine($"Using device with PID: {targetDevice.ProductId:X4}");
                    }

                    bool result = StreamDeckHandler.NativeHid.SetStreamDeckBrightness(targetDevice.DevicePath, targetDevice.ProductId, brightnessByte);
                    
                    if (result && isDebugMode)
                    {
                        Console.WriteLine($"\nBrightness set to {brightnessByte}%.");
                    }
                    else if (isDebugMode)
                    {
                        Console.WriteLine("\nFailed to set brightness.");
                    }
                }
            }
            else
            {
                Console.WriteLine($"Found {foundDevices.Count} Stream Deck device(s):");
                for (int i = 0; i < foundDevices.Count; i++)
                {
                    Console.WriteLine($"  [{i}] VID: {foundDevices[i].VendorId:X4}, PID: {foundDevices[i].ProductId:X4}");
                    //Console.WriteLine($"      Path: {foundDevices[i].DevicePath}"); // Uncomment to show path
                }

                // --- Use the first found device ---
                StreamDeckHandler.StreamDeckInfo targetDevice = foundDevices[0];
                Console.WriteLine($"\nUsing device with PID: {targetDevice.ProductId:X4}");

                // --- Get brightness from user ---
                byte brightness = 0;
                while (true)
                {
                    Console.Write("\nEnter brightness percentage (0-100): ");
                    string? input = Console.ReadLine();
                    if (input != null && byte.TryParse(input, out brightness) && brightness <= 100)
                    {
                        // Nothing to do here, valid input
                    }
                    else
                    {
                        Console.WriteLine("Invalid input. Please enter a number between 0 and 100.");
                    }

                    // --- Set the brightness ---
                    bool result = StreamDeckHandler.NativeHid.SetStreamDeckBrightness(targetDevice.DevicePath, targetDevice.ProductId, brightness);

                    if (result)
                    {
                        Console.WriteLine("Brightness set successfully.");
                    }
                    else
                    {
                        Console.WriteLine("Failed to set brightness.");
                    }


                }
            }

            if (isDebugMode)
            {
                Console.WriteLine("\nPress Enter to Exit...");
                Console.ReadLine();
            }
        }
    }
}